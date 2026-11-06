using UnityEngine;
using UnityEngine.SceneManagement;

// Rideable mechanical horse (MechHorse_Rider.glb, exported from Mechanical_Horse_Gallop.blend).
// The horse becomes rideable only after the scene's salvage/assembly objective is complete.
//   interact (E)  mount / dismount within mountRange
//   W / S         ride forward / back up      A / D  steer
//   sprint        (Shift) gallop               mouse  look around from the saddle
// While riding, the player's own controller and viewmodel are switched off and its camera
// sits on the "RiderCamera" node of the horse, so the rider arms (hands on the grips) stay in view.
//   toggle_view (V)  third person: the operator's full body (ElderboomCharacterBody, clip "Ride")
//                    sits in the saddle and the camera orbits behind the horse (mouse = orbit).
[RequireComponent(typeof(CharacterController))]
public sealed class ElderboomMount : MonoBehaviour
{
    const string ResourcePath = "Elderboom/Mounts/MechHorse_Rider";
    const string SceneName = "Elderboom_Unity_Port";

    [SerializeField] float walkSpeed = 3.5f;
    [SerializeField] float gallopSpeed = 9f;
    [SerializeField] float reverseSpeed = 2f;
    [SerializeField] float turnSpeed = 95f;
    [SerializeField] float acceleration = 5f;
    [SerializeField] float mountRange = 3f;
    [SerializeField] float lookYawLimit = 120f;
    [SerializeField] float lookPitchLimit = 65f;

    HDRPSlicePlayer player;
    CharacterController body, playerBody;
    Transform riderCamera, riderArms;
    Animation anim;
    string gallopClip, idleClip, currentClip;
    bool mounted;
    int mountedFrame;
    float speed, verticalSpeed, lookYaw, lookPitch;
    Transform cameraParent, playerParent;
    Vector3 cameraLocalPos;
    Quaternion cameraLocalRot;
    Renderer[] hiddenRenderers;

    // Third person
    [SerializeField] float thirdPersonDistance = 4.6f;
    [SerializeField] float thirdPersonHeight = .55f;
    // Fine tuning of the seat in horse space (x right, y up, z forward), applied after the hands meet the grips.
    [SerializeField] Vector3 riderSeatOffset = Vector3.zero;
    bool thirdPerson, riderPlaced;
    ElderboomCharacterBody rider;
    Vector3 orbitVelocity;
    float orbitDistance;
    static readonly RaycastHit[] orbitHits = new RaycastHit[16];

    public bool Mounted => mounted;
    public bool Assembled { get; private set; }
    int assembledFrame;

    public void SetAssembled(bool value)
    {
        if (!value) ForceLeave();
        if (Assembled != value) assembledFrame = Time.frameCount;
        Assembled = value;
        var model = transform.Find("MechHorse_Rider");
        if (model != null) model.gameObject.SetActive(value);
        if (body != null) body.enabled = value;
        if (value) { currentClip = null; PlayClip(idleClip, 0f); }
        if (!value) { speed = verticalSpeed = 0; NetDriver = NetPassenger = -1; netPose = false; }
    }

    public void ResetAtWorkshop(Vector3 position, Quaternion rotation)
    {
        SetAssembled(false);
        transform.SetPositionAndRotation(position, rotation);
    }

    // ---- Multiplayer and passenger seat (ElderboomLan) -----------------------------------
    // One shared horse: whoever drives moves it and sends its pose; the host assigns the two seats
    // (driver, passenger) and lists them in its snapshots. The passenger sits behind and keeps the
    // normal first-person view: looks around, aims and shoots, but does not walk.
    public static ElderboomMount Instance { get; private set; }
    public bool Passenger => passenger;
    public int NetDriver = -1, NetPassenger = -1;
    bool passenger, netPose;
    Vector3 netPos; float netYaw;
    static readonly Vector3 PassengerSeat = new Vector3(0f, .35f, -.62f);
    static int LocalNetId => ElderboomLan.Instance != null ? ElderboomLan.Instance.LocalId : 0;
    static bool Online => ElderboomLan.Current != ElderboomLan.Mode.Offline;
    public void SetNetPose(Vector3 position, float yaw, float horseSpeed) { netPos = position; netYaw = yaw; if (!mounted) speed = horseSpeed; netPose = true; }
    // Another player took this seat first (the host decides): leave it.
    public void ForceLeave() { if (mounted) Dismount(); if (passenger) DismountPassenger(); }

