using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Third-person melee operator (Steel Sentinel). Added to the player by ElderboomGame.ConfigureLoadout when the
// operator definition has thirdPersonMelee set.
//   - shows the full-body model (Resources/Elderboom/Sentinel/SteelSentinel) and hides the first-person viewmodel
//   - puts the player camera behind the right shoulder (the player script keeps driving look and movement)
//   - fire: light combo   ·   aim: heavy attack   ·   reload: split the staff / join the batons   ·   crouch: dodge
// The clips come from the FBX and are played with a small Playables cross-fader (no AnimatorController needed).
public sealed class ElderboomSentinel : MonoBehaviour
{
    const string Folder = "Elderboom/Sentinel/";
    const float Scale = 1.15f; // model size against the rig's 1.8 m

    sealed class Attack
    {
        public string clip; public float length, hitAt, damage, range, arcDot, push, stun, trauma, lunge, hitAt2 = -1f;
        public bool around;
    }
    static Attack A(string clip, int frames, int hitFrame, float damage, float range, float arcDot, float push = 0f, float stun = 0f, float trauma = .16f, bool around = false, int hit2 = -1, float lunge = .9f)
        => new Attack { clip = clip, length = frames / 30f, hitAt = hitFrame / 30f, damage = damage, range = range * Scale, arcDot = arcDot, lunge = lunge, push = push, stun = stun, trauma = trauma, around = around, hitAt2 = hit2 < 0 ? -1f : hit2 / 30f };

    // Damage is on the game's scale: a wave-1 zombie has about 6 health (normal difficulty), a rifle bullet does 6.
    static readonly Attack[] StaffLight =
    {
        A("staff_light1", 24, 9, 3f, 2.7f, .1f),
        A("staff_light2", 24, 9, 3f, 2.7f, .1f),
        A("staff_light3", 34, 21, 5f, 2.8f, .45f, 9f, .5f, .3f, lunge: 1.3f)
    };
    static readonly Attack[] DualLight =
    {
        A("dual_light1", 20, 8, 2f, 2.2f, .2f),
        A("dual_light2", 20, 8, 2f, 2.2f, .2f),
        A("dual_light3", 24, 9, 3f, 2.3f, .0f),
        A("dual_light4", 32, 15, 4.5f, 2.4f, .4f, 8f, .45f, .28f, lunge: 1.4f)
    };
    static readonly Attack StaffHeavy = A("staff_heavy", 48, 19, 6f, 3.1f, -1f, 14f, .8f, .4f, true, 26, .7f);
    static readonly Attack DualHeavy = A("dual_heavy", 46, 27, 8f, 3.0f, -.2f, 16f, .9f, .5f, lunge: 2.2f);

    enum Act { None, Attack, Dodge, Switch, Hit, Dead }

    HDRPSlicePlayer player;
    CharacterController body;
    Transform model, cam;
    Animator animator;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable current, previous;
    bool hasCurrent, hasPrevious, currentLoops;
    float fade = 1f, fadeTime = .1f, playSpeed = 1f, hitStopUntil;
    string currentName = "";
    readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
    readonly List<Renderer> hidden = new List<Renderer>();
    float hideScanAt, hurtSoundAt, aimScanAt;
    ElderboomZombie aimTarget; // the enemy the next attack would go to

    bool dual;
    Act act;
    float actStart, actEnd;
    Attack attack;
    bool hit1Done, hit2Done, queuedLight, queuedHeavy;
    int combo;
    float comboResetAt, lastHealth, camDistance = 2.7f;
    Vector3 dodgeDir;
    float modelYaw;
    readonly Collider[] overlaps = new Collider[48];
    readonly RaycastHit[] camHits = new RaycastHit[12];
    readonly HashSet<ElderboomZombie> struck = new HashSet<ElderboomZombie>();

    public bool Active => model != null;
    public bool Dual => dual;

