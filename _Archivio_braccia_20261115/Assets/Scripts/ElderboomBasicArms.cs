using System;
using System.Collections.Generic;
using UnityEngine;

// Every weapon is held by the operator's own arms (Resources/Elderboom/Grips/<Op>_Grips, re-bound to the Basic FPS
// rig's deform skeleton) animated by the 69 Basic FPS rig clips (Assets/GameAssets/BasicFPSAnimations, Generic) through
// one Animator Controller shared by the three operators (Resources/Elderboom/BasicArms/BasicArms).
//
// The Animator sits on the arms' armature node, so the clips' paths ("root/DEF-torso/...") are the same for every
// operator. The clips also drive "Weapon" (the reference weapon's body) and "Weapon/Magazine" / "Weapon/MagazineSpare"
// (its magazines; hidden = near-zero scale). The visible model is calibrated using its own hand sockets,
// then pinned to the animated grip while keeping the clip's independent magazine and shell motion.
public sealed class ElderboomBasicArms : MonoBehaviour
{
    // Kept non-const so the legacy implementation remains compilable, while the shipped
    // Basic-rig path stays animation-driven until a rig-specific solver is authored.
    static bool RuntimeContactSolving => false;
    public const string ControllerResource = "Elderboom/BasicArms/BasicArms";
    const string MetaFolder = "Elderboom/BasicArms/";
    // Basic FPS rig camera (P_eyes): 79.61 deg horizontal. The game frames weapons at 80 deg horizontal.
    const float BasicHorizontalFov = 79.61f;

    [Serializable] public class Clip { public string name, role, file; public int frames; public float length; }
    [Serializable] public class Ref { public Vector3[] gripR, gripL; public Vector3 eye, forward, up; }
    [Serializable] public class Meta { public string family, magazine, spare; public Clip[] clips; public bool hasIdle, hasAds; public Ref idle, ads; }

    static readonly Dictionary<string, Meta> metas = new Dictionary<string, Meta>();
    public static Meta LoadMeta(string family)
    {
        if (metas.TryGetValue(family, out var m)) return m;
        var text = Resources.Load<TextAsset>(MetaFolder + family + "_meta");
        m = text != null ? JsonUtility.FromJson<Meta>(text.text) : null;
        metas[family] = m;
        return m;
    }

    // Which reference weapon's clips each game weapon uses.
    public static string FamilyFor(ElderboomWeaponDefinition w)
    {
        if (w == null) return "AR10";
        switch (w.id)
        {
            case "rhino": case "m1911": case "glock19": case "p45": return "Deagle";
            case "shotgun": case "mp133": case "striker": case "mosin": return "Winchester";
            case "mp7": return "SR2M";
            case "hk53": case "mp5": case "mp5vk": case "a5": return "XBR9";
        }
        if (w.category == ElderboomIds.Category.Melee || w.category == ElderboomIds.Category.Handgun) return "Deagle";
        if (w.category == ElderboomIds.Category.Shotgun) return "Winchester";
        return "AR10";
    }

