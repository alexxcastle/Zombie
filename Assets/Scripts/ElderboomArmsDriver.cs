using System.Collections.Generic;
using UnityEngine;

// Plays the Basic FPS clips on the Tactical arms (one Animator, one controller per weapon family:
// Resources/Elderboom/TacticalArms/<Family>.controller, states named after the clip role) and adapts them to the
// weapon actually held (ElderboomWeaponHandling profile):
//  1. grip: the weapon's own grip sockets (Grip_R / Grip_L, or the weapon pack's palm markers) against the reference
//     hold of the clips; both hands are moved onto them with a two-bone IK while they hold the weapon, and follow
//     the weapon when it is moved in the hands (hand-tuned offsets, procedural actions);
//  2. aim: the ADS clips, with the sight line locked on the view axis (smoothed, so the clips' motion and the recoil
//     stay visible) at the sight's own eye relief;
//  3. fire: an additive recoil spring per weapon; the Fire clip only where it carries the action (handguns);
//  4. weapons without a matching clip set: procedural pump stroke, revolver and heavy-weapon reloads;
//  5. reloads expose their events (magazine in, each shell in) so ammo follows the animation and can be interrupted.
public sealed class ElderboomArmsDriver : MonoBehaviour
{
    public Animator animator;
    public string family, weaponId;
    ElderboomWeaponDefinition definition;
    ElderboomWeaponHandling.Profile profile = new ElderboomWeaponHandling.Profile();
    bool aiming; float aimWeight;
    Vector3 basePos, appliedPos = new Vector3(float.NaN, 0, 0); Quaternion baseRot, appliedRot;
    Transform arms, weaponNode, magNode, handR;
    readonly List<(Transform t, Vector3 p, Quaternion r)> magBase = new List<(Transform, Vector3, Quaternion)>();
    [System.Serializable] class RoleTable { public string[] roles; public float[] lengths; }
    readonly Dictionary<string, float> lengths = new Dictionary<string, float>();
    readonly Queue<(string role, float speed)> queue = new Queue<(string, float)>();
    string current = "", locoWant = "Idle";
    bool acting;
    float actionEnd;

    // ---- grip (index 0 = right, 1 = left)
    readonly Transform[] upper = new Transform[2], fore = new Transform[2], foreTwist = new Transform[2], hand = new Transform[2];
    readonly Transform[][] fistBones = new Transform[2][];
    readonly Vector3[] refFist = new Vector3[2], gripDelta = new Vector3[2], socketLocal = new Vector3[2];
    readonly bool[] hasGrip = new bool[2], hasSocket = new bool[2];
    Vector3 boreLocal = Vector3.forward;
    Transform pumpPart; Vector3 pumpBase;
    Vector3 reachLocal; bool hasReach;
    readonly Dictionary<string, float> insertAt = new Dictionary<string, float>();

    // ---- aim
    Transform rear, front, upMark; float relief = .11f, sightsCheckedAt = -10f;
    Quaternion lockRot = Quaternion.identity; Vector3 lockPos; bool lockLive;

    // ---- recoil spring (position x side / y up / z forward, rotation degrees x pitch / y yaw / z roll)
    Vector3 kp, kpv, kr, krv;

    // ---- procedural actions
    enum Proc { None, Pump, Revolver, Heavy }
    Proc proc; float procStart, procLength, procFadeFrom = -1f;