    public void Begin(HDRPSlicePlayer owner)
    {
        End();
        player = owner; body = owner.GetComponent<CharacterController>();
        cam = owner.ViewCamera != null ? owner.ViewCamera.transform : null;
        var prefab = Resources.Load<GameObject>(Folder + "SteelSentinel");
        if (prefab == null || cam == null) { Debug.LogWarning("[ElderboomSentinel] model or camera missing; staying in first person."); return; }
        model = Instantiate(prefab, transform).transform;
        model.name = "SteelSentinel_Body";
        model.localPosition = Vector3.zero;
        model.localScale = prefab.transform.localScale * Scale;
        modelYaw = transform.eulerAngles.y;
        ApplyMaterial();
        clips.Clear();
        foreach (var clip in Resources.LoadAll<AnimationClip>(Folder + "SteelSentinel"))
        {
            string key = clip.name; int bar = key.LastIndexOf('|'); if (bar >= 0) key = key.Substring(bar + 1);
            if (key.StartsWith("__preview")) continue;
            if (!clips.ContainsKey(key) || clip.name.Contains("Sentinel_Rig")) clips[key] = clip;
        }
        animator = model.GetComponentInChildren<Animator>() ?? model.gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        graph = PlayableGraph.Create("Sentinel");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        var output = AnimationPlayableOutput.Create(graph, "Sentinel", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        dual = false; act = Act.None; combo = 0; lastHealth = owner.Health;
        Upgraded = Deposited = false; ResetChain();
        Play("staff_idle", 0f, true);
        player.MeleeOperator = true; player.MeleeMoveScale = 1f;
        ElderboomGamepad.Active = ElderboomGamepad.Layout.Melee;
        Debug.Log("[ElderboomSentinel] ready, clips: " + clips.Count);
    }

    public void End()
    {
        if (frozen) { frozen = false; if (Time.timeScale > 0f && Time.timeScale < .5f) Time.timeScale = 1f; }
        if (graph.IsValid()) graph.Destroy();
        hasCurrent = hasPrevious = false;
        if (model != null) Destroy(model.gameObject);
        model = null;
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
        if (player != null) { player.MeleeOperator = false; player.MeleeMoveScale = 1f; }
        ElderboomGamepad.Active = ElderboomGamepad.Layout.Shooter;
    }

    void OnDestroy() => End();

    // ---- altar upgrade (ElderboomAltarUpgrader): the staff is laid on the altar and comes back crimson, damage x2 ----
    public bool Upgraded { get; private set; }
    public bool Deposited { get; private set; }
    public bool CanUpgrade => Active && !Upgraded && !Deposited && act == Act.None && player != null && !player.IsDead;

    IEnumerable<SkinnedMeshRenderer> StaffRenderers()
    {
        if (model == null) yield break;
        foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.StartsWith("Staff") && !r.name.StartsWith("CrimsonSurface_")) yield return r;
    }

    public GameObject BeginAltarDeposit(Transform socket)
    {
        if (!CanUpgrade || socket == null) return null;
        var display = new GameObject("Deposited_staff"); display.transform.SetParent(socket, false);
        var lifetime = display.AddComponent<ElderboomWeaponDisplay>();
        int index = 0;
        foreach (var skin in StaffRenderers())
        {
            // each half frozen in its own bone's space, then laid flat with its long axis across the tray
            var bone = skin.bones != null && skin.bones.Length > 0 && skin.bones[0] != null ? skin.bones[0] : skin.transform;
            var pose = new Mesh(); skin.BakeMesh(pose, true); lifetime.meshes.Add(pose);
            var baked = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var matrix = bone.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var parts = new CombineInstance[pose.subMeshCount];
            for (int i = 0; i < parts.Length; i++) parts[i] = new CombineInstance { mesh = pose, subMeshIndex = i, transform = matrix };
            baked.CombineMeshes(parts, false, true); lifetime.meshes.Add(baked);
            var size = baked.bounds.size;
            var turn = size.x >= size.y && size.x >= size.z ? Quaternion.identity : size.y >= size.z ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(0f, 90f, 0f);
            var go = new GameObject(skin.name); go.transform.SetParent(display.transform, false);
            go.transform.localRotation = turn;
            go.transform.localPosition = -(turn * baked.bounds.center) + new Vector3(0f, 0f, index == 0 ? -.05f : .05f);
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            go.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            index++;
        }
        if (index == 0) { Destroy(display); return null; }
        display.transform.localPosition = new Vector3(0f, .12f, 0f);
        foreach (var skin in StaffRenderers()) skin.enabled = false;
        Deposited = true; queuedLight = queuedHeavy = false;
        return display;
    }

