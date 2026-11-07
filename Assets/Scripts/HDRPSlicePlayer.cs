using System.Collections.Generic;
using UnityEngine;

// The player is a facade over separate parts:
//   HDRPSlicePlayer.cs           health, damage, session state, update order
//   HDRPSlicePlayer.Movement.cs  motor state machine, look, move, crouch/slide/vault
//   HDRPSlicePlayer.Weapon.cs    weapon state machine, fire, reload, weapon binding
//   HDRPSlicePlayer.Viewmodel.cs sway, equip draw, obstruction
//   ElderboomPauseMenu           settings (load/save/apply) and the pause menu
//   ElderboomInput               key bindings
// Its public API is unchanged, so other scripts and the regression keep working.
public sealed partial class HDRPSlicePlayer : MonoBehaviour
{

    [Header("Movement")]
    [SerializeField] float walkSpeed = 4.2f;
    [SerializeField] float sprintSpeed = 6.1f;
    [SerializeField] float mouseSensitivity = 1.8f;
    [SerializeField] float jumpHeight = 1.0f;
    [Header("AKMN")]
    [SerializeField] int magazineSize = 30;
    [SerializeField] int ammo = 30;
    [SerializeField] int reserveAmmo = 120;
    [SerializeField] float damage = 34f;
    [SerializeField] float fireInterval = 0.12f;
    [SerializeField] Camera viewCamera;
    [SerializeField] Transform weaponRoot;
    [SerializeField] Transform adsRearSight;
    [SerializeField] Transform adsFrontSight;
    [SerializeField] Transform rifleMuzzle;
    [SerializeField] Animator weaponAnimator;
    [SerializeField] Animation weaponLegacyAnimation;
    [SerializeField] string idleClipName = "Idle_Breathing";
    [SerializeField] string reloadClipName = "Reload_AK";
    [SerializeField] string abilityClipName = "Special_Plasma";
    [SerializeField] Light muzzleFlash;

    [Header("Ported game state")]
    [SerializeField] string operatorDisplayName = ElderboomIds.Soph;
    [SerializeField] string weaponDisplayName = "AKMN ZENITCO";
    [SerializeField] Transform sophViewmodel;
    [SerializeField] Transform eliViewmodel;
    [SerializeField] Transform tacticalViewmodel;

    CharacterController character;
    ElderboomPauseMenu pause;
    float verticalSpeed;
    float pitch;
    float nextShotTime;
    float health = 100f;
    readonly ElderboomHitMarker hitMarker=new ElderboomHitMarker();
    public ElderboomHitMarker HitMarker => hitMarker;
    float spreadBloom;
    float speedPerk;
    public void AddSpeedPerk(float amount){walkSpeed+=amount;sprintSpeed+=amount;speedPerk+=amount;}
    int score;
    bool reloading;
    bool cursorLocked = true;
    bool dead;
    Vector3 hipPosition;
    Vector3 adsPosition;
    Quaternion hipRotation;
    Quaternion adsRotation;
    bool hasAdsReference;
    bool aiming;
    bool poseReady;
    Vector3 posePosition;
    Quaternion poseRotation = Quaternion.identity;
    float sprintPose;
    static readonly Vector3 SprintDrop = new Vector3(0.05f, -0.075f, -0.05f);
    static readonly Quaternion SprintTilt = Quaternion.Euler(12f, -7f, 5f);
    float viewSprint, headBobTime, bobWeight, landingOffset, landingVelocity, moveX, moveZ;
    bool locomoting, wasGrounded = true, cameraBaseReady, wasAiming;
    Vector3 cameraBaseLocal;
    readonly ElderboomRecoil recoil = new ElderboomRecoil();
    readonly ElderboomViewKick viewKick = new ElderboomViewKick();
    readonly List<(Vector3 from, float at)> recentHits = new List<(Vector3 from, float at)>();
    float damagePulse, heartAt;
    public float DamagePulse => damagePulse;
    public IReadOnlyList<(Vector3 from, float at)> RecentHits => recentHits;
    public void AddTrauma(float amount) => viewKick.AddTrauma(amount);
    float fovKick;
    float stamina=100f,regenAt,footstepAt,sprintStarted;
    bool exhausted,sprinting;
    string audioCategory=ElderboomIds.Category.AssaultRifle;
    ElderboomViewmodel portViewmodel;
    ElderboomWeaponDefinition definition;
    public Transform ActiveWeaponRoot=>weaponRoot;
    public float Stamina => stamina;