    // ------------------------------------------------------------------ build
    // Build also runs in the editor (animation previews), where Destroy is not allowed.
    static void Kill(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    public static Transform Build(Transform operatorTemplate, ElderboomOperatorDefinition op, ElderboomWeaponDefinition weapon, Transform parent)
    {
        if (op == null || weapon == null || parent == null) return null;
        string family = FamilyFor(weapon);
        var meta = LoadMeta(family);
        var controller = Resources.Load<RuntimeAnimatorController>(ControllerResource);
        var gripsPrefab = Resources.Load<GameObject>(ElderboomAuthoredGrip.ResourcePath(op.id));
        if (meta == null || controller == null || gripsPrefab == null)
        {
            Debug.LogWarning("[Elderboom] Basic arms: missing " + (meta == null ? "meta " + family : controller == null ? "controller" : "arms " + op.id));
            return null;
        }
        // Weapon geometry source: the weapon's own prefab, or (operator primaries) that operator's viewmodel.
        GameObject packPrefab = string.IsNullOrEmpty(weapon.resource) ? null : Resources.Load<GameObject>(weapon.resource);
        ElderboomOperatorDefinition source = null;
        if (packPrefab == null && weapon.IsOperatorPrimary)
        {
            source = ElderboomOperators.Find(weapon.operatorId);
            var pivot = source != null ? ElderboomAuthoredGrip.FindPivot(parent, source.id) : null;
            if (pivot != null) packPrefab = pivot.gameObject;
        }
        if (packPrefab == null) { Debug.LogWarning("[Elderboom] Basic arms: no weapon model for " + weapon.id); return null; }

        int layer = operatorTemplate != null ? operatorTemplate.gameObject.layer : parent.gameObject.layer;
        var root = new GameObject(op.id + "_BasicArms_" + weapon.id).transform;
        root.SetParent(parent, false);
        var view = root.gameObject.AddComponent<ElderboomViewmodel>();
        var arms = root.gameObject.AddComponent<ElderboomBasicArms>();
        var holder = new GameObject("BasicView").transform;
        holder.SetParent(root, false);
        float k = Mathf.Tan(BasicHorizontalFov * .5f * Mathf.Deg2Rad) / Mathf.Tan(ElderboomPauseMenu.DefaultHorizontalFov * .5f * Mathf.Deg2Rad);
        holder.localScale = new Vector3(1f, 1f, k);
        var model = Instantiate(gripsPrefab, holder, false).transform;
        model.name = op.id + "_Arms";
        foreach (var a in model.GetComponentsInChildren<Animator>(true)) Kill(a);
        foreach (var a in model.GetComponentsInChildren<Animation>(true)) Kill(a);
        var rootBone = ElderboomAuthoredGrip.Find(model, "root");
        if (rootBone == null || rootBone.parent == null) { Kill(root.gameObject); return null; }
        FixBindPoses(model);
        var armature = rootBone.parent;
        var weaponNode = new GameObject("Weapon").transform; weaponNode.SetParent(armature, false);
        var magNode = new GameObject("Magazine").transform; magNode.SetParent(weaponNode, false);
        var spareNode = new GameObject("MagazineSpare").transform; spareNode.SetParent(weaponNode, false);
        var animator = armature.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        arms.Setup(op, weapon, family, meta, animator, armature, weaponNode, magNode, spareNode);
        arms.Pose("Idle");

        // ---- weapon geometry, built at the origin (no depth-scaled parent), then mounted under "Weapon"
        var pack = Instantiate(packPrefab, null, false);
        pack.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        pack.SetActive(true);
        var packView = pack.GetComponentInChildren<ElderboomViewmodel>(true);
        float packTime = source != null && source.id == ElderboomIds.Tactical ? 1f / 60f : 0f;
        if (packView != null && packView.model != null && packView.idle != null) ElderboomAnimationSampling.Sample(packView.idle, packView.model, packTime);
        var include = source != null ? ElderboomAuthoredGrip.WeaponNodes(pack.transform, source) : null;
        var packR = ElderboomAuthoredGrip.PackFrame(pack.transform, ElderboomAuthoredGrip.RightJoints);
        var packL = ElderboomAuthoredGrip.PackFrame(pack.transform, ElderboomAuthoredGrip.LeftJoints);
        var geo = new GameObject("OperatorWeapon_" + weapon.id).transform;
        var moved = new List<Transform>();
        foreach (var r in pack.GetComponentsInChildren<Renderer>(true))
        {
            if (include != null ? !ElderboomAuthoredGrip.Under(r.transform, include) : ElderboomAuthoredGrip.IsArm(r)) continue;
            if (r is ParticleSystemRenderer) continue;
            bool carried = false;
            foreach (var m in moved) if (r.transform.IsChildOf(m)) { carried = true; break; }
            if (carried) continue;
            var t = ElderboomAuthoredGrip.Transplant(r);
            if (t == null) continue;
            t.SetParent(geo, true);
            moved.Add(t);
        }
        foreach (var t in pack.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!(n.Contains("muzzle") || n.Contains("ads_") || n.StartsWith("socket_") || n == "righthand_grip" ||
                n == "lefthand_support" || n == "triggerpoint" || t.GetComponent<Light>() != null)) continue;
            if (include != null && !ElderboomAuthoredGrip.Under(t, include)) continue;
            bool under = false;
            foreach (var m in moved) if (t.IsChildOf(m)) { under = true; break; }
            if (under) continue;
            t.SetParent(geo, true);
        }
        Vector3? packWristR = packR.HasValue ? (Vector3?)packR.Value.GetColumn(3) : null;
        pack.SetActive(false);
        Kill(pack);
        if (weapon.category != ElderboomIds.Category.Melee) ElderboomAuthoredGrip.EnsureSights(geo, weapon);

        // ---- alignment on the reference weapon (Weapon-local, Unity space)
        var refPose = meta.hasAds && meta.ads != null && meta.ads.gripR != null && meta.ads.gripR.Length > 0 ? meta.ads : meta.idle;
        var refIdle = meta.idle ?? refPose;
        Vector3 refFwd = refPose.forward.normalized, refUp = Vector3.ProjectOnPlane(refPose.up, refFwd).normalized;
        Vector3 refWrist = arms.handR != null
            ? weaponNode.InverseTransformPoint(arms.handR.position)
            : refIdle.gripR[0];
        var rigPalm = ElderboomAuthoredGrip.PackFrame(armature, ElderboomAuthoredGrip.RightJoints);
        var rightSocket = ElderboomAuthoredGrip.Find(geo, "RightHand_Grip");
        var leftSocket = ElderboomAuthoredGrip.Find(geo, "LeftHand_Support");
        Vector3 gameFwd, gameUp, gameWrist;
        var rear = ElderboomAuthoredGrip.Find(geo, "MainWeapon_ADS_Rear");
        var front = ElderboomAuthoredGrip.Find(geo, "MainWeapon_ADS_Front");
        var upMarker = ElderboomAuthoredGrip.Find(geo, "MainWeapon_ADS_Up");
        var muzzle = ElderboomAuthoredGrip.Muzzle(geo);
        if (rear != null && front != null && (front.position - rear.position).sqrMagnitude > 1e-8f)
        {
            gameFwd = (front.position - rear.position).normalized;
            gameUp = upMarker != null ? upMarker.position - rear.position : muzzle != null ? muzzle.up : Vector3.up;
        }
        else if (muzzle != null) { gameFwd = muzzle.forward; gameUp = muzzle.up; }
        else if (packR.HasValue) { gameFwd = packR.Value.GetColumn(1); gameUp = -(Vector3)packR.Value.GetColumn(0); }
        else { gameFwd = Vector3.forward; gameUp = Vector3.up; }
        gameUp = Vector3.ProjectOnPlane(gameUp, gameFwd);
        if (gameUp.sqrMagnitude < 1e-8f) gameUp = Vector3.up;
        if (packWristR.HasValue) gameWrist = packWristR.Value;
        else if (rightSocket != null)
        {
            gameWrist = rightSocket.position;
            // The marker lies inside the grip. Match it to the palm, not the wrist joint.
            var middle = ElderboomAuthoredGrip.Find(armature, "DEF-f_middle.01.R");
            refWrist = middle != null
                ? Vector3.Lerp(refWrist, weaponNode.InverseTransformPoint(middle.position), .55f)
                : PalmContact(refIdle.gripR, refWrist);
            if (rigPalm.HasValue)
            {
                Vector3 towardGrip = weaponNode.InverseTransformDirection(rigPalm.Value.GetColumn(2));
                Vector3 inboard = Vector3.Cross(refFwd, refUp);
                if (Vector3.Dot(towardGrip, inboard) < 0f) towardGrip = -towardGrip;
                // The geometric marker is inside the grip; leave space for the palm instead of
                // laying its surface through the middle of the receiver.
                refWrist += towardGrip * .014f;
            }
        }
        else
        {
            // No pack hand: a point a third of the way back from the muzzle-side bounds, just under the bore.
            var b = new Bounds(geo.position, Vector3.zero); bool any = false;
            foreach (var r in geo.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            gameWrist = b.center - gameFwd * (Vector3.Dot(b.extents, new Vector3(Mathf.Abs(gameFwd.x), Mathf.Abs(gameFwd.y), Mathf.Abs(gameFwd.z))) * .35f) - gameUp.normalized * .06f;
        }
        Quaternion align = Quaternion.LookRotation(refFwd, refUp) * Quaternion.Inverse(Quaternion.LookRotation(gameFwd, gameUp));
        if (packR.HasValue && rigPalm.HasValue)
        {
            var palmAlign = Quaternion.Inverse(weaponNode.rotation) * rigPalm.Value.rotation *
                Quaternion.Inverse(packR.Value.rotation);
            // Use the authored palm when it agrees with the gun's forward/up markers.
            // Keeping some influence from the bore prevents inconsistent donor poses from rolling the gun.
            if (Quaternion.Angle(align, palmAlign) < 35f)
                align = Quaternion.RotateTowards(align, palmAlign, 16f);
        }
        geo.SetParent(weaponNode, false);
        geo.localRotation = align;
        geo.localPosition = refWrist - align * gameWrist;
        geo.localScale = Vector3.one;

        // ---- magazine / shell follows the reference magazine track
        string partRole = family == "Winchester" ? "shell" : "magazine";
        Transform mag = null;
        foreach (var t in geo.GetComponentsInChildren<Transform>(true))
            if (ElderboomDonorMotion.PartRole(t.name) == partRole && t.GetComponentInChildren<Renderer>(true) != null) { mag = t; break; }
        if (mag != null && !string.IsNullOrEmpty(meta.magazine))
        {
            mag.SetParent(magNode, true);
            if (!string.IsNullOrEmpty(meta.spare))
            {
                var spare = Instantiate(mag.gameObject, spareNode, false).transform;
                spare.name = mag.name + "_Spare";
                spare.localPosition = mag.localPosition; spare.localRotation = mag.localRotation; spare.localScale = mag.localScale;
                foreach (var tr in spare.GetComponentsInChildren<Transform>(true)) { string n = tr.name.ToLowerInvariant(); if (n.Contains("muzzle") || n.Contains("ads_") || n.StartsWith("socket_")) tr.name = "SpareMarker"; }
            }
            arms.magazine = mag;
            var magBounds = mag.GetComponentInChildren<Renderer>();
            if (magBounds != null)
                arms.magazineContact = magNode.InverseTransformPoint(magBounds.bounds.center);
        }
        // The animation already carries a coherent two-handed hold.  Do not run a second
        // procedural IK/finger pass here: it was authored for the donor meshes and, with
        // the Basic rig, could stretch the support arm far outside its skinned bind pose.
        ElderboomAuthoredGrip.SetLayer(root, layer);

        // ---- framing: the clips frame hip and aim themselves; aiming only corrects for this weapon's sight line
        view.operatorId = op.id;
        view.model = model.gameObject;
        view.basic = arms;
        view.idle = null; view.reload = null; view.ability = null;
        view.hipPosition = Vector3.zero; view.hipRotation = Quaternion.identity;
        view.adsPosition = Vector3.zero; view.adsRotation = Quaternion.identity;
        view.hasAds = false;
        if (weapon.category != ElderboomIds.Category.Melee)
        {
            arms.Pose(meta.hasAds ? "ADSIdle" : "Idle");
            rear = ElderboomAuthoredGrip.Find(geo, "MainWeapon_ADS_Rear");
            front = ElderboomAuthoredGrip.Find(geo, "MainWeapon_ADS_Front");
            if (rear != null && front != null)
            {
                Vector3 r0 = parent.InverseTransformPoint(rear.position);
                float relief = meta.hasAds ? Mathf.Clamp(r0.z, .05f, .45f) : .16f;
                view.hasAds = ElderboomWeaponFraming.SolveSightPose(root, rear, front, new Vector3(0f, 0f, relief), out view.adsPosition, out view.adsRotation);
                if (!view.hasAds) { view.adsPosition = Vector3.zero; view.adsRotation = Quaternion.identity; }
            }
        }
        // ---- hip framing: the pack's own camera holds every weapon low and dead centre, mostly below the screen. The
        // rig is turned a few degrees towards the centre and moved so the right hand (first idle frame) sits at a set
        // point of the screen, at a set distance from the eye. Aiming is unaffected (its pose was solved above).
        if (weapon.category != ElderboomIds.Category.Melee)
        {
            arms.Pose("Idle");
            var handR = ElderboomAuthoredGrip.Find(armature, "DEF-hand.R");
            if (handR != null)
            {
                bool pistol = family == "Deagle";
                // viewport x, viewport y, distance (m), yaw, pitch, roll
                // Keep the entire weapon comfortably inside the viewmodel frustum.  The
                // old close-up distance made a world-size AR fill most of the screen.
                Vector3 vp = pistol ? new Vector3(.60f, .20f, .55f) : new Vector3(.62f, .22f, .60f);
                Vector3 turn = pistol ? new Vector3(0f, -4f, 0f) : new Vector3(0f, -4f, -2f);
                var rot = Quaternion.Euler(turn);
                Vector3 h = rot * parent.InverseTransformPoint(handR.position);
                float tanH = Mathf.Tan(ElderboomPauseMenu.DefaultHorizontalFov * .5f * Mathf.Deg2Rad), aspect = 16f / 9f;
                var want = new Vector3((vp.x - .5f) * 2f * tanH * vp.z, (vp.y - .5f) * 2f * tanH / aspect * vp.z, vp.z);
                view.hipRotation = rot;
                view.hipPosition = want - h;
            }
        }
        view.adsRelief = .2f;
        view.keepAdsRelief = true;
        view.framingStored = true;
        arms.Pose("Idle");
        root.gameObject.SetActive(false);
        return root;
    }

    // The arms come from glTF, where every bone points along its local +Y; the clips come from FBX, where every bone
    // points along its local -Z. Same joints, but each bone frame differs by the same 90 degrees about X (measured on all
    // three operators: every child offset matches exactly). The clips set the bones in the FBX frames, so the skin's bind
    // poses are moved into those frames too: bindpose' = D^-1 * bindpose. Meshes are fixed once and shared.
    static readonly Quaternion ClipBoneFrame = Quaternion.Euler(90f, 0f, 0f);
    static readonly Dictionary<Mesh, Mesh> fixedMeshes = new Dictionary<Mesh, Mesh>();
    static void FixBindPoses(Transform model)
    {
        var inv = Matrix4x4.Rotate(Quaternion.Inverse(ClipBoneFrame));
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var src = smr.sharedMesh;
            if (src == null) continue;
            if (!fixedMeshes.TryGetValue(src, out var fixedMesh) || fixedMesh == null)
            {
                fixedMesh = Instantiate(src);
                fixedMesh.name = src.name + "_ClipFrames";
                var bp = fixedMesh.bindposes;
                var bones = smr.bones;
                for (int i = 0; i < bp.Length && i < bones.Length; i++)
                    if (bones[i] != null && (bones[i].name == "root" || bones[i].name.StartsWith("DEF-"))) bp[i] = inv * bp[i];
                fixedMesh.bindposes = bp;
                fixedMeshes[src] = fixedMesh;
            }
            smr.sharedMesh = fixedMesh;
        }
    }

