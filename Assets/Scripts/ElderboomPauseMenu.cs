using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Settings (load, save, apply) and the in-game pause menu, split out of
// HDRPSlicePlayer. Runs before the player so Escape is handled first.
// The menu and the performance overlay are UI Toolkit (previously IMGUI).
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class ElderboomPauseMenu : MonoBehaviour
{
    const string SensitivityKey = "hdrp.settings.mouse_sensitivity";
    const string BrightnessKey = "hdrp.settings.brightness";
    const string FieldOfViewKey = "hdrp.settings.horizontal_fov_v2"; // horizontal degrees (the old key held a vertical value)
    const string MasterVolumeKey = "hdrp.settings.master_volume";
    const string FullscreenKey = ElderboomDisplaySettings.FullscreenKey;
    const string MusicKey = "hdrp.settings.music_volume";
    const string EffectsKey = "hdrp.settings.effects_volume";
    const string VsyncKey = "hdrp.settings.vsync.v2"; // new key: uncapped by default
    const string ResolutionKey = ElderboomDisplaySettings.ResolutionKey;
    const string QualityKey = ElderboomDisplaySettings.QualityKey;
    const string HudScaleKey = "hdrp.settings.hud_scale";
    const string PerfKey = "hdrp.settings.performance";
    const float DefaultBrightness = 1f;
    public const float DefaultHorizontalFov = 80f; // horizontal, the usual shooter default. The weapon is framed at this angle.
    const float DefaultMasterVolume = 1f;
    string pausePage = "pause";
    string pauseStatus = "";
    string rebindAction;
    string padRebindAction;
    bool padWaitRelease, padShown;
    ElderboomGamepad.Layout padLayout = ElderboomGamepad.Layout.Shooter;
    readonly Dictionary<string, Button> padButtons = new Dictionary<string, Button>();
    bool rebindWaitRelease;
    bool bindingsDirty;
    float brightness = DefaultBrightness;
    float fieldOfView = DefaultHorizontalFov;
    float masterVolume = DefaultMasterVolume;
    float musicVolume = 0.8f;
    float effectsVolume = 0.85f;
    bool fullscreen = true;
    bool vsync = false;
    int resolutionIndex = 1;
    int qualityIndex = 1;
    float hudScale = 1f;
    bool showPerformance;
    static readonly Vector2Int[] Resolutions = ElderboomDisplaySettings.Resolutions;
    static readonly Color PauseBorder = new Color(0.72f, 0.045f, 0.085f);
    Volume brightnessVolume;
    Exposure brightnessExposure;
    float baseExposure;
    HDRPSlicePlayer player;
    int zombieCount;
    float zombieCountAt;
    float sensitivity = 1.8f;
    bool isOpen;
    public bool IsOpen => isOpen;
    public float Sensitivity => sensitivity;
    // The setting is the horizontal angle players expect; the camera takes the vertical one for the screen's aspect.
    public float FieldOfView => VerticalFov(fieldOfView);
    public static float VerticalFov(float horizontal)
    {
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        return 2f * Mathf.Atan(Mathf.Tan(horizontal * .5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
    }

    public void Initialize(HDRPSlicePlayer owner, float defaultSensitivity)
    {
        player = owner;
        sensitivity = defaultSensitivity;
        LoadSettings();
        ConfigureBrightness();
        ApplySettings(false);
    }

    // Used by HDRPSlicePlayer.ResetSession: closes without saving or touching the cursor.
    public void ForceClose() { isOpen = false; rebindAction = null; padRebindAction = null; }

    void Update()
    {
        // As before the split: no pause while the player is disabled (operator menu, loading).
        bool back = !ElderboomCheats.IsOpen && (Input.GetKeyDown(KeyCode.Escape) || ElderboomGamepad.PauseDown || (isOpen && ElderboomGamepad.BackDown));
        if (!string.IsNullOrEmpty(padRebindAction))
        {   // waiting for a pad button: B can be bound, so only Esc or Start cancel
            if (Input.GetKeyDown(KeyCode.Escape) || ElderboomGamepad.PauseDown) { padRebindAction = null; bindingsDirty = true; pauseStatus = ""; }
            return;
        }
        if (player == null || (!player.enabled && !isOpen) || !back) return;
        if (!string.IsNullOrEmpty(rebindAction)) { rebindAction = null; bindingsDirty = true; pauseStatus = "Press a keyboard key or mouse button. ESC cancels."; return; }
        if (!isOpen) { if (player.IsDead) return; pausePage = "pause"; SetSettingsOpen(true); }
        else if (pausePage == "pause") SetSettingsOpen(false);
        else { pausePage = "pause"; ElderboomUi.Back(); }
    }

    void LoadSettings()
    {
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, sensitivity), .25f, 6f);
        brightness = Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness), .5f, 1.5f);
        fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(FieldOfViewKey, DefaultHorizontalFov), 65f, 110f);
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.8f));
        effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 0.85f));
        fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
        vsync = PlayerPrefs.GetInt(VsyncKey, 0) == 1;
        resolutionIndex = ElderboomDisplaySettings.Resolution;
        qualityIndex = ElderboomDisplaySettings.Quality;
        hudScale = Mathf.Clamp(PlayerPrefs.GetFloat(HudScaleKey, 1f), 0.75f, 1.25f);
        showPerformance = PlayerPrefs.GetInt(PerfKey, 0) == 1;
    }

    void SaveSettings()
    {
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        PlayerPrefs.SetFloat(BrightnessKey, brightness);
        PlayerPrefs.SetFloat(FieldOfViewKey, fieldOfView);
        PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
        PlayerPrefs.SetFloat(MusicKey, musicVolume);
        PlayerPrefs.SetFloat(EffectsKey, effectsVolume);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(VsyncKey, vsync ? 1 : 0);
        PlayerPrefs.SetInt(ResolutionKey, resolutionIndex);
        PlayerPrefs.SetInt(QualityKey, qualityIndex);
        PlayerPrefs.SetFloat(HudScaleKey, hudScale);
        PlayerPrefs.SetInt(PerfKey, showPerformance ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ConfigureBrightness()
    {
        var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var candidate in volumes)
            if (candidate.isGlobal && (brightnessVolume == null || candidate.priority > brightnessVolume.priority))
                brightnessVolume = candidate;
        if (brightnessVolume == null || brightnessVolume.profile == null) return;
        if (brightnessVolume.profile.TryGet(out Exposure exposure))
        {
            brightnessExposure = exposure;
            baseExposure = exposure.fixedExposure.value;
        }
    }

    void ApplySettings(bool save)
    {
        if (player.ViewCamera != null) player.ViewCamera.fieldOfView = FieldOfView;
        AudioListener.volume = masterVolume;
        ElderboomAudio.Effects = effectsVolume;
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        Application.targetFrameRate = vsync ? -1 : ElderboomGraphicsOptions.TargetFps; // Settings > Video > Frame rate limit
        if (brightnessExposure != null)
            brightnessExposure.fixedExposure.value = baseExposure - Mathf.Log(Mathf.Max(.01f, brightness), 2f);
        if (!Application.isEditor)
        {
            Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.fullScreen = fullscreen;
        }
        if (save) SaveSettings();
    }

    void SetSettingsOpen(bool open)
    {
        ElderboomInput.ReleaseAll();
        isOpen = open;
        // In a multiplayer game the match keeps running behind the menu (the player just stops acting).
        bool online = ElderboomLan.Current != ElderboomLan.Mode.Offline;
        Time.timeScale = open && !online ? 0f : 1f;
        AudioListener.pause = open && !online;
        // Display choices only take effect through APPLY VIDEO SETTINGS; closing
        // the menu discards unapplied ones instead of saving them for next launch.
        if (!open)
        {
            fullscreen = ElderboomDisplaySettings.Fullscreen;
            resolutionIndex = ElderboomDisplaySettings.Resolution;
            qualityIndex = ElderboomDisplaySettings.Quality;
        }
        // After death the game-over screen owns the cursor.
        player.SetCursor(!open && !player.IsDead);
        if (!open) ApplySettings(true);
    }

    void ApplyVideo()
    {
        ElderboomInput.ReleaseAll();
        int levels = QualitySettings.names.Length;
        if (levels > 0) QualitySettings.SetQualityLevel(ElderboomDisplaySettings.UnityLevel(qualityIndex), true);
        ElderboomPerformanceMode.Set(qualityIndex == ElderboomDisplaySettings.Performance);
        if (!Application.isEditor)
        {
            ElderboomDisplaySettings.SaveDisplay(fullscreen,resolutionIndex,qualityIndex);
            ElderboomDisplaySettings.ApplySaved();
        }
        ApplySettings(true);
    }

    void ReturnToMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        isOpen = false;
        player.SetCursor(false);
        if (ElderboomTestRange.StandaloneOnly) Application.Quit();
        else SceneManager.LoadScene("ZombieTest_Menu");
    }

    void OnDisable()
    {
        if (isOpen) { Time.timeScale = 1f; AudioListener.pause = false; }
    }

    // ---- UI Toolkit -----------------------------------------------------------------------
    // Side-panel pause menu over the dimmed game, authored at 1920x1080.
    const float Margin = 96f;
    PanelSettings uiPanel;
    VisualElement uiRoot, pauseLayer, pageBody, perfBox;
    Label statusLabel, perfLabel, sessionLabel, detailsLabel;
    Action refreshTabs;
    readonly Dictionary<string, Button> bindingButtons = new Dictionary<string, Button>();
    static readonly string[] PageIds = { "pause", "controls", "video", "audio", "admin" };
    string shownPage;
    bool shownOpen;
    float perfAt;
    static KeyCode[] keyCodes;

    void OnDestroy() { if (uiPanel != null) Destroy(uiPanel); }

    void BuildUi()
    {
        uiRoot = ElderboomUi.CreateRoot(gameObject, "PauseMenuUi", 20, out uiPanel, out _);
        uiRoot.pickingMode = PickingMode.Ignore;
        pauseLayer = new VisualElement();
        pauseLayer.AddToClassList("eb-fill");
        uiRoot.Add(pauseLayer);
        ElderboomUi.Fill(pauseLayer, new Color(0.02f, 0.005f, 0.005f, .45f));
        ElderboomUi.Gradient(pauseLayer, new Color(0, 0, 0, .96f), new Color(0, 0, 0, .2f), true, 1.7f);

        var header = Place(pauseLayer, Margin, 72, 1300);
        ElderboomUi.Text(header, "PAUSED", "eb-h1").AddToClassList("eb-enter");
        sessionLabel = ElderboomUi.Text(header, "", "eb-caption");
        sessionLabel.AddToClassList("eb-enter");
        var bar = new VisualElement { pickingMode = PickingMode.Ignore }; bar.AddToClassList("eb-accent-bar"); bar.AddToClassList("eb-enter"); header.Add(bar);

        var tabs = Place(pauseLayer, Margin, 236, 840);
        tabs.pickingMode = PickingMode.Position;
        refreshTabs = ElderboomUi.Tabs(tabs, new[] { "GAME", "CONTROLS", "VIDEO", "AUDIO", "ADMIN" },
            () => Mathf.Max(0, Array.IndexOf(PageIds, pausePage)), i => { pausePage = PageIds[i]; rebindAction = null; padRebindAction = null; });

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("eb-scroll");
        scroll.style.position = Position.Absolute; scroll.style.left = Margin; scroll.style.top = 296; scroll.style.width = 840; scroll.style.bottom = 150;
        pauseLayer.Add(scroll);
        pageBody = scroll.contentContainer;

        var side = Place(pauseLayer, Margin + 900, 296, 480);
        ElderboomUi.Text(side, "DETAILS", "eb-h3");
        var sideBar = new VisualElement { pickingMode = PickingMode.Ignore }; sideBar.AddToClassList("eb-accent-bar"); side.Add(sideBar);
        detailsLabel = ElderboomUi.Text(side, "", "eb-body", "eb-muted");

        var footer = Place(pauseLayer, Margin, null, 1500);
        footer.style.bottom = 56;
        footer.AddToClassList("eb-row"); footer.style.alignItems = Align.Center;
        var hints = ElderboomUi.KeyHints(footer, ("ESC", "BACK / RESUME"), ("ENTER", "SELECT"));
        hints.style.marginLeft = -26;
        statusLabel = ElderboomUi.Text(footer, "", "eb-caption", "eb-alert");
        statusLabel.style.marginLeft = 40;

        perfBox = new VisualElement { pickingMode = PickingMode.Ignore };
        perfBox.style.position = Position.Absolute; perfBox.style.right = 24; perfBox.style.top = 24;
        perfBox.style.paddingLeft = 12; perfBox.style.paddingRight = 12; perfBox.style.paddingTop = 8; perfBox.style.paddingBottom = 8;
        perfBox.style.backgroundColor = new Color(0f, 0f, 0f, .6f);
        perfBox.style.borderLeftWidth = 2; perfBox.style.borderLeftColor = PauseBorder;
        perfLabel = ElderboomUi.Text(perfBox, "", "eb-caption");
        perfLabel.style.color = new Color(.93f, .9f, .88f);
        uiRoot.Add(perfBox);
    }

    static VisualElement Place(VisualElement parent, float left, float? top, float width)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute; e.style.left = left; e.style.width = width;
        if (top.HasValue) e.style.top = top.Value;
        parent.Add(e);
        return e;
    }

    void LateUpdate()
    {
        if (player == null) return;
        if (uiRoot == null) BuildUi();
        ElderboomUi.Show(pauseLayer, isOpen);
        bool perf = !isOpen && showPerformance;
        ElderboomUi.Show(perfBox, perf);
        if (perf && Time.unscaledTime >= perfAt) { perfAt = Time.unscaledTime + .25f; perfLabel.text = PerformanceText(); }
        if (isOpen)
        {
            if (!shownOpen) OnOpened();
            // the gamepad section appears / disappears as soon as a pad is plugged in or removed
            if (pausePage == "controls" && padShown != ElderboomGamepad.Connected) { shownPage = null; padRebindAction = null; }
            UpdateRebind();
            UpdatePadRebind();
            if (shownPage != pausePage) RebuildPage();
            if (bindingsDirty) RefreshBindingButtons();
            if (statusLabel.text != pauseStatus) statusLabel.text = pauseStatus;
        }
        shownOpen = isOpen;
    }

    void OnOpened()
    {
        var game = ElderboomGame.Active;
        sessionLabel.text = game != null && game.IsPlaying
            ? "WAVE " + game.Wave + "   ·   " + game.Kills + " KILLS   ·   " + game.Credits + " CREDITS   ·   " + player.OperatorDisplayName
            : player.OperatorDisplayName;
        shownPage = null;
        ElderboomUi.ReplayIn(pauseLayer, 40);
        ElderboomUi.Confirm();
    }

    string PerformanceText()
    {
        if (Time.unscaledTime >= zombieCountAt)
        {
            zombieCountAt = Time.unscaledTime + .5f;
            zombieCount = ElderboomGame.Active != null ? ElderboomGame.Active.Zombies.Count : FindObjectsByType<ElderboomZombie>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        }
        float frame = Time.unscaledDeltaTime;
        return Mathf.RoundToInt(1f / Mathf.Max(frame, 0.0001f)) + " FPS   ·   " + (frame * 1000f).ToString("0.0") + " MS   ·   "
            + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f)).ToString("0") + " MB   ·   " + zombieCount + " ZOMBIES";
    }

    void Describe(string text) => detailsLabel.text = text;

    void RebuildPage()
    {
        shownPage = pausePage;
        pageBody.Clear();
        bindingButtons.Clear();
        padButtons.Clear();
        refreshTabs?.Invoke();
        Describe("");
        if (pausePage == "controls") BuildControls();
        else if (pausePage == "video") BuildVideo();
        else if (pausePage == "audio") BuildAudio();
        else if (pausePage == "admin") BuildAdmin();
        else BuildHome();
        long delay = 0;
        foreach (var row in pageBody.Children()) { ElderboomUi.AnimateIn(row, delay, true); delay += 28; }
    }

    void BuildHome()
    {
        ElderboomUi.MenuItem(pageBody, "01", "RESUME", () => SetSettingsOpen(false), null, false, () => Describe("Back to the fight."));
        ElderboomUi.MenuItem(pageBody, "02", "SETTINGS", () => pausePage = "video", null, false, () => Describe("Display, audio and controls."));
        bool rangeOnly = ElderboomTestRange.StandaloneOnly;
        ElderboomUi.MenuItem(pageBody, "03", rangeOnly ? "QUIT" : "MAIN MENU", ReturnToMenu, null, false,
            () => Describe(rangeOnly ? "Close the test range." : "Leave this run and return to the main menu. Progress in the run is lost."));
        pageBody.schedule.Execute(() => pageBody.Query(className: "eb-menu-item").First()?.Focus()).StartingIn(60);
    }

    void BuildControls()
    {
        padShown = ElderboomGamepad.Connected;
        if (padShown) BuildPadControls();
        // Mouse sensitivity can be tuned here too, while playing (applies at once).
        var s = ElderboomUi.Setting(pageBody, "Mouse sensitivity", "How far the view turns for the same mouse movement.", Describe);
        ElderboomUi.SliderControl(s, sensitivity, .25f, 6f, v => v.ToString("0.00"), v => { sensitivity = v; PlayerPrefs.SetFloat(SensitivityKey, v); });
        foreach (var entry in ElderboomInput.DefaultBindings)
        {
            string id = entry.id;
            var c = ElderboomUi.Setting(pageBody, entry.label, "Select, then press a key or mouse button. ESC cancels. A key already in use is swapped.", Describe);
            var button = ElderboomUi.Button(c, "", () =>
            {
                rebindAction = id; rebindWaitRelease = true; bindingsDirty = true;
                pauseStatus = "PRESS A KEY OR MOUSE BUTTON  ·  ESC CANCELS";
            }, false, 220);
            button.style.height = 38;
            bindingButtons[id] = button;
        }
        RefreshBindingButtons();
        var buttons = new VisualElement();
        buttons.AddToClassList("eb-row");
        buttons.style.marginTop = 22;
        ElderboomUi.Button(buttons, "SAVE CONTROLS", () => { ElderboomInput.Save(); pauseStatus = "CONTROL BINDINGS SAVED"; }, true).style.marginRight = 12;
        ElderboomUi.Button(buttons, "RESTORE DEFAULTS", () => { ElderboomInput.RestoreDefaults(); pauseStatus = "DEFAULT BINDINGS RESTORED"; bindingsDirty = true; });
        pageBody.Add(buttons);
    }

    // ---- gamepad (only while a pad is connected) ----
    void BuildPadControls()
    {
        padLayout = ElderboomGamepad.Active;
        ElderboomUi.Text(pageBody, "GAMEPAD", "eb-h3");
        var c = ElderboomUi.Setting(pageBody, "Layout to edit", "SHOOTER is used by the gun operators, MELEE by the Steel Sentinel. Each keeps its own buttons.", Describe);
        ElderboomUi.SelectorControl(c, new[] { "SHOOTER", "MELEE" }, () => (int)padLayout, v => { padLayout = (ElderboomGamepad.Layout)v; padRebindAction = null; RefreshPadButtons(); });
        foreach (var entry in ElderboomGamepad.Actions)
        {
            string id = entry.id;
            var s = ElderboomUi.Setting(pageBody, (entry.shooter ?? entry.melee) + "  ·  pad",
                "Select, then press a pad button. START or ESC cancels. A button already in use is swapped; Interact can share a button with one action (tap vs hold).", Describe);
            var button = ElderboomUi.Button(s, "", () =>
            {
                if (ElderboomGamepad.ActionLabel(padLayout, id) == null) return;
                padRebindAction = id; padWaitRelease = true; rebindAction = null; bindingsDirty = true;
                pauseStatus = "PRESS A PAD BUTTON  ·  START CANCELS";
            }, false, 220);
            button.style.height = 38;
            padButtons[id] = button;
        }
        var row = new VisualElement();
        row.AddToClassList("eb-row");
        row.style.marginTop = 14; row.style.marginBottom = 30;
        ElderboomUi.Button(row, "RESTORE PAD DEFAULTS", () =>
        {
            ElderboomGamepad.RestoreDefaults(padLayout); padRebindAction = null;
            pauseStatus = (padLayout == ElderboomGamepad.Layout.Melee ? "MELEE" : "SHOOTER") + " PAD LAYOUT RESTORED"; bindingsDirty = true;
        });
        pageBody.Add(row);
        ElderboomUi.Text(pageBody, "KEYBOARD AND MOUSE", "eb-h3");
        RefreshPadButtons();
    }

    void RefreshPadButtons()
    {
        foreach (var pair in padButtons)
        {
            string label = ElderboomGamepad.ActionLabel(padLayout, pair.Key);
            var setting = pair.Value.parent?.parent;
            if (setting != null) setting.style.display = label == null ? DisplayStyle.None : DisplayStyle.Flex;
            var title = setting?.Q<Label>();
            if (title != null && label != null) title.text = label;
            bool waiting = padRebindAction == pair.Key;
            var b = ElderboomGamepad.Binding(padLayout, pair.Key);
            ElderboomUi.SetText(pair.Value, waiting ? "PRESS A BUTTON…" : ElderboomGamepad.ButtonName(b));
            ElderboomUi.SetSelected(pair.Value, waiting);
        }
    }

    void UpdatePadRebind()
    {
        if (string.IsNullOrEmpty(padRebindAction)) return;
        if (padWaitRelease) { if (!ElderboomGamepad.AnyButtonHeld()) padWaitRelease = false; return; }
        var pressed = ElderboomGamepad.PressedButton();
        if (pressed == ElderboomGamepad.PadButton.None) return;
        ElderboomGamepad.Assign(padLayout, padRebindAction, pressed);
        padRebindAction = null; pauseStatus = ""; bindingsDirty = true;
        ElderboomUi.Confirm();
    }

    void RefreshBindingButtons()
    {
        bindingsDirty = false;
        RefreshPadButtons();
        foreach (var pair in bindingButtons)
        {
            bool waiting = rebindAction == pair.Key;
            ElderboomUi.SetText(pair.Value, waiting ? "PRESS A KEY…" : ElderboomInput.Label(pair.Key).ToUpperInvariant());
            ElderboomUi.SetSelected(pair.Value, waiting);
        }
    }

    // Polls the legacy Input Manager, so it works whatever element has focus.
    void UpdateRebind()
    {
        if (string.IsNullOrEmpty(rebindAction)) return;
        if (rebindWaitRelease)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) rebindWaitRelease = false;
            return;
        }
        keyCodes ??= (KeyCode[])Enum.GetValues(typeof(KeyCode));
        foreach (var key in keyCodes)
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key >= KeyCode.JoystickButton0) continue;
            if (!Input.GetKeyDown(key)) continue;
            ElderboomInput.Assign(rebindAction, key);
            rebindAction = null;
            pauseStatus = "";
            bindingsDirty = true;
            ElderboomUi.Confirm();
            return;
        }
    }

    // Brightness, field of view, HUD size and volumes apply live; display mode,
    // resolution and quality wait for APPLY (closing the menu discards them).
    void BuildVideo()
    {
        var c = ElderboomUi.Setting(pageBody, "Display mode", "Fullscreen fills the display; windowed uses the selected resolution. Press APPLY to use it.", Describe);
        ElderboomUi.SelectorControl(c, new[] { "WINDOWED", "FULLSCREEN" }, () => fullscreen ? 1 : 0, v => fullscreen = v == 1);
        var labels = new string[Resolutions.Length];
        for (int i = 0; i < labels.Length; i++) labels[i] = Resolutions[i].x + " × " + Resolutions[i].y;
        c = ElderboomUi.Setting(pageBody, "Resolution", "Window size when not in fullscreen. Press APPLY to use it.", Describe);
        ElderboomUi.SelectorControl(c, labels, () => resolutionIndex, v => resolutionIndex = v);
        c = ElderboomUi.Setting(pageBody, "Graphics quality", "Performance is the lightest (for slow computers), high favours image quality. Press APPLY to use it.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomDisplaySettings.QualityLabels, () => ElderboomDisplaySettings.LabelIndex(qualityIndex),
            v => qualityIndex = ElderboomDisplaySettings.FromLabel(v));
        c = ElderboomUi.Setting(pageBody, "Vertical sync", "Locks the frame rate to the display refresh to avoid tearing.", Describe);
        ElderboomUi.SwitchControl(c, () => vsync, v => { vsync = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Brightness", "Overall exposure of the night scenes.", Describe);
        ElderboomUi.SliderControl(c, brightness, .5f, 1.5f, v => Mathf.RoundToInt(v * 100f) + "%", v => { brightness = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Field of view", "Horizontal view angle. Wider shows more, narrower magnifies.", Describe);
        ElderboomUi.SliderControl(c, fieldOfView, 65f, 110f, v => Mathf.RoundToInt(v) + "°", v => { fieldOfView = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "HUD size", "Scale of the in-game HUD.", Describe);
        ElderboomUi.SliderControl(c, hudScale, .75f, 1.25f, v => Mathf.RoundToInt(v * 100f) + "%", v => { hudScale = v; PlayerPrefs.SetFloat(HudScaleKey, v); });
        // Extra options for slower PCs: applied at once, on top of the quality preset.
        c = ElderboomUi.Setting(pageBody, "View distance", "How far buildings, props and trees are drawn. NEAR and MEDIUM help slow PCs a lot; the mountains stay visible.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomGraphicsOptions.DistanceLabels, () => ElderboomGraphicsOptions.Distance, v => ElderboomGraphicsOptions.Distance = v);
        c = ElderboomUi.Setting(pageBody, "Render resolution", "Internal resolution, upscaled with FSR. AUTO follows the quality preset; lower is faster but softer.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomGraphicsOptions.ScaleLabels, () => ElderboomGraphicsOptions.Scale, v => ElderboomGraphicsOptions.Scale = v);
        c = ElderboomUi.Setting(pageBody, "Lamp shadows", "Shadows of the village lamps near you. OFF is the biggest saving at night; AUTO follows the quality preset.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomGraphicsOptions.ShadowLabels, () => ElderboomGraphicsOptions.Shadows, v => ElderboomGraphicsOptions.Shadows = v);
        c = ElderboomUi.Setting(pageBody, "Volumetric fog", "Light shafts in the fog around lamps and the moon. Off saves a lot on older graphics cards.", Describe);
        ElderboomUi.SwitchControl(c, () => ElderboomGraphicsOptions.VolumetricFog, v => ElderboomGraphicsOptions.VolumetricFog = v);
        c = ElderboomUi.Setting(pageBody, "Ambient occlusion", "Soft contact shadows in corners and under objects.", Describe);
        ElderboomUi.SwitchControl(c, () => ElderboomGraphicsOptions.AmbientOcclusion, v => ElderboomGraphicsOptions.AmbientOcclusion = v);
        c = ElderboomUi.Setting(pageBody, "Grass and small plants", "Density and distance of the grass on the ground.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomGraphicsOptions.GrassLabels, () => ElderboomGraphicsOptions.Grass, v => ElderboomGraphicsOptions.Grass = v);
        c = ElderboomUi.Setting(pageBody, "Frame rate limit", "Caps the frame rate when vertical sync is off: steadier frames, cooler and quieter PC.", Describe);
        ElderboomUi.SelectorControl(c, ElderboomGraphicsOptions.FpsLabels, () => ElderboomGraphicsOptions.FrameCap, v => { ElderboomGraphicsOptions.FrameCap = v; ApplySettings(false); });
        var apply = ElderboomUi.Button(pageBody, "APPLY DISPLAY SETTINGS", () => { ApplyVideo(); pauseStatus = "DISPLAY SETTINGS APPLIED"; }, true);
        apply.style.marginTop = 22; apply.style.alignSelf = Align.FlexStart;
    }

    void BuildAudio()
    {
        var c = ElderboomUi.Setting(pageBody, "Master volume", "Volume of everything.", Describe);
        ElderboomUi.SliderControl(c, masterVolume, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", v => { masterVolume = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Music volume", "Ambient and menu music.", Describe);
        ElderboomUi.SliderControl(c, musicVolume, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", v => { musicVolume = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Effects volume", "Weapons, zombies, abilities and interface.", Describe);
        ElderboomUi.SliderControl(c, effectsVolume, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", v => { effectsVolume = v; ApplySettings(false); });
    }

    void BuildAdmin()
    {
        var c = ElderboomUi.Setting(pageBody, "Performance overlay", "Frame rate, frame time, managed memory and active zombies, shown in game. Draw calls are not exposed in player builds.", Describe);
        ElderboomUi.SwitchControl(c, () => showPerformance, v => { showPerformance = v; PlayerPrefs.SetInt(PerfKey, v ? 1 : 0); });
    }
}
