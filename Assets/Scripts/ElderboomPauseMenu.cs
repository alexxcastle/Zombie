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
    const string FieldOfViewKey = "hdrp.settings.field_of_view";
    const string MasterVolumeKey = "hdrp.settings.master_volume";
    const string FullscreenKey = ElderboomDisplaySettings.FullscreenKey;
    const string MusicKey = "hdrp.settings.music_volume";
    const string EffectsKey = "hdrp.settings.effects_volume";
    const string VsyncKey = "hdrp.settings.vsync";
    const string ResolutionKey = ElderboomDisplaySettings.ResolutionKey;
    const string QualityKey = ElderboomDisplaySettings.QualityKey;
    const string HudScaleKey = "hdrp.settings.hud_scale";
    const string PerfKey = "hdrp.settings.performance";
    const float DefaultBrightness = 1f;
    const float DefaultFieldOfView = 72f;
    const float DefaultMasterVolume = 1f;
    string pausePage = "pause";
    string pauseStatus = "";
    string rebindAction;
    bool rebindWaitRelease;
    bool bindingsDirty;
    float brightness = DefaultBrightness;
    float fieldOfView = DefaultFieldOfView;
    float masterVolume = DefaultMasterVolume;
    float musicVolume = 0.8f;
    float effectsVolume = 0.85f;
    bool fullscreen = true;
    bool vsync = true;
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
    public float FieldOfView => fieldOfView;

    public void Initialize(HDRPSlicePlayer owner, float defaultSensitivity)
    {
        player = owner;
        sensitivity = defaultSensitivity;
        LoadSettings();
        ConfigureBrightness();
        ApplySettings(false);
    }

    // Used by HDRPSlicePlayer.ResetSession: closes without saving or touching the cursor.
    public void ForceClose() { isOpen = false; rebindAction = null; }

    void Update()
    {
        // As before the split: no pause while the player is disabled (operator menu, loading).
        if (player == null || (!player.enabled && !isOpen) || !Input.GetKeyDown(KeyCode.Escape)) return;
        if (!string.IsNullOrEmpty(rebindAction)) { rebindAction = null; bindingsDirty = true; pauseStatus = "Press a keyboard key or mouse button. ESC cancels."; return; }
        if (!isOpen) { if (player.IsDead) return; pausePage = "pause"; SetSettingsOpen(true); }
        else if (pausePage == "pause") SetSettingsOpen(false);
        else { pausePage = "pause"; ElderboomUi.Back(); }
    }

    void LoadSettings()
    {
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, sensitivity), .25f, 6f);
        brightness = Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness), .5f, 1.5f);
        fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(FieldOfViewKey, DefaultFieldOfView), 60f, 100f);
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.8f));
        effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 0.85f));
        fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
        vsync = PlayerPrefs.GetInt(VsyncKey, 1) == 1;
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
        if (player.ViewCamera != null) player.ViewCamera.fieldOfView = fieldOfView;
        AudioListener.volume = masterVolume;
        ElderboomAudio.Effects = effectsVolume;
        QualitySettings.vSyncCount = vsync ? 1 : 0;
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
        SceneManager.LoadScene("ZombieTest_Menu");
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
            () => Mathf.Max(0, Array.IndexOf(PageIds, pausePage)), i => { pausePage = PageIds[i]; rebindAction = null; });

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
            UpdateRebind();
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
        ElderboomUi.MenuItem(pageBody, "03", "MAIN MENU", ReturnToMenu, null, false, () => Describe("Leave this run and return to the main menu. Progress in the run is lost."));
        pageBody.schedule.Execute(() => pageBody.Query(className: "eb-menu-item").First()?.Focus()).StartingIn(60);
    }

    void BuildControls()
    {
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

    void RefreshBindingButtons()
    {
        bindingsDirty = false;
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
        ElderboomUi.SelectorControl(c, new[] { "PERFORMANCE", "LOW", "MEDIUM", "HIGH" }, () => qualityIndex == ElderboomDisplaySettings.Performance ? 0 : qualityIndex + 1,
            v => qualityIndex = v == 0 ? ElderboomDisplaySettings.Performance : v - 1);
        c = ElderboomUi.Setting(pageBody, "Vertical sync", "Locks the frame rate to the display refresh to avoid tearing.", Describe);
        ElderboomUi.SwitchControl(c, () => vsync, v => { vsync = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Brightness", "Overall exposure of the night scenes.", Describe);
        ElderboomUi.SliderControl(c, brightness, .5f, 1.5f, v => Mathf.RoundToInt(v * 100f) + "%", v => { brightness = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "Field of view", "Horizontal view angle. Wider shows more, narrower magnifies.", Describe);
        ElderboomUi.SliderControl(c, fieldOfView, 60f, 100f, v => Mathf.RoundToInt(v) + "°", v => { fieldOfView = v; ApplySettings(false); });
        c = ElderboomUi.Setting(pageBody, "HUD size", "Scale of the in-game HUD.", Describe);
        ElderboomUi.SliderControl(c, hudScale, .75f, 1.25f, v => Mathf.RoundToInt(v * 100f) + "%", v => { hudScale = v; PlayerPrefs.SetFloat(HudScaleKey, v); });
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