    // ------------------------------------------------------------------ runtime
    ElderboomOperatorDefinition op;
    ElderboomWeaponDefinition weapon;
    Meta meta;
    string family;
    Animator animator;
    Transform armature, weaponNode, magNode, spareNode;
    [NonSerialized] public Transform magazine;
    Transform upperL, foreL, handL, handR, support, pump;
    Vector3 referenceRight, referenceLeft, pumpRest, pumpTravel;
    Quaternion referenceRightRotation, referenceLeftRotation;
    bool hasRightContact;
    ElderboomFingerContact fingers;
    ElderboomLeftArmAbility armAbility;
    Vector3 magazineContact;
    readonly Dictionary<string, Clip> byRole = new Dictionary<string, Clip>();
    bool aiming, sprinting; float locomotion;
    string action; float actionEnd; string loco;
    readonly Queue<(string role, float speed)> queue = new Queue<(string, float)>();
    float reloadDuration;

    void Setup(ElderboomOperatorDefinition op, ElderboomWeaponDefinition weapon, string family, Meta meta, Animator animator, Transform armature, Transform weaponNode, Transform magNode, Transform spareNode)
    {
        this.op = op; this.weapon = weapon; this.family = family; this.meta = meta; this.animator = animator; this.armature = armature;
        this.weaponNode = weaponNode; this.magNode = magNode; this.spareNode = spareNode;
        byRole.Clear();
        foreach (var c in meta.clips ?? new Clip[0]) byRole[c.role] = c;
        upperL = ElderboomAuthoredGrip.Find(armature, "DEF-upper_arm.L");
        foreL = ElderboomAuthoredGrip.Find(armature, "DEF-forearm.L");
        handL = ElderboomAuthoredGrip.Find(armature, "DEF-hand.L");
        handR = ElderboomAuthoredGrip.Find(armature, "DEF-hand.R");
        armAbility = GetComponentInParent<ElderboomLeftArmAbility>();
    }