    void MountPassenger()
    {
        passenger = true; mountedFrame = Time.frameCount;
        playerBody = player.GetComponent<CharacterController>();
        if (playerBody != null) playerBody.enabled = false;
        playerParent = player.transform.parent;
        player.transform.SetParent(transform, true);
        player.transform.SetLocalPositionAndRotation(PassengerSeat, Quaternion.identity);
        player.Seated = true;
        ElderboomAudio.Play("menu/single_mouse_click", transform.position, .5f);
    }

    void DismountPassenger()
    {
        passenger = false;
        player.Seated = false;
        Vector3 facing = player.transform.forward; facing.y = 0f;
        player.transform.SetParent(playerParent, true);
        player.transform.SetPositionAndRotation(ExitPoint(), Quaternion.LookRotation(facing.sqrMagnitude > .01f ? facing : transform.forward));
        if (playerBody != null) playerBody.enabled = true;
    }
    public bool ThirdPerson => thirdPerson;
    public float Speed => speed;

    // ---- Spawning ------------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Spawn(scene);

    static void Spawn(Scene scene)
    {
        if (scene.name != SceneName || FindAnyObjectByType<ElderboomMount>() != null) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        var target = FindAnyObjectByType<HDRPSlicePlayer>();
        if (prefab == null || target == null)
        {
            Debug.LogWarning("[ElderboomMount] Horse not spawned: " + (prefab == null ? "missing Resources/" + ResourcePath : "no player in scene"));
            return;
        }
        var assembly = FindAnyObjectByType<ElderboomHorseAssembly>();
        if (assembly == null) { Debug.LogWarning("[ElderboomMount] No workshop configured; horse remains unavailable."); return; }
        var root = new GameObject("MechHorse");
        root.transform.SetPositionAndRotation(assembly.transform.position, assembly.transform.rotation);
        var model = Instantiate(prefab, root.transform, false);
        model.name = "MechHorse_Rider";
        root.AddComponent<ElderboomMount>();
    }