    public float Health => health;
    public bool IsDead => dead;
    public int Score => ElderboomGame.Active != null ? ElderboomGame.Active.TotalScore : score;
    public bool HitMarkerVisible => hitMarker.IsVisible(Time.time);
    public Camera ViewCamera => viewCamera;
    public Transform MainMuzzle => rifleMuzzle;
    public bool WeaponDeposited { get; internal set; }
    public Transform AbilityMuzzle => ElderboomWeaponFraming.FindNamed(weaponRoot,"Soph_Plasma_Muzzle");
    public void ConfirmHit(){hitMarker.Notify(Time.time);ElderboomGame.Active?.ReportHit();}
    public void AddLegacyScore(int amount){score+=amount;}
    public void CancelGameplayActions()
    {
        StopAllCoroutines();hitMarker.Reset();reloading=aiming=false;spreadBloom=fovKick=0;recoil.Reset();
        if(muzzleFlash!=null)muzzleFlash.enabled=false;
        portViewmodel?.ResetToIdle();
        foreach(var stream in GetComponentsInChildren<ElderboomFireStream>(true))Destroy(stream.gameObject);
    }
    public void ResetSession()
    {
        ElderboomInput.ReleaseAll(); regenAt=0;
        CancelGameplayActions();walkSpeed-=speedPerk;sprintSpeed-=speedPerk;speedPerk=0;health=100;dead=false;score=0;Syringes=0;stamina=100;nextShotTime=0;
        sprinting=exhausted=false;pause?.ForceClose();Time.timeScale=1;AudioListener.pause=false;hitMarker.Reset();
        viewKick.Reset();recentHits.Clear();damagePulse=0;heartAt=0;ResetMovement();
    }
    public int Ammo => ammo;
    public int ReserveAmmo => reserveAmmo;
    public string OperatorDisplayName => operatorDisplayName;
    public string WeaponDisplayName => weaponDisplayName + (definition != null && definition.upgraded ? " // CRIMSON" : "");
    public bool SettingsOpen => pause != null && pause.IsOpen;
    public bool ActionDown(string action) => Down(action);
    public string ActionLabel(string action) => ElderboomInput.Label(action);
    public bool Reloading => reloading;
    public int Syringes {get;private set;}
    static bool Down(string action) => ElderboomInput.Down(action);
    static bool Held(string action) => ElderboomInput.Held(action);
    public void AddSyringe(){Syringes=Mathf.Min(3,Syringes+1);}
    void Awake()
    {
        character = GetComponent<CharacterController>();
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        pause = GetComponent<ElderboomPauseMenu>() ?? gameObject.AddComponent<ElderboomPauseMenu>();
        pause.Initialize(this, mouseSensitivity);
        EnsureViewmodelRoot();
        InitMovement();
        ammo = Mathf.Clamp(ammo, 0, magazineSize);
        if (weaponRoot != null) ApplyWeaponPose(weaponRoot);
        PlayIdle();
        SetCursor(true);
    }

    void Update()
    {
        // Escape and the pause menu are handled by ElderboomPauseMenu, which runs first.
        if (SettingsOpen || !cursorLocked || dead || !ElderboomInput.AcceptsInput) return;
        if(Down("use_syringe") && Syringes>0 && health<100){Syringes--;Heal(40);}
        UpdateFlashlightInput();
        if(Down("cycle_ability") && ElderboomGame.Active!=null)ElderboomGame.Active.SelectAbility((ElderboomGame.Active.SelectedAbility+1)%ElderboomGame.Active.AbilityCount);

        spreadBloom=Mathf.MoveTowards(spreadBloom,0f,Time.deltaTime*3f);
        damagePulse=Mathf.MoveTowards(damagePulse,0f,Time.deltaTime*1.4f);
        recentHits.RemoveAll(h=>Time.time-h.at>1.3f);
        if(health<35f && Time.time>=heartAt)
        {
            float danger=1f-health/35f;
            ElderboomAudio.PlayClip(ElderboomFeel.Heartbeat,transform.position,Mathf.Lerp(.25f,.6f,danger));
            heartAt=Time.time+Mathf.Lerp(1.05f,.72f,danger);
        }
        Look();
        if (!Seated) Move(); // passenger on the mechanical horse: look, aim and shoot only

        UpdateMotorState();

        var arms = GetComponent<ElderboomTacticalArms>();
        bool abilityBusy = ElderboomGame.Active != null && ElderboomGame.Active.AbilityBusy;
        // Abilities play on the left arm: the right hand keeps firing (no aiming or reloading meanwhile).
        // Only an ability that took the weapon away blocks the trigger.
        var arsenal = GetComponent<ElderboomArsenal>();
        bool handBusy = abilityBusy && ((arms != null && arms.HidesWeapon) || (arsenal != null && arsenal.AbilityHidesWeapon));
        UpdateWeaponPose(arms, abilityBusy);
        ApplyViewBob();
        UpdateWeaponActions(abilityBusy, handBusy);
    }