    public string Family => family;
    public bool Has(string role) => byRole.ContainsKey(role);
    string State(string role) => family + "_" + role;
    float Length(string role) => byRole.TryGetValue(role, out var c) ? Mathf.Max(.05f, c.length) : 0f;

    // Immediate pose (build time, reset): the state's first frame.
    public void Pose(string role)
    {
        if (animator == null || !Has(role)) return;
        bool was = animator.gameObject.activeInHierarchy;
        animator.Play(State(role), 0, 0f);
        animator.speed = 1f;
        if (was)
        {
            animator.Update(0f);
        }
        loco = role; action = null; queue.Clear();
    }

    public bool SprintActive => sprinting && locomotion > .05f && !aiming && Has("Sprint") && action == null;
    public bool WalkActive => locomotion > .05f && !aiming && Has("Walk") && action == null;
    public bool Busy => action != null;
    public float ReloadDuration => reloadDuration > 0f ? reloadDuration : Length("Reload");

    public void SetAiming(bool value) { aiming = value; }
    public void SetLocomotion(float amount, bool sprint) { locomotion = amount; sprinting = sprint; }

    public void ResetToIdle()
    {
        action = null; queue.Clear(); aiming = false;
        if (animator != null && animator.isActiveAndEnabled) { animator.Play(State("Idle"), 0, 0f); animator.speed = 1f; }
        loco = "Idle";
    }