    public bool CompleteAltarUpgrade()
    {
        if (!Deposited || player == null || player.IsDead) return false;
        Deposited = false; Upgraded = true;
        foreach (var skin in StaffRenderers()) { skin.enabled = true; ElderboomUpgradedWeapon.Apply(skin.transform, null, "", true); }
        return true;
    }

    public void CancelAltarDeposit()
    {
        if (!Deposited) return;
        Deposited = false;
        foreach (var skin in StaffRenderers()) skin.enabled = true;
    }

    void ApplyMaterial() => ApplyBodyMaterial(model);

    static Material bodyMaterial;
    // HDRP Lit from the Meshy textures (albedo, normal, packed metallic/smoothness mask). The staff keeps its own materials.
    public static void ApplyBodyMaterial(Transform root)
    {
        if (bodyMaterial == null)
        {
            var shader = Shader.Find("HDRP/Lit");
            var albedo = Resources.Load<Texture2D>(Folder + "Sentinel_texture_0");
            if (shader == null || albedo == null) return;
            var m = new Material(shader) { name = "Sentinel_Body" };
            m.SetTexture("_BaseColorMap", albedo); m.SetColor("_BaseColor", Color.white);
            var normal = Resources.Load<Texture2D>(Folder + "Sentinel_texture_0_normal");
            if (normal != null) { m.SetTexture("_NormalMap", normal); m.SetFloat("_NormalScale", 1f); }
            var mask = Resources.Load<Texture2D>(Folder + "Sentinel_mask");
            if (mask != null) { m.SetTexture("_MaskMap", mask); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_MetallicRemapMax", 1f); m.SetFloat("_SmoothnessRemapMax", .8f); }
            else { m.SetFloat("_Metallic", .7f); m.SetFloat("_Smoothness", .5f); }
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
            bodyMaterial = m;
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (!r.name.StartsWith("Staff")) r.sharedMaterial = bodyMaterial;
    }

    // ---- animation ------------------------------------------------------------------------
    void Play(string name, float crossFade, bool loop, float speed = 1f)
    {
        if (!graph.IsValid() || !clips.TryGetValue(name, out var clip)) return;
        if (hasPrevious) { mixer.DisconnectInput(1); previous.Destroy(); hasPrevious = false; }
        if (hasCurrent) { mixer.DisconnectInput(0); previous = current; hasPrevious = true; mixer.ConnectInput(1, previous, 0); }
        current = AnimationClipPlayable.Create(graph, clip);
        current.SetApplyFootIK(false);
        mixer.ConnectInput(0, current, 0);
        hasCurrent = true; currentLoops = loop; currentName = name; playSpeed = speed;
        fadeTime = Mathf.Max(.0001f, crossFade); fade = hasPrevious && crossFade > 0f ? 0f : 1f;
        mixer.SetInputWeight(0, fade); if (hasPrevious) mixer.SetInputWeight(1, 1f - fade);
    }

    void Loop(string name, float speed, float crossFade = .16f)
    {
        if (currentName != name) Play(name, crossFade, true, speed); else playSpeed = speed;
    }

    void TickAnimation()
    {
        if (!graph.IsValid() || !hasCurrent) return;
        float dt = Time.deltaTime;
        float speed = Time.time < hitStopUntil ? .04f : playSpeed;
        current.SetSpeed(speed);
        if (hasPrevious) previous.SetSpeed(Time.time < hitStopUntil ? .04f : 1f);
        if (fade < 1f)
        {
            fade = Mathf.Min(1f, fade + dt / fadeTime);
            mixer.SetInputWeight(0, fade);
            if (hasPrevious)
            {
                mixer.SetInputWeight(1, 1f - fade);
                if (fade >= 1f) { mixer.DisconnectInput(1); previous.Destroy(); hasPrevious = false; }
            }
        }
        graph.Evaluate(dt);
        if (currentLoops)
        {
            double length = current.GetAnimationClip().length;
            if (length > 0.01 && current.GetTime() >= length) current.SetTime(current.GetTime() % length);
        }
    }

    // ---- gameplay -------------------------------------------------------------------------
    void Update()
    {
        TickFreeze();
        if (!Active || player == null) return;
        if (Time.time >= hideScanAt) { hideScanAt = Time.time + .3f; HideViewmodel(); }
        bool input = !player.SettingsOpen && ElderboomInput.AcceptsInput && Time.timeScale > 0f;
        if (Deposited) { input = false; queuedLight = queuedHeavy = false; } // the staff is on the altar

        if (player.IsDead)
        {
            if (act != Act.Dead) { act = Act.Dead; player.MeleeMoveScale = 0f; Play(dual ? "dual_death" : "staff_death", .1f, false); ElderboomAudio.Play("sentinel/death", transform.position + Vector3.up, .8f, true); }
            TickAnimation(); return;
        }
        if (act == Act.Dead) { act = Act.None; Play(Idle, .2f, true); } // revived or new session

        // took damage: flinch (never out of a heavy swing or a dodge)
        if (player.Health < lastHealth - .5f && (act == Act.None || (act == Act.Attack && attack != StaffHeavy && attack != DualHeavy && Time.time < actStart + attack.hitAt - .08f)))
        { act = Act.Hit; actStart = Time.time; actEnd = Time.time + .42f; Play(dual ? "dual_hit" : "staff_hit", .05f, false); queuedLight = queuedHeavy = false; }
        if (player.Health < lastHealth - .5f && Time.time >= hurtSoundAt) { hurtSoundAt = Time.time + .4f; ElderboomAudio.PlayAny("sentinel/hurt", 2, transform.position + Vector3.up, .55f, true, .08f); }
        if (player.Health < lastHealth - .5f) ResetChain(); // a hit taken breaks the chain
        if (chain > 0 && act != Act.Attack && Time.time > chainUntil) ResetChain();
        if (act == Act.Attack) aimTarget = target;
        else if (Time.time >= aimScanAt) { aimScanAt = Time.time + .1f; aimTarget = PickTarget(out _); }
        lastHealth = player.Health;

        if (input)
        {
            if (ElderboomInput.Down("fire")) queuedLight = true;
            if (ElderboomInput.Down("aim")) queuedHeavy = true;
        }
        if (Time.time > comboResetAt && act == Act.None) combo = 0;

        switch (act)
        {
            case Act.Attack: TickAttack(); break;
            case Act.Dodge:
                float t = (Time.time - actStart) / (actEnd - actStart);
                if (t < .6f && body != null && body.enabled) body.Move(dodgeDir * (11f * (1f - t)) * Time.deltaTime);
                if (Time.time >= actEnd) Finish();
                break;
            case Act.Switch:
            case Act.Hit:
                if (Time.time >= actEnd) Finish();
                break;
        }

        if (act == Act.None && input)
        {
            if (ElderboomInput.Down("crouch")) StartDodge();
            else if (ElderboomInput.Down("reload")) StartSwitch();
            else if (queuedHeavy) StartAttack(dual ? DualHeavy : StaffHeavy);
            else if (queuedLight) StartAttack(NextLight());
        }
        if (act == Act.None) Locomotion();

        // facing: the swing follows the camera; free running turns the body towards the movement
        float wantYaw = transform.eulerAngles.y;
        if (act == Act.None || act == Act.Dodge)
        {
            Vector3 v = act == Act.Dodge ? dodgeDir : PlanarVelocity();
            if (v.sqrMagnitude > 1f) wantYaw = Quaternion.LookRotation(v.normalized, Vector3.up).eulerAngles.y;
        }
        if (act == Act.Attack && attackDir.sqrMagnitude > .5f) wantYaw = Quaternion.LookRotation(attackDir, Vector3.up).eulerAngles.y;
        modelYaw = Mathf.LerpAngle(modelYaw, wantYaw, 1f - Mathf.Exp(-(act == Act.Attack ? 22f : 12f) * Time.deltaTime));
        model.rotation = Quaternion.Euler(0f, modelYaw, 0f) * BaseRotation;
        TickAnimation();
    }

    Quaternion baseRotation; bool baseReady;
    Quaternion BaseRotation
    {
        get
        {
            if (!baseReady) { var prefab = Resources.Load<GameObject>(Folder + "SteelSentinel"); baseRotation = prefab != null ? prefab.transform.localRotation : Quaternion.identity; baseReady = true; }
            return baseRotation;
        }
    }

    string Idle => dual ? "dual_idle" : "staff_idle";
    Vector3 PlanarVelocity() { if (body == null) return Vector3.zero; var v = body.velocity; v.y = 0f; return v; }

    void Locomotion()
    {
        player.MeleeMoveScale = 1f;
        float v = PlanarVelocity().magnitude;
        string prefix = dual ? "dual_" : "staff_";
        if (v < .4f) Loop(prefix + "idle", 1f, .22f);
        else if (v < 2.6f) Loop(prefix + "walk", Mathf.Clamp(v / 1.5f, .7f, 1.6f));
        else Loop(prefix + "run", Mathf.Clamp(v / 4.2f, .85f, 1.9f));
    }

    Attack NextLight()
    {
        var set = dual ? DualLight : StaffLight;
        if (combo >= set.Length) combo = 0;
        return set[combo++];
    }

    // ---- free-flow combat: every attack picks a zombie (stick / move keys, else the camera) and dashes to it ----
    const float DashRange = 5.5f, StopDistance = 1.35f, ChainWindow = 2.5f;
    ElderboomZombie target;
    Vector3 attackDir = Vector3.forward;
    int chain, finisherCharges, nextFinisherAt = 8;
    float chainUntil, tempo = 1f, damageScale = 1f;
    bool finisher;

    void ResetChain() { chain = 0; finisherCharges = 0; nextFinisherAt = 8; }

    ElderboomZombie PickTarget(out Vector3 want)
    {
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = transform.right; right.y = 0f; right.Normalize();
        float x = (ElderboomInput.Held("move_right") ? 1f : 0f) - (ElderboomInput.Held("move_left") ? 1f : 0f);
        float z = (ElderboomInput.Held("move_forward") ? 1f : 0f) - (ElderboomInput.Held("move_back") ? 1f : 0f);
        Vector2 stick = ElderboomGamepad.Move;
        if (stick.sqrMagnitude > .04f) { x = stick.x; z = stick.y; }
        bool steer = x * x + z * z > .04f;
        want = steer ? (right * x + fwd * z).normalized : fwd;
        var game = ElderboomGame.Active;
        if (game == null) return null;
        ElderboomZombie best = null; float bestScore = float.MinValue;
        Vector3 origin = transform.position, eye = origin + Vector3.up * 1.2f;
        foreach (var zombie in game.Zombies)
        {
            if (zombie == null || !zombie.IsAlive) continue;
            Vector3 to = zombie.transform.position - origin;
            if (Mathf.Abs(to.y) > 2.5f) continue;
            to.y = 0f;
            float d = to.magnitude;
            if (d > DashRange) continue;
            Vector3 n = d > .05f ? to / d : want;
            float dot = Vector3.Dot(n, want);
            if (d > 1.6f && dot < (steer ? .25f : 0f)) continue;
            float score = dot * 2f - d * .22f;
            if (!steer && zombie == target) score += .3f; // no direction given: stay on the same enemy
            if (score <= bestScore) continue;
            if (d > 2f && Physics.Linecast(eye, zombie.transform.position + Vector3.up * 1.2f, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<ElderboomZombie>() == null && !hit.transform.IsChildOf(transform)) continue; // wall in between
            best = zombie; bestScore = score;
        }
        return best;
    }

    bool IsHeavy(Attack a) => a == StaffHeavy || a == DualHeavy;
    // +1: the weapon comes in from the Sentinel's right, -1: from the left, 0: overhead or straight
    static float SwingSide(Attack a)
    {
        switch (a.clip)
        {
            case "staff_light1": case "dual_light1": case "staff_heavy": return 1f;
            case "staff_light2": case "dual_light2": return -1f;
        }
        return 0f;
    }

    void StartAttack(Attack a)
    {
        queuedLight = queuedHeavy = false;
        target = PickTarget(out attackDir);
        if (target != null)
        {
            Vector3 to = target.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > .01f) attackDir = to.normalized;
        }
        finisher = IsHeavy(a) && finisherCharges > 0;
        if (finisher) finisherCharges--;
        tempo = 1f + Mathf.Min(chain, 15) * .02f;
        damageScale = 1f + Mathf.Min(chain, 20) * .04f;
        attack = a; act = Act.Attack; actStart = Time.time; actEnd = Time.time + a.length / tempo;
        hit1Done = hit2Done = false; struck.Clear();
        comboResetAt = actEnd + .55f;
        player.MeleeMoveScale = target != null ? 0f : IsHeavy(a) ? .15f : .4f;
        Play(a.clip, .07f, false, tempo);
        Vector3 at = transform.position + Vector3.up * 1.2f;
        if (a == StaffHeavy) ElderboomAudio.Play("sentinel/swing_heavy", at, .6f, true);
        else if (dual) ElderboomAudio.PlayAny("sentinel/baton", 2, at, .5f, true, .1f);
        else ElderboomAudio.PlayAny("sentinel/swing", 3, at, .55f, true, .1f);
    }

    void TickAttack()
    {
        if (Time.time < hitStopUntil) { actStart += Time.deltaTime * .96f; actEnd += Time.deltaTime * .96f; return; }
        float t = (Time.time - actStart) * tempo;
        Lunge(t);
        if (!hit1Done && t >= attack.hitAt) { hit1Done = true; Strike(); }
        if (attack.hitAt2 > 0f && !hit2Done && t >= attack.hitAt2) { hit2Done = true; struck.Clear(); Strike(); }
        // chain into the next blow (and the next enemy) once this one has landed
        float cancelAt = IsHeavy(attack) ? attack.length : Mathf.Min(attack.length, attack.hitAt + .16f);
        if (t >= cancelAt && (queuedLight || queuedHeavy))
        {
            if (queuedHeavy) StartAttack(dual ? DualHeavy : StaffHeavy); else StartAttack(NextLight());
            return;
        }
        if (Time.time >= actEnd) Finish();
    }

    // Before the blow lands: dash to the target so the hit connects at arm's length. Without a target the blow
    // just carries the body forward a little.
    void Lunge(float t)
    {
        if (body == null || !body.enabled || t > attack.hitAt + .04f) return;
        if (target != null && target.IsAlive)
        {
            Vector3 to = target.transform.position - transform.position; to.y = 0f;
            float d = to.magnitude;
            if (d > .05f) attackDir = to / d;
            float gap = d - StopDistance;
            if (gap <= .02f) return;
            float left = Mathf.Max((attack.hitAt - t) / tempo, Time.deltaTime);
            body.Move(attackDir * Mathf.Min(gap, Mathf.Min(gap / left, 22f) * Time.deltaTime));
            return;
        }
        const float lead = .24f;
        if (attack.lunge <= 0f || t < attack.hitAt - lead) return;
        int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up + attackDir * 1.0f, .65f, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (overlaps[i] != null && overlaps[i].GetComponentInParent<ElderboomZombie>() != null) return;
        body.Move(attackDir * (attack.lunge / (lead + .04f)) * tempo * Time.deltaTime);
    }

    void Strike()
    {
        Vector3 origin = transform.position, forward = attackDir;
        bool around = attack.around || finisher;
        float range = attack.range * (finisher ? 1.45f : 1f);
        float damage = attack.damage * damageScale * (finisher ? 2.2f : 1f) * (Upgraded ? 2f : 1f);
        float push = finisher ? Mathf.Max(attack.push, 18f) : attack.push, stun = finisher ? Mathf.Max(attack.stun, .9f) : attack.stun;
        Vector3 center = around ? origin + Vector3.up : origin + Vector3.up + forward * (range * .5f);
        float radius = around ? range : range * .62f;
        int n = Physics.OverlapSphereNonAlloc(center, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        int landed = 0;
        bool big = finisher || IsHeavy(attack) || attack.trauma > .25f;
        for (int i = 0; i < n; i++)
        {
            var z = overlaps[i] != null ? overlaps[i].GetComponentInParent<ElderboomZombie>() : null;
            if (z == null || !struck.Add(z)) continue;
            Vector3 to = z.transform.position - origin; to.y = 0f;
            float distance = to.magnitude;
            if (distance > range + .6f) continue;
            Vector3 dir = distance > .05f ? to / distance : forward;
            if (!around && distance > .9f && Vector3.Dot(forward, dir) < attack.arcDot) continue;
            Vector3 point = z.transform.position + Vector3.up * 1.25f - dir * .25f;
            // where the blow comes from, as the zombie sees it: towards the Sentinel, pulled to the side the weapon swings in from
            Vector3 from = -dir * .5f + Vector3.Cross(Vector3.up, dir) * SwingSide(attack);
            z.TakeMeleeDamage(damage, origin + Vector3.up * 1.2f, point, -dir, finisher || IsHeavy(attack) || attack.trauma > .25f, from);
            if (z != null)
            {   // every blow shoves the body: a short knock on light hits, the full throw on heavy ones
                if (push > 0f) z.PushFrom(origin, push, stun); else z.PushFrom(origin, 4.5f, .12f);
                ElderboomCombatVfx.Burst(point, new Color(1f, .82f, .5f), big ? .5f : .28f);
                if (big) { ElderboomImportedVfx.Blood(point, -dir, true); ElderboomBloodSplat.FromHit(point, dir, false); }
            }
            landed++;
        }
        bool heavy = IsHeavy(attack);
        if (attack == DualHeavy) ElderboomAudio.Play("sentinel/slam", transform.position + forward, .85f, true); // hits the ground either way
        if (landed == 0) return;
        chain++; chainUntil = Time.time + ChainWindow;
        if (chain >= nextFinisherAt)
        {
            finisherCharges++; nextFinisherAt += nextFinisherAt == 8 ? 7 : 8;
            ElderboomAudio.Play("sentinel/join", transform.position + Vector3.up, .5f, false, 1.5f);
        }
        player.ConfirmHit();
        // the blow has to be felt: freeze frame, camera punch, loud close impact, rumble (AddTrauma)
        player.AddTrauma(finisher ? .75f : big ? .5f : .3f);
        player.AddFovKick(finisher ? 7f : big ? 4.5f : 2.2f);
        float stop = finisher ? .16f : big ? .11f : .065f;
        if (ElderboomLan.Current == ElderboomLan.Mode.Offline && Time.timeScale > .5f)
        {   // solo: the whole world holds for an instant
            frozen = true; freezeUntil = Time.unscaledTime + stop; Time.timeScale = .03f;
        }
        else hitStopUntil = Time.time + stop; // co-op: only the Sentinel's own animation holds
        Vector3 ear = cam != null ? cam.position : transform.position;
        if (big)
        {
            ElderboomAudio.Play("sentinel/hit_heavy", ear, 1f, false, Random.Range(.94f, 1.05f));
            ElderboomAudio.Play("kenney/impactPunch_heavy_00" + Random.Range(0, 5), ear, .8f, false, Random.Range(.8f, .9f));
        }
        else
        {
            ElderboomAudio.PlayAny("sentinel/hit", 3, ear, .95f, false, .08f);
            ElderboomAudio.Play("kenney/impactPunch_medium_00" + Random.Range(0, 5), ear, .55f, false, Random.Range(.9f, 1.05f));
        }
    }

    // Freeze frame on impact (solo only). Runs on unscaled time; never touches a pause (time scale 0).
    bool frozen; float freezeUntil;
    void TickFreeze()
    {
        if (!frozen) return;
        if (Time.unscaledTime < freezeUntil && Time.timeScale > 0f) return;
        frozen = false;
        if (Time.timeScale > 0f && Time.timeScale < .5f) Time.timeScale = 1f;
    }

    void StartDodge()
    {
        Vector3 v = PlanarVelocity();
        dodgeDir = v.sqrMagnitude > .5f ? v.normalized : -transform.forward;
        act = Act.Dodge; actStart = Time.time; actEnd = Time.time + .5f;
        player.MeleeMoveScale = .2f; player.InvulnerableUntil = Time.time + .32f;
        queuedLight = queuedHeavy = false;
        Play(dual ? "dual_dodge" : "staff_dodge", .06f, false, 1.25f);
        ElderboomAudio.Play("sentinel/dodge", transform.position + Vector3.up, .5f, true);
    }

    void StartSwitch()
    {
        act = Act.Switch; actStart = Time.time; actEnd = Time.time + 22f / 30f;
        player.MeleeMoveScale = .5f; combo = 0;
        Play(dual ? "dual_combine" : "staff_split", .08f, false);
        dual = !dual;
        ElderboomAudio.Play(dual ? "sentinel/split" : "sentinel/join", transform.position + Vector3.up, .6f, true);
    }

    void Finish()
    {
        act = Act.None; player.MeleeMoveScale = 1f;
        Play(Idle, .18f, true);
    }

    void HideViewmodel()
    {
        var root = player.ViewmodelParent;
        if (root == null || root == transform) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (model != null && r.transform.IsChildOf(model)) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
        }
    }

    // ---- camera: behind the right shoulder, pulled in by walls ----------------------------
    void LateUpdate()
    {
        if (!Active || cam == null || player == null || player.Seated) return;
        Vector3 pivot = transform.position + Vector3.up * 1.8f;
        Quaternion look = cam.rotation;
        Vector3 offset = look * new Vector3(.7f, .12f, -3.0f);
        float want = offset.magnitude; Vector3 dir = offset / want;
        float allowed = want;
        int n = Physics.SphereCastNonAlloc(pivot, .22f, dir, camHits, want, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = camHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<ElderboomZombie>() != null) continue;
            if (hit.distance > 0f) allowed = Mathf.Min(allowed, hit.distance);
        }
        camDistance = allowed < camDistance ? allowed : Mathf.MoveTowards(camDistance, allowed, 6f * Time.deltaTime);
        cam.position = pivot + dir * Mathf.Max(.35f, camDistance);
    }

    void OnGUI()
    {
        if (!Active || player == null || player.IsDead || player.SettingsOpen) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.LowerCenter };
        style.normal.textColor = new Color(1f, 1f, 1f, .7f);
        GUI.Label(new Rect(0, Screen.height - 58, Screen.width, 22),
            (dual ? "TWIN BATONS" : "BATTLE STAFF") + "   ·   " + ElderboomInput.Prompt("fire") + " combo   ·   " + ElderboomInput.Prompt("aim") + " heavy   ·   " +
            ElderboomInput.Prompt("reload") + (dual ? " join" : " split") + "   ·   " + ElderboomInput.Prompt("crouch") + " dodge", style);
        if (chain >= 2)
        {
            float life = Mathf.Clamp01((chainUntil - Time.time) / ChainWindow);
            var big = new GUIStyle(GUI.skin.label) { fontSize = 46 + Mathf.Min(chain, 20), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            big.normal.textColor = new Color(1f, finisherCharges > 0 ? .55f : .92f, finisherCharges > 0 ? .2f : .85f, act == Act.Attack ? 1f : .35f + .65f * life);
            GUI.Label(new Rect(Screen.width - 380, Screen.height * .36f, 320, 70), "x" + chain, big);
            var small = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            small.normal.textColor = new Color(1f, .6f, .25f, 1f);
            if (finisherCharges > 0) GUI.Label(new Rect(Screen.width - 480, Screen.height * .36f + 62, 420, 24), "FINISHER READY  ·  " + ElderboomInput.Prompt("aim"), small);
        }
        if (aimTarget != null && aimTarget.IsAlive && cam != null)
        {   // marker on the enemy the attack goes to: dim while choosing, bright during the blow
            Vector3 sp = cam.GetComponent<Camera>().WorldToScreenPoint(aimTarget.transform.position + Vector3.up * 2.05f);
            if (sp.z > 0f)
            {
                bool hitting = act == Act.Attack; float size = hitting ? 10f : 7f;
                GUI.color = new Color(1f, .75f, .3f, hitting ? .95f : .5f);
                GUI.DrawTexture(new Rect(sp.x - size * .5f, Screen.height - sp.y - size * .5f, size, size), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }
    }
}