    public void Init(Animator a, string fam, string id)
    {
        animator = a; family = fam; weaponId = id;
        definition = ElderboomWeapons.Find(id);
        profile = ElderboomWeaponHandling.For(definition);
        arms = a.transform; weaponNode = arms.Find("Weapon");
        magNode = weaponNode != null ? weaponNode.Find("Magazine") : null;
        handR = Deep(arms, "DEF-hand.R");
        if (weaponNode != null)
            foreach (var n in new[] { "Magazine", "MagazineSpare" })
            {
                var node = weaponNode.Find(n); if (node == null) continue;
                foreach (Transform c in node) magBase.Add((c, c.localPosition, c.localRotation));
            }
        var text = Resources.Load<TextAsset>("Elderboom/TacticalArms/" + fam + "_roles");
        if (text != null)
        {
            var t = JsonUtility.FromJson<RoleTable>(text.text);
            for (int i = 0; i < t.roles.Length; i++) lengths[t.roles[i]] = t.lengths[i];
        }
        string[] sides = { "R", "L" };
        for (int s = 0; s < 2; s++)
        {
            upper[s] = Deep(arms, "DEF-upper_arm." + sides[s]);
            fore[s] = Deep(arms, "DEF-forearm." + sides[s]);
            foreTwist[s] = Deep(arms, "DEF-forearm." + sides[s] + ".001");
            hand[s] = Deep(arms, "DEF-hand." + sides[s]);
            fistBones[s] = new[] { Deep(arms, "DEF-f_middle.01." + sides[s]), Deep(arms, "DEF-f_index.02." + sides[s]), Deep(arms, "DEF-f_middle.02." + sides[s]),
                                   Deep(arms, "DEF-f_ring.02." + sides[s]), Deep(arms, "DEF-thumb.03." + sides[s]) };
        }
        Calibrate();
    }

    static Transform Deep(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
    static Transform DeepAny(Transform r, params string[] names) { foreach (var n in names) { var t = Deep(r, n); if (t != null) return t; } return null; }

    AnimationClip Clip(string role)
    {
        var ctrl = animator != null ? animator.runtimeAnimatorController : null;
        if (ctrl == null) return null;
        foreach (var c in ctrl.animationClips) if (c != null && c.name.EndsWith("_" + role)) return c;
        return null;
    }

    // Centre of the closed hand: palm (wrist .. middle knuckle) and the fingers around the grip.
    Vector3 Fist(int s)
    {
        var h = hand[s]; if (h == null) return Vector3.zero;
        var b = fistBones[s];
        Vector3 palm = b[0] != null ? (h.position + b[0].position * 2f) / 3f : h.position;
        Vector3 sum = palm; int n = 1;
        for (int i = 1; i < b.Length; i++) if (b[i] != null) { sum += b[i].position; n++; }
        return sum / n;
    }

    // Reference hold (Idle, first frame) against the weapon's own grip sockets; bore axis; pump; reload events.
    void Calibrate()
    {
        if (weaponNode == null) return;
        var idle = Clip("Idle");
        if (idle != null) idle.SampleAnimation(arms.gameObject, 0f);
        var sockets = new[]
        {
            DeepAny(weaponNode, "Grip_R", "RightHand_Grip", "R_palm", "palm_2", "R_wrist"),
            DeepAny(weaponNode, "Grip_L", "LeftHand_Support", "L_palm", "palm", "L_wrist"),
        };
        float invScale = 1f / Mathf.Max(1e-4f, weaponNode.lossyScale.x);
        for (int s = 0; s < 2; s++)
        {
            if (hand[s] == null || upper[s] == null || fore[s] == null) continue;
            refFist[s] = weaponNode.InverseTransformPoint(Fist(s));
            hasGrip[s] = true;
            if (sockets[s] != null)
            {
                // Allow up to 0.38 m offset so real weapon handles (minigun/flame/m4 foregrip) are reached without clipping off
                gripDelta[s] = Vector3.ClampMagnitude(weaponNode.InverseTransformPoint(sockets[s].position) - refFist[s], .38f * invScale);
                socketLocal[s] = refFist[s] + gripDelta[s];
                hasSocket[s] = true;
            }
        }
        FindSights();
        if (rear != null && front != null) boreLocal = weaponNode.InverseTransformDirection(front.position - rear.position).normalized;
        else boreLocal = weaponNode.InverseTransformDirection(arms.forward).normalized;
        Vector3 upLocal = weaponNode.InverseTransformDirection(arms.up).normalized;
        if (profile.pump)
            foreach (var t in weaponNode.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if ((n.Contains("pump") || n.Contains("forend") || n.Contains("fore_end") || n.Contains("foregrip")) && t.GetComponentInChildren<Renderer>(true) != null) { pumpPart = t; pumpBase = t.localPosition; break; }
            }
        // where the support hand reaches during a procedural reload: the cylinder (revolver) or the feed/canister (heavy weapons)
        Transform reach = null;
        if (profile.reload == ElderboomWeaponHandling.Reload.Revolver)
            foreach (var t in weaponNode.GetComponentsInChildren<Transform>(true)) { string n = t.name.ToLowerInvariant(); if (n.Contains("cylinder") || n.Contains("drum") || n.Contains("tamburo")) { reach = t; break; } }
        if (reach == null && profile.reload == ElderboomWeaponHandling.Reload.Heavy)
        {
            if (magNode != null && magNode.GetComponentInChildren<Renderer>(true) != null) reach = magNode;
            else foreach (var t in weaponNode.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n == "shell" || n == "canister" || n == "lid" || n == "unloader") { reach = t; break; }
            }
        }
        if (reach != null) { reachLocal = weaponNode.InverseTransformPoint(reach.position); hasReach = true; }
        else if (hasGrip[0])
        {
            if (profile.reload == ElderboomWeaponHandling.Reload.Revolver)
                reachLocal = refFist[0] + boreLocal * (.075f * invScale) + upLocal * (.04f * invScale);
            else
                reachLocal = refFist[0] + boreLocal * (.14f * invScale) + upLocal * (.02f * invScale);
            hasReach = true;
        }
        // magazine-in moment of each magazine reload clip: the magazine comes back to where it sits in the weapon
        foreach (var role in new[] { "Reload", "ReloadEmpty", "ADSReload", "ADSReloadEmpty" })
        {
            var c = Clip(role); if (c == null || magNode == null) continue;
            c.SampleAnimation(arms.gameObject, 0f);
            Vector3 home = magNode.localPosition; bool away = false; float t = -1f;
            float scale = Mathf.Max(1e-4f, weaponNode.lossyScale.x);
            for (int i = 1; i <= 60; i++)
            {
                float time = c.length * i / 60f; c.SampleAnimation(arms.gameObject, time);
                float d = (magNode.localPosition - home).magnitude * scale;
                if (d > .03f) away = true;
                else if (away && d < .006f) { t = time; away = false; }
            }
            insertAt[role] = t > 0f ? t : c.length * .7f;
        }
        if (idle != null) idle.SampleAnimation(arms.gameObject, 0f);
    }