    void Play(string role, float fade, float speed = 1f)
    {
        if (!Has(role)) return;
        animator.speed = speed;
        if (fade <= 0f) animator.Play(State(role), 0, 0f);
        else animator.CrossFadeInFixedTime(State(role), fade, 0, 0f);
        action = role; actionEnd = Time.time + Length(role) / Mathf.Max(.01f, speed);
    }

    public void PlayDraw()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        queue.Clear();
        Play("Draw", 0f);
    }

    public bool PlayFire(float interval)
    {
        if (weapon != null && weapon.category == ElderboomIds.Category.Melee) return false;
        string role = aiming && Has("ADSFire") ? "ADSFire" : "Fire";
        if (!Has(role) || animator == null || !animator.isActiveAndEnabled) return false;
        queue.Clear();
        Play(role, .02f);
        return true;
    }

    // Reload: the family's own reload (empty or not). Tube-fed weapons (Winchester clips): start, one loop per shell, end.
    public float PlayReload(bool empty, int shells)
    {
        if (animator == null || !animator.isActiveAndEnabled) return 2f;
        queue.Clear();
        if (Has("ReloadLoop"))
        {
            int n = Mathf.Max(1, shells);
            string start = empty && Has("ReloadStartEmpty") ? "ReloadStartEmpty" : "ReloadStart";
            float total = Length(start) + Length("ReloadEnd");
            int loops = empty && start == "ReloadStartEmpty" ? n - 1 : n;
            for (int i = 0; i < loops; i++) { queue.Enqueue(("ReloadLoop", 1f)); total += Length("ReloadLoop"); }
            queue.Enqueue(("ReloadEnd", 1f));
            Play(start, .1f);
            reloadDuration = total;
            return total;
        }
        string role = empty && Has("ReloadEmpty") ? "ReloadEmpty" : "Reload";
        string ads = empty ? "ADSReloadEmpty" : "ADSReload";
        if (aiming && Has(ads)) role = ads;
        float length = Length(role);
        float cap = empty ? 3.8f : 3.2f;
        float speed = Mathf.Max(1f, length / cap);
        Play(role, .1f, speed);
        reloadDuration = length / speed;
        return reloadDuration;
    }

    void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        if (action != null && Time.time >= actionEnd)
        {
            if (queue.Count > 0) { var next = queue.Dequeue(); Play(next.role, .05f, next.speed); return; }
            action = null; loco = null; animator.speed = 1f;
        }
        if (action != null) return;
        bool moving = locomotion > .05f;
        string want = aiming && Has("ADSIdle") ? "ADSIdle" : moving && sprinting && Has("Sprint") ? "Sprint" : moving && Has("Walk") ? "Walk" : "Idle";
        if (want != loco)
        {
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(State(want), want == "ADSIdle" || loco == "ADSIdle" ? .18f : .25f, 0);
            loco = want;
        }
    }

    static Vector3 PalmContact(Vector3[] points, Vector3 fallback)
    {
        return points != null && points.Length >= 3
            ? Vector3.Lerp(points[0], points[2], .55f)
            : fallback;
    }

    void CalibrateContacts(Transform geo, Transform leftSocket, Matrix4x4? packLeft, Transform muzzle)
    {
        if (handR != null)
        {
            referenceRight = weaponNode.InverseTransformPoint(handR.position);
            referenceRightRotation = Quaternion.Inverse(weaponNode.rotation) * handR.rotation;
            hasRightContact = true;
        }
        if (upperL == null || foreL == null || handL == null) return;
        referenceLeft = weaponNode.InverseTransformPoint(handL.position);
        referenceLeftRotation = Quaternion.Inverse(weaponNode.rotation) * handL.rotation;
        Vector3 target = handL.position;
        Quaternion turn = handL.rotation;
        bool haveTarget = false;
        bool pistol = weapon.category == ElderboomIds.Category.Handgun;
        var leftPalm = ElderboomAuthoredGrip.PackFrame(armature, ElderboomAuthoredGrip.LeftJoints);
        if (packLeft.HasValue && leftPalm.HasValue)
        {
            target = geo.TransformPoint(packLeft.Value.GetColumn(3));
            Quaternion referencePalm = leftPalm.Value.rotation;
            Quaternion gamePalm = geo.rotation * packLeft.Value.rotation;
            turn = gamePalm * Quaternion.Inverse(referencePalm) * handL.rotation;
            haveTarget = true;
        }
        else if (leftSocket != null && !pistol)
        {
            var knuckle = ElderboomAuthoredGrip.Find(armature, "DEF-f_middle.01.L");
            var palmOffset = knuckle != null ? (knuckle.position - handL.position) * .55f : Vector3.zero;
            target = leftSocket.position - palmOffset;
            haveTarget = true;
        }

        if (weapon.id == "mp133")
        {
            pump = ElderboomAuthoredGrip.Find(geo, "G_Pump");
            if (pump != null && muzzle != null)
            {
                pumpRest = pump.localPosition;
                pumpTravel = pump.parent.InverseTransformVector(-muzzle.forward * .075f);
            }
        }

        if (!haveTarget && pistol)
        {
            // Both hands keep their relative grip on a pistol when the right-hand anchor moves.
            haveTarget = true;
        }
        if (!haveTarget)
        {
            fingers = ElderboomFingerContact.FitContact(transform, armature, weaponNode, handR, handL);
            return;
        }
        support = new GameObject("BasicSupportContact").transform;
        support.SetParent(pump != null ? pump : geo, false);
        support.SetPositionAndRotation(target, turn);
        var middle = ElderboomAuthoredGrip.Find(armature, "DEF-f_middle.01.L");
        bool measured = weapon.id == "m4" && leftPalm.HasValue &&
            ElderboomSupportGrip.PlaceMarker(support, geo, weapon.id, handL, middle);
        if (measured)
            support.rotation *= Quaternion.Inverse(leftPalm.Value.rotation) * handL.rotation;
        else
        {
            Quaternion idleRotation = weaponNode.rotation * referenceLeftRotation;
            support.rotation = Quaternion.RotateTowards(idleRotation, support.rotation, 38f);
        }
        ApplyContacts("Idle", 0f);
        fingers = ElderboomFingerContact.FitContact(transform, armature, weaponNode, handR, handL);
    }

    void LateUpdate()
    {
        // Intentionally empty.  The Animator is the sole owner of the Basic FPS bones.
        // Keeping the weapon in the reference hold avoids a feedback loop where a hand
        // solver moves the arm, then moves the weapon, then solves the arm again.
    }

    // Runs after Animator in play and after SampleAnimation in the preview.
    public void ApplyContacts(string role, float normalTime)
    {
        // Retained as a harmless compatibility hook for the editor preview tool.
        // Runtime contact solving is deliberately disabled; see LateUpdate above.
        if (!RuntimeContactSolving) return;
        if (weaponNode == null) return;
        Vector3 leftInWeapon = handL != null ? weaponNode.InverseTransformPoint(handL.position) : Vector3.zero;
        Vector3 leftPosition = handL != null ? handL.position : Vector3.zero;
        Quaternion originalWeaponRotation = weaponNode.rotation;
        // Determine magazine contact before the weapon is pulled back onto the right grip.
        Transform heldMagazine = null;
        float magazineWeight = 0f;
        if (magazine != null && handL != null && role != null &&
            (role.Contains("Reload") || role == "Load"))
        {
            for (int i = 0; i < 2; i++)
            {
                var candidate = i == 0 ? magNode : spareNode;
                if (candidate == null || (i == 1 && string.IsNullOrEmpty(meta.spare)) || candidate.localScale.sqrMagnitude < .001f) continue;
                float d = Vector3.Distance(leftPosition, candidate.position);
                float proximity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, .125f, d));
                if (proximity <= magazineWeight) continue;
                magazineWeight = proximity;
                heldMagazine = candidate;
            }
        }
        if (hasRightContact)
        {
            Vector3 authoredPoint = weaponNode.TransformPoint(referenceRight);
            Quaternion authoredRotation = weaponNode.rotation * referenceRightRotation;
            Quaternion delta = Quaternion.RotateTowards(Quaternion.identity, handR.rotation * Quaternion.Inverse(authoredRotation), 24f);
            Vector3 correction = Vector3.ClampMagnitude(handR.position - authoredPoint, .15f);
            weaponNode.rotation = delta * weaponNode.rotation;
            weaponNode.position += authoredPoint + correction - weaponNode.TransformPoint(referenceRight);
        }

        float pumpFactor = 0f;
        if (pump != null)
        {
            if (role == "Fire" || role == "ADSFire")
            {
                float t = Mathf.Clamp01(normalTime);
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.10f, .30f, t));
                float forward = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.38f, .65f, t));
                pumpFactor = back * (1f - forward);
            }
            pump.localPosition = pumpRest + pumpTravel * pumpFactor;
        }

        if (handL == null || (armAbility != null && armAbility.Active))
        {
            if (fingers != null) fingers.ApplyPose(1f, 0f);
            return;
        }
        float distance = (leftInWeapon - referenceLeft).magnitude;
        float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .135f, distance));
        if (pumpFactor > 0f) weight = Mathf.Max(weight, pumpFactor);
        Quaternion sourceRotation = handL.rotation;
        if (heldMagazine != null && magazineWeight > weight)
        {
            // Keep the authored wrist offset while accounting for the real magazine's grip point.
            var correction = Vector3.ClampMagnitude(heldMagazine.TransformVector(magazineContact), .06f);
            Vector3 target = Vector3.Lerp(leftPosition, weaponNode.TransformPoint(leftInWeapon) + correction, magazineWeight);
            TwoBone(upperL, foreL, handL, target);
            handL.rotation = Quaternion.Slerp(sourceRotation,
                weaponNode.rotation * Quaternion.Inverse(originalWeaponRotation) * sourceRotation, magazineWeight);
            weight = 0f;
        }
        else if (support != null && weight > 0f)
        {
            Vector3 target = handL.position + Vector3.ClampMagnitude(support.position - handL.position, .30f) * weight;
            float elbowBias = weapon.id == "m4" ? .48f * weight : 0f;
            TwoBone(upperL, foreL, handL, target, -transform.up, elbowBias);
            handL.rotation = Quaternion.Slerp(sourceRotation, support.rotation, weight);
        }
        if (fingers != null) fingers.ApplyPose(1f, support != null ? weight : 0f);
    }

    static void TwoBone(Transform upper, Transform forearm, Transform hand, Vector3 target, Vector3 preferredPole = default, float poleWeight = 0f)
    {
        Vector3 a = upper.position, b = forearm.position, c = hand.position;
        float upperLength = (b - a).magnitude, lowerLength = (c - b).magnitude;
        Vector3 aim = target - a;
        if (upperLength < .001f || lowerLength < .001f || aim.sqrMagnitude < 1e-8f) return;
        Vector3 axis = aim.normalized;
        float reach = Mathf.Clamp(aim.magnitude, Mathf.Abs(upperLength - lowerLength) + .001f, upperLength + lowerLength - .001f);
        Vector3 pole = Vector3.ProjectOnPlane(b - a, axis);
        if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(upper.right, axis);
        if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(upper.up, axis);
        var bias = Vector3.ProjectOnPlane(preferredPole, axis);
        if (poleWeight > 0f && bias.sqrMagnitude > 1e-8f)
            pole = Vector3.Slerp(pole.normalized, bias.normalized, poleWeight);
        float along = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) / (2f * reach);
        float bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 elbow = a + axis * along + pole.normalized * bend;
        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, a + axis * reach - forearm.position) * forearm.rotation;
    }
}
