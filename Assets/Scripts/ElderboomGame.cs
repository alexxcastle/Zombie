using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Session controller, split by concern (one class, several files):
//   ElderboomGame.cs            session state, score/credits, kills, power-ups, events
//   ElderboomGame.Waves.cs      wave loop, spawning, director intensity, atmosphere
//   ElderboomGame.Abilities.cs  ability selection, cooldowns, services used by ability assets
//   ElderboomGame.Menu.cs       in-scene operator menu and game-over screen
public sealed partial class ElderboomGame : MonoBehaviour
{
    public static ElderboomGame Active { get; private set; }

    enum State { Menu, Playing, GameOver }
    State state = State.Menu;
    HDRPSlicePlayer player;
    Camera playerCamera;
    readonly List<ElderboomZombie> zombies = new List<ElderboomZombie>();
    readonly List<Transform> spawnPoints = new List<Transform>();
    readonly List<Vector3> recentSpawns = new List<Vector3>();
    string selectedOperator = ElderboomIds.Soph;
    string selectedDifficulty = "NORMAL";
    int selectedAbility;
    int wave;
    int waveRemaining;
    int credits;
    bool spawning;
    // Cooldowns are per ability index; operators with sharedCooldown use index 0 for all.
    readonly float[] abilityReady = new float[8];
    readonly bool[] abilityCooling = new bool[8];
    string notice = "SELECT AN OPERATOR";
    float ignoredUntil;
    float abilityAnimationUntil;
    readonly List<GameObject> abilityObjects = new List<GameObject>();
    NavMeshPath spawnPath;
    int session;
    int totalScore;
    float noticeUntil;
    public int Session => session;
    public int TotalScore => totalScore;
    public HDRPSlicePlayer Player => player;
    public IReadOnlyList<ElderboomZombie> Zombies => zombies;
    public ElderboomOperatorDefinition Operator => ElderboomOperators.Find(selectedOperator) ?? ElderboomOperators.All[0];
    public string AbilityName => AbilityNames()[Mathf.Clamp(selectedAbility,0,AbilityNames().Length-1)];
    public string Notice => Time.time < noticeUntil ? notice : "";
    public bool IsSessionActive(int token) => token == session && state == State.Playing && player != null && !player.IsDead;
    public void ShowNotice(string message, float seconds = 4f) { notice=message; noticeUntil=Time.time+seconds; }
    // ---- Director, statistics, power-ups and atmosphere (phases 4-6) ----------------
    float intensity, instaKillUntil, doublePointsUntil, waveBannerAt = -10f, sessionStart, survivedSeconds, musicCheckAt, musicSetting = .8f;
    int kills, shotsFired, shotsHit, lastHitFrame = -1, wavesCleared;
    readonly List<(Vector3 position, int amount, float at)> popups = new List<(Vector3 position, int amount, float at)>();
    AudioSource musicSource, ambienceSource;
    public bool InstaKill => Time.time < instaKillUntil;
    public bool DoublePoints => Time.time < doublePointsUntil;
    public float InstaKillRemaining => Mathf.Max(0f, instaKillUntil - Time.time);
    public float DoublePointsRemaining => Mathf.Max(0f, doublePointsUntil - Time.time);
    public float WaveBannerAge => Time.time - waveBannerAt;
    public int Kills => kills;
    public IReadOnlyList<(Vector3 position, int amount, float at)> Popups => popups;
    public void ReportShot() { shotsFired++; }
    public void ReportHit() { if (Time.frameCount == lastHitFrame) return; lastHitFrame = Time.frameCount; shotsHit = Mathf.Min(shotsFired, shotsHit + 1); }
    public void ActivatePowerUp(string kind)
    {
        if (state != State.Playing || player == null) return;
        if (kind == "max_ammo")
        {
            var arsenal = player.GetComponent<ElderboomArsenal>();
            if (arsenal != null) arsenal.RefillAmmo(); else player.RefillAmmo();
            ShowNotice("MAX AMMO");
        }
        else if (kind == "insta_kill") { instaKillUntil = Time.time + 20f; ShowNotice("INSTA-KILL // 20 SECONDS"); }
        else { doublePointsUntil = Time.time + 20f; ShowNotice("DOUBLE POINTS // 20 SECONDS"); }
        ElderboomLan.BroadcastPowerUp(kind); // host: everybody gets it
        ElderboomAudio.Play("kenney/impactBell_heavy_00" + UnityEngine.Random.Range(0, 5), player.transform.position, .45f);
        ElderboomEvents.RaisePowerUpActivated(kind);
    }
    void ResetStats()
    {
        intensity = 0f; instaKillUntil = doublePointsUntil = 0f; waveBannerAt = -10f; sessionStart = Time.time; survivedSeconds = 0f;
        kills = shotsFired = shotsHit = wavesCleared = 0; lastHitFrame = -1; popups.Clear();
    }
    public bool CanInteract => IsPlaying && player != null && !player.IsDead && !player.SettingsOpen && !player.Reloading && !player.WeaponDeposited && !AbilityBusy;