    void FindSights()
    {
        sightsCheckedAt = Time.time;
        rear = front = upMark = null;
        if (weaponNode == null) return;
        foreach (var t in weaponNode.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "MainWeapon_ADS_Rear") rear = t;
            else if (t.name == "MainWeapon_ADS_Front") front = t;
            else if (t.name == "MainWeapon_ADS_Up") upMark = t;
        }
        relief = ElderboomWeaponHandling.Relief(profile, weaponNode);
    }

    public void SetAim(bool active) => aiming = active;
    public bool LocksSights => profile.lockSights;
    public Vector3 FistPosition(int side) => side >= 0 && side < 2 ? Fist(side) : Vector3.zero;
    public Transform WeaponNode => weaponNode;
    public Transform Arms => arms;

    // ------------------------------------------------------------------ per frame, after the clips
    void LateUpdate()
    {
        if (arms == null) return;
        float dt = Time.deltaTime;
        float aimTime = Mathf.Max(.05f, ElderboomWeaponMods.AimTime(definition));
        aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, dt / aimTime);
        float aw = Mathf.SmoothStep(0f, 1f, aimWeight);
        var all = ElderboomArmsOffsets.Get("*"); var own = ElderboomArmsOffsets.Get(weaponId ?? "");
        // framing (hand-tuned hip offsets)
        arms.localPosition = all.hipPos + own.hipPos;
        arms.localRotation = Quaternion.Euler(own.hipEuler) * Quaternion.Euler(all.hipEuler);

        Matrix4x4 before = Matrix4x4.identity, after = Matrix4x4.identity;
        if (weaponNode != null)
        {
            // the clips write the Weapon node every frame; if a frame did not, start again from the clip's pose
            if (weaponNode.localPosition == appliedPos && weaponNode.localRotation == appliedRot) { weaponNode.localPosition = basePos; weaponNode.localRotation = baseRot; }
            basePos = weaponNode.localPosition; baseRot = weaponNode.localRotation;
            before = weaponNode.localToWorldMatrix;
            // the weapon moved in the hands: hand-tuned offset + procedural action, both around the right fist
            Quaternion q = Quaternion.Euler(own.weaponEuler); Vector3 move = own.weaponPos;
            ProcWeapon(out Quaternion pq, out Vector3 pm);
            q = pq * q; move += pm;
            if (move != Vector3.zero || q != Quaternion.identity)
            {
                Vector3 pivot = hasGrip[0] ? arms.InverseTransformPoint(Fist(0)) : handR != null ? arms.InverseTransformPoint(handR.position) : Vector3.zero;
                Vector3 p = arms.InverseTransformPoint(weaponNode.position);
                Quaternion r = Quaternion.Inverse(arms.rotation) * weaponNode.rotation;
                weaponNode.position = arms.TransformPoint(pivot + q * (p - pivot) + move);
                weaponNode.rotation = arms.rotation * (q * r);
            }
            after = weaponNode.localToWorldMatrix;
            appliedPos = weaponNode.localPosition; appliedRot = weaponNode.localRotation;
        }
        if (magBase.Count > 0)
            foreach (var (t, bp, br) in magBase)
            {
                if (t == null || t.parent == null) continue;
                Vector3 d = t.parent.InverseTransformVector(arms.TransformVector(own.magPos));
                Quaternion e = Quaternion.Inverse(t.parent.rotation) * arms.rotation * Quaternion.Euler(own.magEuler) * Quaternion.Inverse(arms.rotation) * t.parent.rotation;
                t.localPosition = bp + d; t.localRotation = e * br;
            }
        Vector3 pumpLocal = PumpOffset();
        if (pumpPart != null && pumpPart.parent != null)
            pumpPart.localPosition = pumpBase + pumpPart.parent.InverseTransformVector(weaponNode.TransformVector(pumpLocal));
        if (weaponNode != null) Grip(before, after, pumpLocal);
        if (Time.time - sightsCheckedAt > 1f || (aiming && (rear == null || front == null))) FindSights();
        LockSights(aw, dt);
        Recoil(aw, dt);
        // hand-tuned aim correction, on top of the locked sight line
        if (aw > 0f && (own.adsPos != Vector3.zero || own.adsEuler != Vector3.zero))
        {
            arms.localPosition += own.adsPos * aw;
            arms.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(own.adsEuler), aw) * arms.localRotation;
        }
    }

    // Both hands onto the weapon: where a hand holds the reference weapon in the clip (its fist within a few
    // centimetres of the reference hold), it is moved with the weapon and onto this weapon's grip socket.
    void Grip(Matrix4x4 before, Matrix4x4 after, Vector3 pumpLocal)
    {
        var beforeInv = before.inverse;
        Quaternion turn = after.rotation * Quaternion.Inverse(before.rotation);
        float reach = ReachWeight();
        for (int s = 0; s < 2; s++)
        {
            if (!hasGrip[s]) continue;
            Vector3 fist = Fist(s);
            Vector3 clipLocal = beforeInv.MultiplyPoint3x4(fist);
            float d = (clipLocal - refFist[s]).magnitude * Mathf.Max(1e-4f, weaponNode.lossyScale.x);
            // Keep both hands glued to real sockets in steady hold (Idle / ADSIdle); release smoothly during reload/draw clips
            float w = (!acting && proc == Proc.None) ? 1f : 1f - Mathf.Clamp01((d - .05f) / .1f);
            Vector3 hp = hand[s].position; Quaternion hr = hand[s].rotation;
            Vector3 pumpAdd = s == 1 ? pumpLocal : Vector3.zero;
            Vector3 desired;
            if (hasSocket[s])
            {
                Vector3 wantSock = after.MultiplyPoint3x4(socketLocal[s] + pumpAdd);
                desired = wantSock - turn * (fist - hp);
            }
            else
            {
                Vector3 rigid = after.MultiplyPoint3x4(beforeInv.MultiplyPoint3x4(hp));
                desired = rigid + after.MultiplyVector(gripDelta[s] + pumpAdd);
            }
            Vector3 target = Vector3.Lerp(hp, desired, w);
            Quaternion rot = Quaternion.Slerp(hr, turn * hr, w);
            if (s == 1 && reach > 0f && hasReach)
            {
                Vector3 want = after.MultiplyPoint3x4(reachLocal) - turn * (fist - hp);
                target = Vector3.Lerp(target, want, reach);
            }
            // Clamp IK target within the arm's anatomical reach so elbows never flip or stretch the forearm mesh.
            Vector3 pa = upper[s].position;
            float lab = (fore[s].position - pa).magnitude;
            float lbc = (hp - fore[s].position).magnitude;
            if (lab > 1e-5f && lbc > 1e-5f)
            {
                float maxReach = (lab + lbc) * .96f;
                float minReach = Mathf.Abs(lab - lbc) + .02f;
                Vector3 toT = target - pa;
                float dist = toT.magnitude;
                if (dist > maxReach && dist > 1e-5f) target = pa + toT * (maxReach / dist);
                else if (dist < minReach && dist > 1e-5f) target = pa + toT * (minReach / dist);
            }
            if ((target - hp).sqrMagnitude < 1e-10f && Quaternion.Angle(rot, hr) < .01f) continue;
            // Relax forearm twist bone before IK so TwoBone never pinches the sleeve/wrist (Rigify DEF-forearm.001)
            Quaternion twistLocalKeep = foreTwist[s] != null ? foreTwist[s].localRotation : Quaternion.identity;
            if (foreTwist[s] != null) foreTwist[s].localRotation = Quaternion.Slerp(Quaternion.identity, twistLocalKeep, 0.35f);
            TwoBone(upper[s], fore[s], hand[s], target);
            hand[s].rotation = rot;
            // Smoothly distribute residual wrist twist onto DEF-forearm.001 along the forearm axis to prevent candy-wrapper pinching
            if (foreTwist[s] != null)
            {
                Vector3 axis = (hand[s].position - fore[s].position).normalized;
                if (axis.sqrMagnitude > 0.5f)
                {
                    Quaternion rel = Quaternion.Inverse(fore[s].rotation) * hand[s].rotation;
                    Vector3 localAxis = Quaternion.Inverse(fore[s].rotation) * axis;
                    Vector3 proj = Vector3.Project(new Vector3(rel.x, rel.y, rel.z), localAxis);
                    Quaternion twist = new Quaternion(proj.x, proj.y, proj.z, rel.w).normalized;
                    foreTwist[s].localRotation = Quaternion.Slerp(Quaternion.identity, twist, 0.5f);
                    hand[s].rotation = rot;
                }
            }
        }
    }

    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target)
    {
        Vector3 pa = a.position, pb = b.position, pc = c.position;
        float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude;
        if (lab < 1e-5f || lbc < 1e-5f) return;
        float lat = Mathf.Clamp((target - pa).magnitude, Mathf.Abs(lab - lbc) + 1e-4f, (lab + lbc) * .98f);
        Vector3 n = Vector3.Cross(pb - pa, pc - pb);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(pc - pa, Vector3.up);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.right;
        n.Normalize();
        float cur = Vector3.Angle(pa - pb, pc - pb);
        float want = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2f * lab * lbc), -1f, 1f)) * Mathf.Rad2Deg;
        b.rotation = Quaternion.AngleAxis(cur - want, n) * b.rotation;
        a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;
    }

    // Aim: the sight line on the view axis, the rear sight at its eye relief. Smoothed, so the ADS clips' motion and
    // the recoil keep showing; the lock comes in with the aim weight.
    void LockSights(float aw, float dt)
    {
        if (!profile.lockSights || aw <= 0f || rear == null || front == null || arms.parent == null) { lockLive = false; return; }
        var space = arms.parent;
        Vector3 r = space.InverseTransformPoint(rear.position), f = space.InverseTransformPoint(front.position);
        Vector3 dir = f - r; if (dir.sqrMagnitude < 1e-8f) return;
        Vector3 up = upMark != null ? space.InverseTransformPoint(upMark.position) - r : Vector3.up;
        up = Vector3.ProjectOnPlane(up, dir); if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
        Quaternion q = Quaternion.Inverse(Quaternion.LookRotation(dir, up));
        Vector3 rr = q * r;
        Vector3 t = new Vector3(-rr.x, -rr.y, relief - rr.z);
        if (!lockLive) { lockRot = q; lockPos = t; lockLive = true; }
        float k = 1f - Mathf.Exp(-9f * dt);
        lockRot = Quaternion.Slerp(lockRot, q, k); lockPos = Vector3.Lerp(lockPos, t, k);
        Quaternion qw = Quaternion.Slerp(Quaternion.identity, lockRot, aw);
        arms.localRotation = qw * arms.localRotation;
        arms.localPosition = qw * arms.localPosition + lockPos * aw;
    }

    // Additive recoil: a damped spring per axis, kicked by every shot, applied around the right fist.
    void Recoil(float aw, float dt)
    {
        float w = profile.frequency, z = profile.damping;
        float remaining = Mathf.Min(dt, .25f);
        while (remaining > 0f)
        {
            float st = Mathf.Min(remaining, 1f / 240f);
            kpv += (-w * w * kp - 2f * z * w * kpv) * st; kp += kpv * st;
            krv += (-w * w * kr - 2f * z * w * krv) * st; kr += krv * st;
            remaining -= st;
        }
        if (kp.sqrMagnitude < 1e-12f && kr.sqrMagnitude < 1e-8f) return;
        var space = arms.parent; if (space == null) return;
        Vector3 pivot = hasGrip[0] ? space.InverseTransformPoint(Fist(0)) : arms.localPosition;
        float back = aw > 0f ? Mathf.Lerp(1f, .55f, aw) : 1f, turn = aw > 0f ? Mathf.Lerp(1f, .7f, aw) : 1f;
        Quaternion q = Quaternion.Euler(kr.x * turn, kr.y * turn, kr.z * turn);
        arms.localRotation = q * arms.localRotation;
        arms.localPosition = pivot + q * (arms.localPosition - pivot) + new Vector3(kp.x, kp.y, kp.z * back);
    }

    void Kick()
    {
        float scale = ElderboomWeaponMods.RecoilScale(definition);
        float w = profile.frequency * 1.7f * scale;
        kpv += new Vector3(Random.Range(-.15f, .15f) * profile.back, .12f * profile.back, -profile.back) * w;
        krv += new Vector3(-profile.pitch, Random.Range(-profile.yaw, profile.yaw), Random.Range(-profile.roll, profile.roll)) * w;
    }

    // ------------------------------------------------------------------ procedural actions
    float ProcT => procLength > 0f ? Mathf.Clamp01((Time.time - procStart) / procLength) : 1f;
    float ProcFade => procFadeFrom < 0f ? 1f : 1f - Mathf.Clamp01((Time.time - procFadeFrom) / .2f);
    void StartProc(Proc p, float length) { proc = p; procStart = Time.time; procLength = length; procFadeFrom = -1f; }
    void UpdateProc() { if (proc != Proc.None && (ProcT >= 1f || ProcFade <= 0f)) proc = Proc.None; }

    static float Bell(float t, float a, float b, float c, float d) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c, d, t)));

    void ProcWeapon(out Quaternion q, out Vector3 move)
    {
        q = Quaternion.identity; move = Vector3.zero;
        UpdateProc();
        if (proc != Proc.Revolver && proc != Proc.Heavy) return;
        float t = ProcT, w = Bell(t, 0f, .2f, .82f, 1f) * ProcFade;
        if (proc == Proc.Revolver)
        {   // keep the revolver clearly in view: slight nose-up, canted so the cylinder faces the left hand
            q = Quaternion.Euler(-12f * w, 16f * w, 34f * w);
            move = new Vector3(-.03f, -.01f, -.04f) * w;
            // the speedloader goes in: a small push of the weapon
            float push = Bell(t, .5f, .56f, .6f, .68f) * ProcFade;
            move += new Vector3(.006f, -.008f, 0f) * push;
        }
        else
        {   // lifted slightly into frame and rolled toward the support hand so the feed/canister stays visible
            float shake = Bell(t, .45f, .5f, .58f, .64f) * ProcFade;
            q = Quaternion.Euler(-6f * w + 2.5f * shake, -14f * w, -22f * w);
            move = new Vector3(-.03f, .055f - .01f * shake, -.02f) * w;
        }
    }

    float ReachWeight()
    {
        if (proc != Proc.Revolver && proc != Proc.Heavy) return 0f;
        return Bell(ProcT, .18f, .32f, .74f, .88f) * ProcFade;
    }

    Vector3 PumpOffset()
    {
        if (proc != Proc.Pump) return Vector3.zero;
        float t = Mathf.Clamp01((Time.time - procStart - .08f) / Mathf.Max(.05f, procLength - .08f));
        float scale = weaponNode != null ? Mathf.Max(1e-4f, weaponNode.lossyScale.x) : 1f;
        return -boreLocal * (.085f / scale) * Mathf.Sin(Mathf.PI * t) * ProcFade;
    }

    // ------------------------------------------------------------------ actions
    public bool Has(string role) => lengths.ContainsKey(role);
    float Len(string role) => lengths.TryGetValue(role, out var l) ? l : 1f;

    void Play(string role, float fade, float speed)
    {
        if (animator == null || !Has(role)) return;
        animator.speed = speed;
        if (fade <= 0f) animator.Play(role, 0, 0f); else animator.CrossFadeInFixedTime(role, fade, 0, 0f);
        current = role;
    }
    void Act(string role, float speed, float fade)
    {
        queue.Clear();
        Play(role, fade, speed);
        acting = true; actionEnd = Time.time + Len(role) / speed;
    }

    public void ResetIdle()
    {
        queue.Clear(); acting = false; proc = Proc.None;
        kp = kpv = kr = krv = Vector3.zero; lockLive = false;
        if (animator != null && animator.isActiveAndEnabled) Play(Has(locoWant) ? locoWant : "Idle", 0f, LocoSpeed(locoWant));
    }
    public void Draw() { proc = Proc.None; if (Has("Draw")) Act("Draw", 1.25f, 0f); }

    // Every shot: the additive recoil; the Fire clip where it carries the action; the pump stroke.
    public bool Fire()
    {
        Kick();
        if (profile.fire == ElderboomWeaponHandling.Fire.Clip)
        {
            string role = aiming && Has("ADSFire") ? "ADSFire" : "Fire";
            if (Has(role)) Act(role, 1f, .02f);
        }
        if (profile.pump) StartProc(Proc.Pump, .5f);
        return true;
    }

    public sealed class ReloadPlan
    {
        public float duration;
        public bool tube;
        // seconds from the start; amount -1 = the magazine goes in (fill), otherwise shells
        public readonly List<(float time, int amount)> events = new List<(float, int)>();
    }

    // Reload with its events. Tube: start, one loop per shell (a shell in each), end. Magazine: the family clip,
    // the magazine going in where the clip puts it. Revolver / heavy weapons: procedural over the idle.
    public ReloadPlan Reload(bool empty, int shells)
    {
        var plan = new ReloadPlan();
        proc = Proc.None;
        if (profile.reload == ElderboomWeaponHandling.Reload.Tube && Has("ReloadLoop"))
        {
            plan.tube = true;
            const float sp = 1.2f;
            string start = empty && Has("ReloadStartEmpty") ? "ReloadStartEmpty" : "ReloadStart";
            int n = Mathf.Clamp(shells, 1, 12);
            Act(start, sp, .08f);
            float total = Len(start) / sp;
            int loops = n;
            if (start == "ReloadStartEmpty") { plan.events.Add((total * .8f, 1)); loops = n - 1; }
            for (int i = 0; i < loops; i++)
            {
                queue.Enqueue(("ReloadLoop", sp));
                plan.events.Add((total + Len("ReloadLoop") / sp * .62f, 1));
                total += Len("ReloadLoop") / sp;
            }
            if (Has("ReloadEnd")) { queue.Enqueue(("ReloadEnd", sp)); total += Len("ReloadEnd") / sp; }
            actionEnd = Time.time + Len(start) / sp;
            plan.duration = total;
            return plan;
        }
        if (profile.reload == ElderboomWeaponHandling.Reload.Revolver || profile.reload == ElderboomWeaponHandling.Reload.Heavy)
        {
            queue.Clear(); acting = false;
            float len = profile.reloadSeconds;
            StartProc(profile.reload == ElderboomWeaponHandling.Reload.Revolver ? Proc.Revolver : Proc.Heavy, len);
            plan.duration = len;
            plan.events.Add((len * .6f, -1));
            return plan;
        }
        string role = empty && Has("ReloadEmpty") ? "ReloadEmpty" : "Reload";
        if (!Has(role)) { plan.duration = 2.3f; plan.events.Add((2.3f * .7f, -1)); return plan; }
        float clipLen = Len(role), speed = Mathf.Max(1f, clipLen / (empty ? 3.4f : 3.0f));
        Act(role, speed, .08f);
        plan.duration = clipLen / speed;
        float at = insertAt.TryGetValue(role, out var ins) ? ins : clipLen * .7f;
        plan.events.Add((at / speed, -1));
        return plan;
    }

    // Reload interrupted (fire on a tube, sprint, weapon switch): back to the locomotion pose.
    public void CancelReload()
    {
        queue.Clear();
        if (proc == Proc.Revolver || proc == Proc.Heavy) procFadeFrom = Time.time;
        if (acting) { acting = false; if (animator != null && animator.isActiveAndEnabled) Play(locoWant, .15f, LocoSpeed(locoWant)); }
    }

    public void SetLocomotion(float amount, bool sprinting)
    {
        locoWant = aiming && profile.lockSights && Has("ADSIdle") ? "ADSIdle"
                 : sprinting && amount > .3f && Has("Sprint") ? "Sprint"
                 : amount > .35f && Has("Walk") ? "Walk" : "Idle";
    }
    float LocoSpeed(string role) => role == "Idle" || role == "ADSIdle" || role == "Walk" ? profile.idleSpeed : 1f;

    void Update()
    {
        if (animator == null) return;
        if (acting)
        {
            if (Time.time < actionEnd) return;
            if (queue.Count > 0)
            {
                var (role, speed) = queue.Dequeue();
                Play(role, .05f, speed); actionEnd = Time.time + Len(role) / speed;
                return;
            }
            acting = false;
            Play(locoWant, .2f, LocoSpeed(locoWant));
            return;
        }
        if (current != locoWant) { Play(locoWant, current == "ADSIdle" || locoWant == "ADSIdle" ? .15f : .25f, LocoSpeed(locoWant)); return; }
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && st.normalizedTime >= .999f) Play(locoWant, 0f, LocoSpeed(locoWant)); // loop
    }
}