    // ---- Setup ---------------------------------------------------------------------------
    void Awake()
    {
        Instance = this;
        body = GetComponent<CharacterController>();
        body.height = 1.8f; body.radius = .55f; body.center = new Vector3(0f, .95f, 0f);
        body.stepOffset = .45f; body.slopeLimit = 50f; body.skinWidth = .05f;
        riderCamera = FindDeep(transform, "RiderCamera");
        riderArms = FindDeep(transform, "RiderArms");
        anim = GetComponentInChildren<Animation>();
        if (anim != null)
        {
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState state in anim)
            {
                state.wrapMode = WrapMode.Loop;
                if (gallopClip == null && state.name.Contains("Gallop")) gallopClip = state.name;
                else if (idleClip == null && state.name.Contains("Idle")) idleClip = state.name;
            }
        }
        if (riderCamera == null) Debug.LogWarning("[ElderboomMount] RiderCamera node not found in the horse model.");
        if (anim == null || gallopClip == null) Debug.LogWarning("[ElderboomMount] Gallop animation not found; set the glTF import to Legacy animation.");
        SetRiderArms(false);
        PlayClip(idleClip, 0f);
        SetAssembled(ElderboomHorseAssembly.Instance != null && ElderboomHorseAssembly.Instance.Built);
    }

    void Start() => player = FindAnyObjectByType<HDRPSlicePlayer>();

    // ---- Update --------------------------------------------------------------------------
    void Update()
    {
        if (!Assembled || Time.frameCount == assembledFrame) return;
        if (player == null) { player = FindAnyObjectByType<HDRPSlicePlayer>(); if (player == null) return; }
        bool paused = player.SettingsOpen || Time.timeScale == 0f || !ElderboomInput.AcceptsInput;
        if (paused && mounted) speed = 0f;

        if (passenger)
        {
            if (player.IsDead || player.transform.parent != transform) DismountPassenger();
            else if (!paused && Time.frameCount != mountedFrame && player.ActionDown("interact")) DismountPassenger();
            else player.transform.localPosition = PassengerSeat;
        }
        else if (!mounted)
        {
            if (!paused && !player.IsDead && InRange() && CanInteract() && player.ActionDown("interact"))
            {
                bool driverTaken = Online && NetDriver >= 0 && NetDriver != LocalNetId;
                bool passengerTaken = Online && NetPassenger >= 0 && NetPassenger != LocalNetId;
                if (!driverTaken) Mount();
                else if (!passengerTaken) MountPassenger();
                else ElderboomGame.Active?.ShowNotice("BOTH SEATS ARE TAKEN", 2f);
            }
        }
        else if (player.IsDead || player.enabled)
        {
            Dismount(); // died, or something else (a session reset) took the player back
        }
        else if (!paused)
        {
            if (Time.frameCount != mountedFrame && player.ActionDown("interact")) Dismount();
            else
            {
                if (Time.frameCount != mountedFrame && ElderboomInput.Down("toggle_view")) SetThirdPerson(!thirdPerson);
                Look(); Ride();
            }
        }

        // Driven by another player, or a client's copy of the host's horse: follow the network pose.
        if (!mounted && netPose && (ElderboomLan.IsClient || (NetDriver >= 0 && NetDriver != LocalNetId)))
        {
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            if ((transform.position - netPos).sqrMagnitude > 25f) transform.position = netPos;
            else transform.position = Vector3.Lerp(transform.position, netPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw, 0f), k);
        }
        else
        {
            if (!mounted) speed = Mathf.MoveTowards(speed, 0f, acceleration * 2f * Time.deltaTime);
            ApplyMotion();
        }
        Animate();
    }

    bool InRange() => player != null && Vector3.Distance(player.transform.position, transform.position) <= mountRange;
    static bool CanInteract() => ElderboomGame.Active == null || ElderboomGame.Active.CanInteract;

    void Look()
    {
        var pause = player.GetComponent<ElderboomPauseMenu>();
        float sensitivity = pause != null ? pause.Sensitivity : 1.8f;
        lookYaw = Mathf.Clamp(lookYaw + Input.GetAxisRaw("Mouse X") * sensitivity, -lookYawLimit, lookYawLimit);
        if (thirdPerson)
        {
            // Free orbit around the horse; pitch kept above the ground and below straight-down.
            lookYaw = Mathf.Repeat(lookYaw + Input.GetAxisRaw("Mouse X") * sensitivity + 180f, 360f) - 180f;
            lookPitch = Mathf.Clamp(lookPitch - Input.GetAxisRaw("Mouse Y") * sensitivity, -25f, 70f);
            return; // camera placed in LateUpdate, after the horse has moved
        }
        lookYaw = Mathf.Clamp(lookYaw + Input.GetAxisRaw("Mouse X") * sensitivity, -lookYawLimit, lookYawLimit);
        lookPitch = Mathf.Clamp(lookPitch - Input.GetAxisRaw("Mouse Y") * sensitivity, -lookPitchLimit, lookPitchLimit);
        if (player.ViewCamera != null) player.ViewCamera.transform.localRotation = Quaternion.Euler(lookPitch, lookYaw, 0f);
    }

    // ---- Third person ----------------------------------------------------------------------
    void SetThirdPerson(bool on)
    {
        if (on == thirdPerson) return;
        var cam = player != null && player.ViewCamera != null ? player.ViewCamera.transform : null;
        if (on)
        {
            string op = ElderboomGame.Active != null && ElderboomGame.Active.Operator != null ? ElderboomGame.Active.Operator.id : ElderboomMenu.Operator;
            if (rider != null && rider.OperatorId != op) { Destroy(rider.gameObject); rider = null; }
            if (rider == null)
            {
                rider = ElderboomCharacterBody.Create(op, transform);
                if (rider == null)
                {
                    Debug.Log("[ElderboomMount] No full-body model for operator " + op + "; third person unavailable.");
                    return;
                }
                riderPlaced = false;
            }
            if (cam == null) return;
            thirdPerson = true;
            rider.gameObject.SetActive(true);
            rider.Play("Ride", 0f);
            SetRiderArms(false);
            cam.SetParent(transform, true);
            lookYaw = 0f; lookPitch = 12f;
            orbitDistance = thirdPersonDistance;
            orbitVelocity = Vector3.zero;
        }
        else
        {
            thirdPerson = false;
            if (rider != null) rider.gameObject.SetActive(false);
            lookYaw = lookPitch = 0f;
            if (cam != null && riderCamera != null)
            {
                cam.SetParent(riderCamera, false);
                cam.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            SetRiderArms(mounted);
        }
    }

    void LateUpdate()
    {
        if (!mounted || !thirdPerson || rider == null) return;

        // Seat the body once its first pose exists: its hands on the grips the first-person arms hold
        // (the RiderArms mesh ends at the handlebars), plus a hand-tuned offset in horse space.
        if (!riderPlaced)
        {
            var lh = rider.Bone(HumanBodyBones.LeftHand); var rh = rider.Bone(HumanBodyBones.RightHand);
            if (lh != null && rh != null && TryGripPoint(out Vector3 grip))
            {
                Vector3 hands = (lh.position + rh.position) * .5f;
                rider.transform.position += grip - hands + transform.TransformVector(riderSeatOffset);
                riderPlaced = true;
            }
            else if (riderCamera != null && rider.Bone(HumanBodyBones.Head) is Transform head)
            {
                Vector3 eye = riderCamera.position - transform.up * .1f - transform.forward * .08f;
                rider.transform.position += eye - head.position + transform.TransformVector(riderSeatOffset);
                riderPlaced = true;
            }
        }

        // Ride clip speed follows the horse.
        rider.SetSpeed(Mathf.Lerp(.55f, 1.35f, Mathf.InverseLerp(0f, gallopSpeed, Mathf.Abs(speed))));

        var cam = player != null && player.ViewCamera != null ? player.ViewCamera.transform : null;
        if (cam == null) return;
        Vector3 pivot = (riderCamera != null ? riderCamera.position : transform.position + Vector3.up * 2f) + Vector3.up * thirdPersonHeight;
        Quaternion rot = Quaternion.Euler(lookPitch, transform.eulerAngles.y + lookYaw, 0f);
        Vector3 dir = rot * Vector3.back;
        float want = thirdPersonDistance;
        int n = Physics.SphereCastNonAlloc(pivot, .25f, dir, orbitHits, thirdPersonDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = orbitHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform) || (player != null && c.transform.IsChildOf(player.transform))) continue;
            if (orbitHits[i].distance > 0f) want = Mathf.Min(want, orbitHits[i].distance);
        }
        // Pull in instantly on a hit, ease back out when clear.
        orbitDistance = want < orbitDistance ? want : Mathf.MoveTowards(orbitDistance, want, 6f * Time.deltaTime);
        Vector3 target = pivot + dir * Mathf.Max(.6f, orbitDistance);
        cam.position = Vector3.SmoothDamp(cam.position, target, ref orbitVelocity, .04f);
        cam.rotation = Quaternion.LookRotation(pivot - cam.position, Vector3.up);
    }

    void Ride()
    {
        float throttle = (ElderboomInput.Held("move_forward") ? 1f : 0f) - (ElderboomInput.Held("move_back") ? 1f : 0f);
        float steer = (ElderboomInput.Held("move_right") ? 1f : 0f) - (ElderboomInput.Held("move_left") ? 1f : 0f);
        bool gallop = ElderboomInput.Held("sprint") && throttle > 0f;
        float target = throttle > 0f ? (gallop ? gallopSpeed : walkSpeed) : throttle < 0f ? -reverseSpeed : 0f;
        float rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration : acceleration * 1.8f;
        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);
        // Tighter turns when slow, wider at a gallop; reversing steers like backing up.
        float turnScale = Mathf.Lerp(1f, .5f, Mathf.InverseLerp(walkSpeed, gallopSpeed, Mathf.Abs(speed)));
        float direction = speed < -.1f ? -1f : 1f;
        transform.Rotate(0f, steer * turnSpeed * turnScale * direction * Time.deltaTime, 0f, Space.World);
    }

    void ApplyMotion()
    {
        if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        else verticalSpeed -= 20f * Time.deltaTime;
        body.Move((transform.forward * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    void Animate()
    {
        if (anim == null) return;
        float a = Mathf.Abs(speed);
        if (a < .15f || gallopClip == null) { PlayClip(idleClip, .3f); return; }
        PlayClip(gallopClip, .25f);
        anim[gallopClip].speed = Mathf.Sign(speed) * Mathf.Lerp(.45f, 1.1f, Mathf.InverseLerp(0f, gallopSpeed, a));
    }

    void PlayClip(string clip, float fade)
    {
        if (anim == null || clip == null || clip == currentClip) return;
        currentClip = clip;
        if (fade <= 0f) anim.Play(clip); else anim.CrossFade(clip, fade);
    }

    // ---- Mount / dismount ----------------------------------------------------------------
    void Mount()
    {
        if (!Assembled || riderCamera == null || player.ViewCamera == null) return;
        mounted = true; mountedFrame = Time.frameCount;
        lookYaw = lookPitch = 0f;
        player.CancelGameplayActions();
        playerBody = player.GetComponent<CharacterController>();
        if (playerBody != null) playerBody.enabled = false;
        player.enabled = false;

        var cam = player.ViewCamera.transform;
        cameraParent = cam.parent; cameraLocalPos = cam.localPosition; cameraLocalRot = cam.localRotation;
        hiddenRenderers = System.Array.FindAll(player.GetComponentsInChildren<Renderer>(false), r => r.enabled);
        foreach (var r in hiddenRenderers) r.enabled = false;

        playerParent = player.transform.parent;
        player.transform.SetParent(transform, true);
        player.transform.SetLocalPositionAndRotation(new Vector3(0f, .3f, 0f), Quaternion.identity);
        cam.SetParent(riderCamera, false);
        cam.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        SetRiderArms(true);
        ElderboomAudio.Play("menu/single_mouse_click", transform.position, .5f);
    }

    void Dismount()
    {
        SetThirdPerson(false);
        mounted = false;
        SetRiderArms(false);
        var cam = player.ViewCamera != null ? player.ViewCamera.transform : null;
        if (cam != null)
        {
            cam.SetParent(cameraParent, false);
            cam.SetLocalPositionAndRotation(cameraLocalPos, cameraLocalRot);
        }
        Vector3 facing = Quaternion.Euler(0f, lookYaw, 0f) * transform.forward; facing.y = 0f;
        player.transform.SetParent(playerParent, true);
        player.transform.SetPositionAndRotation(ExitPoint(), Quaternion.LookRotation(facing.sqrMagnitude > .01f ? facing : transform.forward));
        if (hiddenRenderers != null) foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
        hiddenRenderers = null;
        if (playerBody != null) playerBody.enabled = true;
        player.enabled = true;
    }

    // First free spot beside the horse (right, left, behind), dropped onto the ground.
    Vector3 ExitPoint()
    {
        float radius = playerBody != null ? playerBody.radius : .4f;
        float height = playerBody != null ? playerBody.height : 1.8f;
        Vector3[] offsets = { transform.right * 1.7f, -transform.right * 1.7f, -transform.forward * 2.4f, transform.forward * 2.4f };
        foreach (var offset in offsets)
        {
            Vector3 p = transform.position + offset;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            Vector3 bottom = p + Vector3.up * (radius + .05f), top = p + Vector3.up * (height - radius);
            if (!Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore)) return p + Vector3.up * .05f;
        }
        return transform.position + transform.right * 1.7f + Vector3.up * .1f;
    }

    // Handlebar point in world space: the front end of the first-person RiderArms mesh (where its hands are).
    bool TryGripPoint(out Vector3 grip)
    {
        grip = default;
        if (riderArms == null) return false;
        bool any = false; float maxZ = float.MinValue; Vector3 sum = Vector3.zero; int n = 0;
        foreach (var r in riderArms.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(r.transform.TransformPoint(c));
                maxZ = Mathf.Max(maxZ, local.z); sum += local; n++; any = true;
            }
        }
        if (!any) return false;
        Vector3 centre = sum / n;
        grip = transform.TransformPoint(new Vector3(centre.x, centre.y, maxZ - .07f));
        return true;
    }

    void SetRiderArms(bool visible)
    {
        if (riderArms != null) riderArms.gameObject.SetActive(visible);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name || t.name.StartsWith(name + "_") || t.name.StartsWith(name + ".")) return t;
        foreach (Transform child in t)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---- Prompt --------------------------------------------------------------------------
    void OnGUI()
    {
        if (!Assembled || player == null || player.IsDead || player.SettingsOpen) return;
        if (ElderboomGame.Active != null && !ElderboomGame.Active.IsPlaying) return;
        if (mounted)
        {
            GUI.Label(new Rect(24, Screen.height - 24, 760, 22),
                "W/S ride   ·   A/D steer   ·   " + ElderboomInput.Label("sprint") + " gallop   ·   mouse look   ·   " + ElderboomInput.Label("toggle_view") + (thirdPerson ? " first person" : " third person") + "   ·   " + player.ActionLabel("interact") + " dismount",
                new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(.8f, .84f, .88f) } });
        }
        else if (InRange())
        {
            GUI.Label(new Rect(Screen.width / 2 - 190, Screen.height * .67f, 380, 34), player.ActionLabel("interact") + " · RIDE", GUI.skin.box);
        }
    }
}