    public void PlayerDied()
    {
        if(state != State.Playing)return;
        state=State.GameOver;survivedSeconds=Time.time-sessionStart;
        ElderboomEvents.RaisePlayerDied();
        CancelGameplay();
        Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
    }
    void CancelGameplay()
    {
        session++; StopAllCoroutines(); spawning=false;
        ignoredUntil=abilityAnimationUntil=0f;
        player?.CancelGameplayActions();
        player?.GetComponent<ElderboomTacticalArms>()?.Finish();
        player?.GetComponent<ElderboomArsenal>()?.CancelOperatorAbility();
        foreach(var zombie in zombies) if(zombie != null)zombie.CancelAbilityEffects();
        foreach(var go in abilityObjects) if(go != null){go.SetActive(false);Destroy(go);}
        abilityObjects.Clear();
        foreach(var bolt in FindObjectsByType<ElderboomPlasmaBolt>(FindObjectsSortMode.None))
            if(bolt.Owner==player){bolt.gameObject.SetActive(false);Destroy(bolt.gameObject);}
        foreach(var grenade in FindObjectsByType<ElderboomGrenadeProjectile>(FindObjectsSortMode.None))
            if(grenade.Owner==player){grenade.gameObject.SetActive(false);Destroy(grenade.gameObject);}
        ElderboomAudio.StopGameplay();
    }
    internal GameObject AbilityObject(GameObject go) { if(go != null)abilityObjects.Add(go); return go; }
    void OnDisable(){if(Application.isPlaying && Active==this)ElderboomAudio.StopGameplay();}
    void OnDestroy(){if(Active==this)Active=null;}
    public int Credits => credits;
    public bool IsPlaying => state == State.Playing;
    public bool PlayerIgnored => Time.time<ignoredUntil;
    public int SelectedAbility => selectedAbility;
    public int AbilityCount => AbilityNames().Length;
    public float AbilityRemaining => Mathf.Max(0, abilityReady[CooldownSlot(selectedAbility)] - Time.time);
    public bool AbilityBusy => Time.time < abilityAnimationUntil;
    public int Wave => wave;
    public int Remaining => waveRemaining;

    void Awake()
    {
        Active = this;spawnPath=new NavMeshPath();
        player = FindFirstObjectByType<HDRPSlicePlayer>();
        playerCamera = Camera.main;
        foreach (var marker in FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None))
            spawnPoints.Add(marker.transform);
        if (player != null) player.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
    }

    void Update()
    {
        zombies.RemoveAll(z => z == null || !z.gameObject.activeInHierarchy);
        RetireBlindSurvivors();
        abilityObjects.RemoveAll(go=>go==null);
        UpdateAtmosphere();
        if (state == State.Playing && player != null && TeamDown)
        {
            PlayerDied();
        }
        // Abilities with their own cooldown announce when they are ready again.
        if (state == State.Playing && !Operator.sharedCooldown)
        {
            var abilities = Operator.abilities;
            for (int i = 0; i < abilities.Length && i < abilityCooling.Length; i++)
            {
                if (!abilityCooling[i] || Time.time < abilityReady[i]) continue;
                abilityCooling[i] = false;
                if (i == selectedAbility && Time.time >= abilityAnimationUntil && abilities[i] != null)
                    ShowNotice(abilities[i].displayName + " ready [Q]");
            }
        }
    }

    void Start()
    {
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        // Operator and difficulty are chosen in the main menu (PLAY). Opening this scene
        // directly (Editor) starts at once with the last choice instead of a second menu.
        ElderboomMenu.LaunchRequested=false;
        BeginSession(ElderboomMenu.Operator,ElderboomMenu.Difficulty);
    }

    public void BeginSession(string operatorId,string difficulty)
    {
        CancelGameplay();
        foreach(var zombie in zombies) if(zombie != null){zombie.gameObject.SetActive(false);Destroy(zombie.gameObject);}
        zombies.Clear();
        foreach(var pickup in FindObjectsByType<ElderboomSpecialPickup>(FindObjectsSortMode.None))Destroy(pickup.gameObject);
        player.ResetSession(); totalScore=0; ResetStats();
        Array.Clear(abilityReady,0,abilityReady.Length); Array.Clear(abilityCooling,0,abilityCooling.Length);
        selectedOperator=operatorId;selectedDifficulty=difficulty;StartCoroutine(StartGame());
        ElderboomEvents.RaiseSessionStarted();
    }

    public void RegisterKill(ElderboomZombie zombie)
    {
        if (zombie == null || zombie.Registered) return;
        zombie.Registered = true;
        int points = DoublePoints ? 200 : 100;
        // LAN: a client's kill is rewarded on that client (ElderboomLan sends it back).
        if (ElderboomLan.RemoteShooter == 0) { credits += points; totalScore += points; kills++; }
        popups.Add((zombie.transform.position + Vector3.up * 1.9f, points, Time.time));
        ElderboomEvents.RaiseZombieKilled(zombie, points);
        if (ElderboomSpecialPickup.TrySpawn(zombie.transform.position) == null) ElderboomPowerUp.TrySpawn(zombie.transform.position);
        if (zombie.CountsForWave) waveRemaining = Mathf.Max(0, waveRemaining - 1);
    }

    public bool TrySpend(int amount)
    {
        if (amount<0 || credits < amount) return false;
        credits -= amount;
        return true;
    }
    internal void RefundAltar(int amount) { if (amount > 0) credits += amount; }

    void ConfigureLoadout()
    {
        var op = Operator;
        player.SelectPortedViewmodel(op.id);
        player.ConfigurePortedLoadout(op.id, op.weaponLabel, op.capacity, op.reserve, op.damage, op.fireInterval);
        if (op.tacticalArms)
        {
            var arms = player.GetComponent<ElderboomTacticalArms>() ?? player.gameObject.AddComponent<ElderboomTacticalArms>();
            var camera = player.GetComponentInChildren<Camera>(true);
            var pivot = player.ViewmodelParent.Find("Tactical_Viewmodel_Pivot");
            if (pivot == null && camera != null) pivot = camera.transform.Find("Tactical_Viewmodel_Pivot");
            arms.BindStriker(pivot);
        }
        var arsenal=player.GetComponent<ElderboomArsenal>();if(arsenal==null)arsenal=player.gameObject.AddComponent<ElderboomArsenal>();arsenal.Initialize(op.id);
    }


}
