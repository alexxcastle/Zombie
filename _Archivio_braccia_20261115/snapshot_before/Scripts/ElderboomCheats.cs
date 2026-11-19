using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// In-game cheat menu for testing: sections with switches, drop-downs and sliders. Open/close with F1 (fn+F1 on Mac), ` or \ ; Esc closes.
// While it is open, the game receives no input (ElderboomInput.AcceptsInput) and Esc does not open the pause menu.
// Local only: credits, weapons and god mode apply to this player; in LAN the host's commands drive waves and zombies.
[DefaultExecutionOrder(1000)] // after ElderboomPauseMenu, so Esc closes the console without opening the pause menu
public sealed class ElderboomCheats : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static ElderboomCheats instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ElderboomCheats"); DontDestroyOnLoad(go);
        instance = go.AddComponent<ElderboomCheats>();
    }

    readonly List<string> lines = new List<string>();
    readonly SortedDictionary<string, (string usage, string help, Action<string[]> run)> commands = new SortedDictionary<string, (string, string, Action<string[]>)>(StringComparer.OrdinalIgnoreCase);
    Vector2 scroll; float fps = 60f;
    bool god, infAmmo, noTarget, frozen, noCooldown;
    float tickAt;

    static ElderboomGame Game => ElderboomGame.Active;
    static HDRPSlicePlayer Player => Game != null && Game.Player != null ? Game.Player : FindAnyObjectByType<HDRPSlicePlayer>();
    static ElderboomArsenal Arsenal => Player != null ? Player.GetComponent<ElderboomArsenal>() : null;

    void Awake()
    {
        Register();
        
    }

    void OnDestroy() { if (instance == this) { instance = null; IsOpen = false; } }

    // ------------------------------------------------------------------ open / close
    void Update()
    {
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(.0001f, Time.unscaledDeltaTime), .05f);
        if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Backslash)) Toggle();
        else if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Toggle();
        Tick();
    }

    void Toggle()
    {
        IsOpen = !IsOpen;
        ElderboomInput.ReleaseAll();
        var p = Player;
        if (IsOpen)
        {
            openDrop = -1; RefreshLists();
            if (p != null) p.SetCursor(false); else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        else if (p != null && Game != null && Game.IsPlaying && !p.SettingsOpen) p.SetCursor(true);
    }

    // ------------------------------------------------------------------ persistent toggles
    void Tick()
    {
        var p = Player; var g = Game;
        if (p == null) return;
        if (god)
        {
            HDRPSlicePlayer.DevInvulnerable = true;
            if (p.Downed) p.Revive();
            if (p.Health < 100f) p.Heal(100f);
        }
        if (infAmmo)
        {
            Arsenal?.RefillAmmo(); p.RefillAmmo();
            var mag = GetField(p, "magazineSize"); var ammo = GetField(p, "ammo");
            if (mag is int m && ammo is int a && a < m && !p.Reloading) SetField(p, "ammo", m);
        }
        if (noCooldown && g != null && GetField(g, "abilityReady") is float[] ready) for (int i = 0; i < ready.Length; i++) ready[i] = 0f;
        if (g != null && Time.unscaledTime >= tickAt)
        {
            tickAt = Time.unscaledTime + .5f;
            if (noTarget) SetField(g, "ignoredUntil", Time.time + 2f);
            foreach (var z in g.Zombies)
            {
                if (z == null || !z.IsAlive) continue;
                if (noTarget) z.IgnorePlayerFor(1.5f);
                if (frozen) z.FreezeFor(1.5f);
            }
        }
    }

    // ------------------------------------------------------------------ commands
    void Add(string name, string usage, string help, Action<string[]> run) => commands[name] = (usage, help, run);

    void Register()
    {
        Add("help", "help", "questa lista", a =>
        {
            foreach (var c in commands) Print("  " + c.Value.usage.PadRight(30) + c.Value.help);
        });
        Add("clear", "clear", "pulisce la console", a => lines.Clear());

        // Player
        Add("god", "god", "invulnerabile (on/off)", a => { god = !god; HDRPSlicePlayer.DevInvulnerable = god; Print("god " + OnOff(god)); });
        Add("heal", "heal", "vita piena", a => { Player?.Heal(100f); Print("vita piena"); });
        Add("revive", "revive", "rialza se sei a terra", a => { Player?.Revive(); Print("rialzato"); });
        Add("kill", "kill", "uccide il giocatore (test game over)", a => { HDRPSlicePlayer.DevInvulnerable = god = false; Player?.TakeDamage(5000f); Print("morto"); });
        Add("speed", "speed <+m/s>", "aumenta la velocità di corsa (es. speed 3)", a => { float v = ArgF(a, 0, 2f); Player?.AddSpeedPerk(v); Print("velocità +" + v); });
        Add("syringes", "syringes", "3 siringhe", a => { for (int i = 0; i < 3; i++) Player?.AddSyringe(); Print("siringhe 3"); });
        Add("pos", "pos", "posizione attuale", a => { if (Player) Print(Player.transform.position.ToString("F1")); });
        Add("tp", "tp <nome|x z|list>", "teletrasporto (stazione, cancello, chiesa, ponte...)", Teleport);

        // Economy & weapons
        Add("credits", "credits <n>", "aggiunge crediti (default 10000)", a => { int n = ArgI(a, 0, 10000); Game?.RefundAltar(n); Print("+" + n + " crediti (totale " + (Game != null ? Game.Credits : 0) + ")"); });
        Add("weapons", "weapons", "elenco armi disponibili", a =>
        {
            foreach (var grp in ElderboomWeapons.All.GroupBy(w => w.category)) Print("  " + grp.Key.ToUpperInvariant() + ": " + string.Join(", ", grp.Select(w => w.id)));
            Print("  speciali (give o temp): " + string.Join(", ", ElderboomSpecialPickup.DropIds));
        });
        Add("give", "give <id>", "dà un'arma (vedi weapons)", a =>
        {
            var def = a.Length > 0 ? ElderboomWeapons.Find(a[0]) : null;
            if (def == null) { Print("arma sconosciuta, scrivi weapons"); return; }
            if (Array.IndexOf(ElderboomSpecialPickup.DropIds, def.id) >= 0) { Print(Arsenal != null && Arsenal.ReceiveTemporary(def, 120f) ? "arma speciale " + def.id + " per 120 s" : "non posso darla adesso"); return; }
            Print(Arsenal != null && Arsenal.Add(def) ? "presa " + def.id : "non posso (già posseduta, in ricarica o slot pieni: prova slot3)");
        });
        Add("temp", "temp <id> [secondi]", "arma temporanea (minigun, flame, grenade...)", a =>
        {
            var def = a.Length > 0 ? ElderboomWeapons.Find(a[0]) : null;
            if (def == null) { Print("arma sconosciuta"); return; }
            Print(Arsenal != null && Arsenal.ReceiveTemporary(def, ArgF(a, 1, 120f)) ? "temporanea " + def.id : "non posso darla adesso");
        });
        Add("equip", "equip <id>", "impugna un'arma già posseduta", a => Print(a.Length > 0 && Arsenal != null && Arsenal.Equip(a[0]) ? "impugnata" : "non posseduta"));
        Add("slot3", "slot3", "sblocca il terzo slot", a => Print(Arsenal != null && Arsenal.UnlockThirdSlot() ? "terzo slot sbloccato" : "già sbloccato"));
        Add("ammo", "ammo", "munizioni piene", a => { Arsenal?.RefillAmmo(); Player?.RefillAmmo(); if (GetField(Player, "magazineSize") is int m) SetField(Player, "ammo", m); Print("munizioni piene"); });
        Add("infammo", "infammo", "munizioni infinite (on/off)", a => { infAmmo = !infAmmo; Print("munizioni infinite " + OnOff(infAmmo)); });
        Add("powerup", "powerup <max_ammo|insta_kill|double_points>", "attiva un power-up", a => { string k = a.Length > 0 ? a[0] : "insta_kill"; Game?.ActivatePowerUp(k); Print("power-up " + k); });
        Add("nocd", "nocd", "abilità senza ricarica (on/off)", a => { noCooldown = !noCooldown; Print("abilità senza ricarica " + OnOff(noCooldown)); });

        // Zombies & waves
        Add("spawn", "spawn <n> [walker|runner|brute]", "zombie davanti a te (non contano per l'ondata)", Spawn);
        Add("killall", "killall", "uccide tutti gli zombie", a => Print("uccisi " + KillAll()));
        Add("skipwave", "skipwave", "chiude l'ondata in corso", a => { int n = KillAll(); if (Game) SetField(Game, "waveRemaining", 0); Print("ondata chiusa (" + n + " zombie)"); });
        Add("wave", "wave <n>", "salta all'ondata n (dopo la pausa)", a =>
        {
            int n = Mathf.Max(1, ArgI(a, 0, 5));
            if (Game == null) return;
            SetField(Game, "wave", n - 1); KillAll(); SetField(Game, "waveRemaining", 0);
            Print("prossima ondata: " + n);
        });
        Add("notarget", "notarget", "gli zombie ti ignorano (on/off)", a => { noTarget = !noTarget; if (!noTarget && Game) SetField(Game, "ignoredUntil", 0f); Print("notarget " + OnOff(noTarget)); });
        Add("freeze", "freeze", "zombie immobili (on/off)", a => { frozen = !frozen; Print("zombie congelati " + OnOff(frozen)); });
        Add("gates", "gates", "apre tutti i cancelli e le zone", a =>
        {
            int n = 0;
            foreach (var gt in FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None)) if (!gt.Opened) { gt.NetOpen(); ElderboomLan.GateOpened(gt); n++; }
            Print("cancelli aperti: " + n);
        });

        // World
        Add("day", "day", "mezzogiorno", a => SetTime(12.5f));
        Add("dusk", "dusk", "tramonto", a => SetTime(17.4f));
        Add("dawn", "dawn", "alba", a => SetTime(6.7f));
        Add("night", "night", "notte (luce originale)", a => SetTime(0f));
        Add("time", "time <0-24>", "ora del giorno (6-18 sole, resto notte)", a => SetTime(ArgF(a, 0, 12f)));
        Add("timescale", "timescale <x>", "velocità del tempo (0.2 rallenta, 1 normale)", a => { Time.timeScale = Mathf.Clamp(ArgF(a, 0, 1f), 0f, 5f); Print("timescale " + Time.timeScale); });
        Add("status", "status", "stato partita e cheat attivi", a =>
        {
            var g = Game; var p = Player;
            Print("ondata " + (g ? g.Wave : 0) + "  restanti " + (g ? g.Remaining : 0) + "  zombie vivi " + (g ? g.Zombies.Count(z => z != null && z.IsAlive) : 0) + "  crediti " + (g ? g.Credits : 0));
            if (p) Print("vita " + p.Health.ToString("F0") + "  arma " + p.WeaponDisplayName + "  " + p.Ammo + "/" + p.ReserveAmmo);
            Print("attivi: " + Flags());
        });
    }

    void Run(string text)
    {
        text = text.Trim(); if (text.Length == 0) return;
        var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (!commands.TryGetValue(parts[0], out var cmd)) { Print("comando sconosciuto: " + parts[0] + " (help)"); return; }
        try { cmd.run(parts.Skip(1).ToArray()); }
        catch (Exception e) { Print("errore: " + e.Message); Debug.LogException(e); }
    }

    // ------------------------------------------------------------------ helpers for commands
    int KillAll()
    {
        int n = 0; var g = Game; if (g == null) return 0;
        foreach (var z in g.Zombies.ToArray()) if (z != null && z.IsAlive) { z.TakeDamage(1e6f, z.transform.position + Vector3.up); n++; }
        return n;
    }

    void Spawn(string[] a)
    {
        var g = Game; var p = Player;
        if (g == null || p == null || !g.IsPlaying) { Print("solo durante una partita"); return; }
        int n = Mathf.Clamp(ArgI(a, 0, 5), 1, 60); string variant = a.Length > 1 ? a[1].ToLowerInvariant() : null;
        var marker = new GameObject("CheatSpawn").transform; int made = 0;
        for (int i = 0; i < n; i++)
        {
            var at = p.transform.position + p.transform.forward * 12f + new Vector3(UnityEngine.Random.Range(-4f, 4f), 0, UnityEngine.Random.Range(-4f, 4f));
            if (!NavMesh.SamplePosition(at, out var hit, 8f, NavMesh.AllAreas)) continue;
            marker.position = hit.position; int before = g.Zombies.Count;
            g.SpawnScripted(marker);
            if (g.Zombies.Count > before) { made++; if (variant != null) g.Zombies[g.Zombies.Count - 1].SetVariant(variant); }
        }
        Destroy(marker.gameObject);
        Print("zombie creati " + made + (variant != null ? " (" + variant + ")" : ""));
    }

    void Teleport(string[] a)
    {
        var p = Player; if (p == null) return;
        var targets = new List<(string name, Transform t)>();
        foreach (var s in FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None)) targets.Add((s.label ?? s.name, s.transform));
        foreach (var gt in FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None)) targets.Add((gt.name.Replace("Access_", "").Replace("Passage_", ""), gt.transform));
        foreach (var n in new[] { "FallenCity_Church_HumanScale|chiesa", "Church_Altar_Weapon_Upgrader|altare", "SouthCreek_WideStoneBridge|ponte", "Horse_Salvage_Objective|cavallo", "SouthCreek_Timber_Inn|locanda", "Woodland_Stone_Amphitheatre|anfiteatro" })
        {
            var parts = n.Split('|'); var go = GameObject.Find(parts[0]); if (go != null) targets.Add((parts[1], go.transform));
        }
        if (a.Length == 0 || a[0] == "list") { Print("  " + string.Join(", ", targets.Select(t => t.name.ToLowerInvariant().Replace(' ', '_')))); return; }
        Vector3 dest;
        if (a.Length >= 2 && float.TryParse(a[0], out float x) && float.TryParse(a[1], out float z)) dest = new Vector3(x, 2f, z);
        else
        {
            string q = string.Join(" ", a).ToLowerInvariant().Replace('_', ' ');
            var match = targets.FirstOrDefault(t => t.name.ToLowerInvariant().Replace('_', ' ').Contains(q));
            if (match.t == null) { Print("destinazione sconosciuta (tp list)"); return; }
            dest = match.t.position + match.t.forward * 2.5f;
        }
        if (NavMesh.SamplePosition(dest, out var hit, 30f, NavMesh.AllAreas)) dest = hit.position;
        var cc = p.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        p.transform.position = dest + Vector3.up * .15f;
        if (cc) cc.enabled = true;
        Physics.SyncTransforms();
        Print("teletrasportato a " + dest.ToString("F1"));
    }

    // ------------------------------------------------------------------ time of day
    Volume dayVolume; VisualEnvironment env; GradientSky sky; Exposure exposure; Fog fog;
    Light sun; Color sunColor; float sunIntensity; Quaternion sunRotation; bool sunSaved;

    void SetTime(float h)
    {
        h = Mathf.Repeat(h, 24f);
        float u = (h - 6f) / 12f;
        float elev = u > 0f && u < 1f ? 75f * Mathf.Sin(Mathf.PI * u) : -1f;
        if (elev < 3f) { Night(); Print("ora " + h.ToString("F1") + " // notte"); return; }
        if (!EnsureDay()) { Print("nessuna luce direzionale in scena"); return; }
        float t = Mathf.Clamp01(elev / 45f);
        dayVolume.enabled = true;
        sun.transform.rotation = Quaternion.Euler(elev, Mathf.Lerp(80f, 280f, u), 0f);
        sun.color = Color.Lerp(new Color(1f, .55f, .3f), new Color(1f, .96f, .9f), t);
        sun.intensity = Mathf.Lerp(2500f, 90000f, t * t);
        float ev = Mathf.Lerp(10f, 13.4f, Mathf.Sqrt(t));
        exposure.fixedExposure.Override(ev);
        sky.exposure.Override(ev - .6f);
        sky.top.Override(Color.Lerp(new Color(.2f, .2f, .42f), new Color(.2f, .42f, .85f), t));
        sky.middle.Override(Color.Lerp(new Color(1f, .5f, .3f), new Color(.65f, .78f, .95f), t));
        sky.bottom.Override(Color.Lerp(new Color(.35f, .28f, .28f), new Color(.45f, .45f, .44f), t));
        fog.color.Override(Color.Lerp(new Color(.8f, .55f, .42f), new Color(.72f, .8f, .9f), t));
        fog.meanFreePath.Override(Mathf.Lerp(260f, 600f, t));
        Print("ora " + h.ToString("F1") + " // sole a " + elev.ToString("F0") + "°");
    }

    bool EnsureDay()
    {
        if (sun == null)
        {
            sun = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional && l.isActiveAndEnabled).OrderByDescending(l => l.intensity).FirstOrDefault();
            if (sun == null) return false;
            sunColor = sun.color; sunIntensity = sun.intensity; sunRotation = sun.transform.rotation; sunSaved = true;
        }
        if (dayVolume == null)
        {
            dayVolume = gameObject.AddComponent<Volume>(); dayVolume.isGlobal = true; dayVolume.priority = 2000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); dayVolume.sharedProfile = profile;
            env = profile.Add<VisualEnvironment>(true);
            env.skyType.Override(SkySettings.GetUniqueID<GradientSky>());
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            sky = profile.Add<GradientSky>(true); sky.gradientDiffusion.Override(1.2f);
            exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.Fixed);
            fog = profile.Add<Fog>(true); fog.enabled.Override(true);
        }
        return true;
    }

    void Night()
    {
        if (dayVolume != null) dayVolume.enabled = false;
        if (sun != null && sunSaved) { sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = sunRotation; }
    }

    // ------------------------------------------------------------------ UI: mini menu with sections, switches and drop-downs
    int openDrop = -1; readonly Dictionary<int, Vector2> dropScroll = new Dictionary<int, Vector2>();
    int weaponIndex, powerIndex, variantIndex, tpIndex, spawnCount = 5, waveTarget = 5;
    float hour = 12f, timeScaleUi = 1f; Vector2 panelScroll;
    readonly bool[] folded = new bool[8];
    string[] weaponIds = new string[0], tpNames = new string[0];
    static readonly string[] Powerups = { "insta_kill", "double_points", "max_ammo" };
    static readonly string[] PowerupLabels = { "Insta-kill", "Punti doppi", "Munizioni max" };
    static readonly string[] Variants = { "walker", "runner", "brute" };
    static readonly string[] VariantLabels = { "Normale", "Corridore", "Bruto" };
    GUIStyle sHead, sLabel, sBtn, sItem, sItemSel, sToggle, sLog, sTitle; float uiScale;

    void RefreshLists()
    {
        weaponIds = ElderboomWeapons.All.Where(w => w.id != "knife").OrderBy(w => w.category).ThenBy(w => w.id).Select(w => w.id).ToArray();
        var names = new List<string>();
        foreach (var st in FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None)) names.Add((st.label ?? st.name).ToLowerInvariant().Replace(' ', '_'));
        foreach (var gt in FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None)) names.Add(gt.name.Replace("Access_", "").Replace("Passage_", "").ToLowerInvariant());
        names.AddRange(new[] { "chiesa", "altare", "ponte", "cavallo", "locanda", "anfiteatro" });
        tpNames = names.Distinct().ToArray();
        weaponIndex = Mathf.Clamp(weaponIndex, 0, Mathf.Max(0, weaponIds.Length - 1)); tpIndex = Mathf.Clamp(tpIndex, 0, Mathf.Max(0, tpNames.Length - 1));
    }

    void BuildStyles(float s)
    {
        if (sHead != null && Mathf.Approximately(uiScale, s)) return;
        uiScale = s;
        int fs = Mathf.RoundToInt(15 * s);
        sTitle = Style(17 * s, new Color(1f, .78f, .3f)); sTitle.fontStyle = FontStyle.Bold;
        sHead = new GUIStyle(GUI.skin.button) { fontSize = fs, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fixedHeight = 30 * s };
        sHead.normal.textColor = sHead.hover.textColor = new Color(1f, .82f, .45f);
        sLabel = Style(fs, new Color(.85f, .87f, .9f));
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = fs, fixedHeight = 28 * s };
        sItem = new GUIStyle(GUI.skin.button) { fontSize = fs, alignment = TextAnchor.MiddleLeft, fixedHeight = 25 * s };
        sItemSel = new GUIStyle(sItem); sItemSel.normal.textColor = sItemSel.hover.textColor = new Color(1f, .8f, .35f);
        sToggle = new GUIStyle(GUI.skin.toggle) { fontSize = fs, fixedHeight = 26 * s }; sToggle.normal.textColor = sToggle.onNormal.textColor = sToggle.hover.textColor = sToggle.onHover.textColor = new Color(.9f, .92f, .95f);
        sLog = Style(13 * s, new Color(.7f, .75f, .8f));
    }

    bool Section(int i, string title)
    {
        if (GUILayout.Button((folded[i] ? "+  " : "-  ") + title, sHead)) folded[i] = !folded[i];
        return !folded[i];
    }

    int Drop(int id, string label, string[] options, int selected)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, sLabel, GUILayout.Width(95 * uiScale));
        string cur = options.Length > 0 ? options[Mathf.Clamp(selected, 0, options.Length - 1)] : "-";
        if (GUILayout.Button(cur + (openDrop == id ? "   ^" : "   v"), sItem)) openDrop = openDrop == id ? -1 : id;
        GUILayout.EndHorizontal();
        if (openDrop == id)
        {
            dropScroll.TryGetValue(id, out var sc);
            sc = GUILayout.BeginScrollView(sc, GUILayout.Height(Mathf.Min(options.Length * 27f * uiScale + 6, 230f * uiScale)));
            for (int i = 0; i < options.Length; i++)
                if (GUILayout.Button(options[i], i == selected ? sItemSel : sItem)) { selected = i; openDrop = -1; }
            GUILayout.EndScrollView();
            dropScroll[id] = sc;
        }
        return selected;
    }

    bool Switch(bool value, string label, string command)
    {
        bool next = GUILayout.Toggle(value, "  " + label, sToggle);
        if (next != value) Run(command);
        return next;
    }

    bool Btn(string label) => GUILayout.Button(label, sBtn);

    int Stepper(string label, int value, int min, int max, int step)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, sLabel, GUILayout.Width(95 * uiScale));
        if (GUILayout.Button("-", sBtn, GUILayout.Width(34 * uiScale))) value = Mathf.Max(min, value - step);
        GUILayout.Label(value.ToString(), sLabel, GUILayout.Width(48 * uiScale));
        if (GUILayout.Button("+", sBtn, GUILayout.Width(34 * uiScale))) value = Mathf.Min(max, value + step);
        GUILayout.EndHorizontal();
        return value;
    }

    void OnGUI()
    {
        GUI.depth = -1000;
        float s = Mathf.Max(.75f, Screen.height / 1080f);
        if (!IsOpen)
        {
            string f = Flags();
            if (f != "nessuno") { var st = Style(14 * s, new Color(1f, .8f, .3f)); GUI.Label(new Rect(10 * s, Screen.height - 30 * s, 900 * s, 24 * s), "CHEAT: " + f, st); }
            return;
        }
        BuildStyles(s);
        if (weaponIds.Length == 0 || tpNames.Length == 0) RefreshLists();

        float w = 430 * s, pad = 10 * s;
        var area = new Rect(pad, pad, w, Screen.height - 2 * pad);
        GUI.color = new Color(0f, 0f, 0f, .86f); GUI.DrawTexture(area, Texture2D.whiteTexture); GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, area.height - 2 * pad));
        GUILayout.Label("MENU CHEAT", sTitle);
        var g = Game; var p = Player;
        GUILayout.Label("Ondata " + (g ? g.Wave : 0) + "  ·  zombie vivi " + (g ? g.Zombies.Count(z => z != null && z.IsAlive) : 0) + "  ·  crediti " + (g ? g.Credits : 0) + "  ·  " + fps.ToString("F0") + " FPS", sLabel);
        if (p) GUILayout.Label("Vita " + p.Health.ToString("F0") + "  ·  " + p.WeaponDisplayName + " " + p.Ammo + "/" + p.ReserveAmmo, sLabel);
        GUILayout.Space(6 * s);
        panelScroll = GUILayout.BeginScrollView(panelScroll);

        if (Section(0, "GIOCATORE"))
        {
            god = Switch(god, "Invulnerabile", "god");
            infAmmo = Switch(infAmmo, "Munizioni infinite", "infammo");
            noCooldown = Switch(noCooldown, "Abilità senza ricarica", "nocd");
            GUILayout.BeginHorizontal(); if (Btn("Cura")) Run("heal"); if (Btn("Rialza")) Run("revive"); if (Btn("3 siringhe")) Run("syringes"); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if (Btn("Velocità +2")) Run("speed 2"); if (Btn("Muori (test)")) Run("kill"); GUILayout.EndHorizontal();
        }
        if (Section(1, "ARMI E CREDITI"))
        {
            weaponIndex = Drop(1, "Arma", weaponIds, weaponIndex);
            string wid = weaponIds.Length > 0 ? weaponIds[weaponIndex] : "";
            GUILayout.BeginHorizontal(); if (Btn("Dai arma")) Run("give " + wid); if (Btn("Impugna")) Run("equip " + wid); if (Btn("Temporanea")) Run("temp " + wid + " 120"); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if (Btn("Munizioni piene")) Run("ammo"); if (Btn("Terzo slot")) Run("slot3"); GUILayout.EndHorizontal();
            powerIndex = Drop(2, "Power-up", PowerupLabels, powerIndex);
            if (Btn("Attiva power-up")) Run("powerup " + Powerups[powerIndex]);
            GUILayout.BeginHorizontal(); if (Btn("+1.000 cr")) Run("credits 1000"); if (Btn("+10.000 cr")) Run("credits 10000"); if (Btn("+100.000 cr")) Run("credits 100000"); GUILayout.EndHorizontal();
        }
        if (Section(2, "ZOMBIE E ONDATE"))
        {
            variantIndex = Drop(3, "Tipo", VariantLabels, variantIndex);
            spawnCount = Stepper("Quantità", spawnCount, 1, 60, spawnCount >= 10 ? 5 : 1);
            if (Btn("Crea zombie davanti a me")) Run("spawn " + spawnCount + " " + Variants[variantIndex]);
            noTarget = Switch(noTarget, "Gli zombie mi ignorano", "notarget");
            frozen = Switch(frozen, "Zombie congelati", "freeze");
            GUILayout.BeginHorizontal(); if (Btn("Uccidi tutti")) Run("killall"); if (Btn("Chiudi ondata")) Run("skipwave"); GUILayout.EndHorizontal();
            waveTarget = Stepper("Ondata", waveTarget, 1, 100, 1);
            if (Btn("Vai all'ondata " + waveTarget)) Run("wave " + waveTarget);
            if (Btn("Apri tutti i cancelli")) Run("gates");
        }
        if (Section(3, "GIORNO / NOTTE E TEMPO"))
        {
            GUILayout.BeginHorizontal(); if (Btn("Alba")) { hour = 6.7f; Run("dawn"); } if (Btn("Giorno")) { hour = 12.5f; Run("day"); } if (Btn("Tramonto")) { hour = 17.4f; Run("dusk"); } if (Btn("Notte")) { hour = 0f; Run("night"); } GUILayout.EndHorizontal();
            GUILayout.Label("Ora: " + hour.ToString("F1"), sLabel);
            float nh = GUILayout.HorizontalSlider(hour, 0f, 24f); GUILayout.Space(8 * s);
            if (Mathf.Abs(nh - hour) > .05f) { hour = nh; SetTimeQuiet(hour); }
            GUILayout.Label("Velocità del gioco: " + timeScaleUi.ToString("F2") + "x", sLabel);
            float ts = GUILayout.HorizontalSlider(timeScaleUi, .1f, 3f); GUILayout.Space(8 * s);
            if (Mathf.Abs(ts - timeScaleUi) > .01f) { timeScaleUi = ts; Time.timeScale = ts; }
            if (Btn("Velocità normale")) { timeScaleUi = 1f; Time.timeScale = 1f; }
        }
        if (Section(4, "TELETRASPORTO"))
        {
            tpIndex = Drop(5, "Dove", tpNames, tpIndex);
            GUILayout.BeginHorizontal(); if (Btn("Vai")) Run("tp " + (tpNames.Length > 0 ? tpNames[tpIndex] : "list")); if (Btn("Aggiorna lista")) RefreshLists(); GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6 * s);
        int from = Mathf.Max(0, lines.Count - 4);
        for (int i = from; i < lines.Count; i++) GUILayout.Label(lines[i], sLog);
        GUILayout.Label("F1 · ` · \\ · Esc per chiudere", sLog);
        GUILayout.EndArea();
    }

    void SetTimeQuiet(float h) { int n = lines.Count; SetTime(h); if (lines.Count > n) lines.RemoveRange(n, lines.Count - n); }

    static GUIStyle Style(float size, Color c) { var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size), richText = false }; st.normal.textColor = c; return st; }

    void Print(string text)
    {
        foreach (var l in text.Split('\n')) lines.Add(l);
        while (lines.Count > 400) lines.RemoveAt(0);
        scroll.y = float.MaxValue;
    }

    string Flags()
    {
        var f = new List<string>();
        if (god) f.Add("GOD"); if (infAmmo) f.Add("MUNIZIONI INF"); if (noTarget) f.Add("NOTARGET"); if (frozen) f.Add("FREEZE"); if (noCooldown) f.Add("NO CD");
        if (Mathf.Abs(Time.timeScale - 1f) > .01f) f.Add("TIMESCALE " + Time.timeScale.ToString("F2"));
        if (dayVolume != null && dayVolume.enabled) f.Add("LUCE DIURNA");
        return f.Count == 0 ? "nessuno" : string.Join(" · ", f);
    }

    static string OnOff(bool v) => v ? "ON" : "OFF";
    static int ArgI(string[] a, int i, int def) => a.Length > i && int.TryParse(a[i], out int v) ? v : def;
    static float ArgF(string[] a, int i, float def) => a.Length > i && float.TryParse(a[i].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;

    static FieldInfo Field(Type t, string name)
    {
        for (; t != null; t = t.BaseType) { var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); if (f != null) return f; }
        return null;
    }
    static object GetField(object o, string name) => o == null ? null : Field(o.GetType(), name)?.GetValue(o);
    static void SetField(object o, string name, object v) { if (o != null) Field(o.GetType(), name)?.SetValue(o, v); }
}