    // Riding behind on the mechanical horse (ElderboomMount): no walking, everything else as usual.
    public bool Seated { get; set; }

    public void TakeDamage(float amount) => TakeDamage(amount, transform.position);

#if UNITY_EDITOR
    public static bool DevInvulnerable; // Elderboom HDRP > Dev > Toggle Invulnerable (Editor only)
#endif
    public void TakeDamage(float amount, Vector3 source)
    {
#if UNITY_EDITOR
        if (DevInvulnerable && amount < 999f) return;
#endif
        if (dead || amount<=0) return;
        health = Mathf.Max(0f, health - amount);
        ElderboomGame.Active?.ReportPlayerDamage(amount);
        ElderboomEvents.RaisePlayerDamaged(amount, source);
        // Hit feedback: flinch, trauma, red pulse and a direction marker.
        damagePulse = Mathf.Min(1f, damagePulse + .35f + amount / 40f);
        viewKick.AddTrauma(Mathf.Clamp(amount / 30f, .15f, .5f));
        viewKick.Kick(-Random.Range(.6f, 1.3f), Random.Range(-1.6f, 1.6f));
        Vector3 flat = source - transform.position; flat.y = 0f;
        if (flat.sqrMagnitude > .01f) { recentHits.Add((source, Time.time)); if (recentHits.Count > 4) recentHits.RemoveAt(0); }
        if (health <= 0f)
        {
            dead=true;CancelGameplayActions();
            ElderboomGame.Active?.PlayerDied();
        }
    }

    public void Heal(float amount)
    {
        if (dead) return;
        health = Mathf.Clamp(health + Mathf.Max(0f, amount), 0f, 100f);
    }

    internal void SetCursor(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnGUI()
    {
        if (SettingsOpen) return; // ElderboomPauseMenu draws the menu and the performance overlay.
        if (dead)
        {
            // ElderboomGame draws its own game-over screen at the same height.
            if (ElderboomGame.Active == null)
                DrawCentered("YOU ARE DOWN  ·  Reload the scene to try again", 30, Screen.height * 0.43f);
            return;
        }
        if(ElderboomGame.Active!=null)return;
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        GUI.color = Color.white;
        if(HitMarkerVisible)hitMarker.Draw(new Vector2(cx,cy),Time.time);
        else
        {
            GUI.DrawTexture(new Rect(cx - 8, cy - 1, 16, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, cy - 8, 2, 16), Texture2D.whiteTexture);
        }

        GUI.color = new Color(0.03f, 0.04f, 0.05f, 0.72f);
        GUI.DrawTexture(new Rect(24, Screen.height - 92, 178, 62), Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.17f, 0.11f, 0.95f);
        GUI.DrawTexture(new Rect(34, Screen.height - 42, 158 * health / 100f, 5), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(34, Screen.height - 85, 180, 25), operatorDisplayName + "  /  " + Mathf.CeilToInt(health) + " HP");
        GUI.Label(new Rect(Screen.width - 300, Screen.height - 82, 270, 34), reloading ? "RELOADING  ·  " + weaponDisplayName : $"{weaponDisplayName}     {ammo:00} / {reserveAmmo:000}", new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold });
        GUI.Label(new Rect(24, Screen.height - 24, 700, 22), "WASD move   ·   Shift sprint   ·   Space jump   ·   RMB aim   ·   LMB fire   ·   R reload   ·   1–3 select ability   ·   Q use   ·   Esc settings", new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.8f, 0.84f, 0.88f) } });
        GUI.Label(new Rect(Screen.width - 150, 26, 120, 32), "SCORE  " + score, new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold });
    }

    static void DrawCentered(string text, int size, float y)
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(0, y, Screen.width, 42), text, new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
    }
}
