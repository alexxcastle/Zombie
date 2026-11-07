using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

public sealed class ElderboomMenu : MonoBehaviour
{
    public static string Operator = ElderboomIds.Soph, Difficulty = "NORMAL";
    public static bool LaunchRequested;
    enum Page { Home, Operators, Difficulty, Settings, Loading, Lan }
    Page page;
    float loadedAt, spinnerAngle;
    string tip, message = "SURVIVE THE NIGHT  /  ELDERBOOM VILLAGE";
    VideoPlayer video;
    RenderTexture background;
    AudioSource music;
    Texture2D[] frames;
    Texture2D logo;
    int frameIndex;
    float frameAt;
    readonly string[] tips =
    {
        "Eli can make zombies ignore you. Try not to take it personally.","Soph brought a rifle and a plasma pistol. She dislikes waiting her turn.",
        "The Tactical operator believes personal space should be enforced.","Reload before the horde arrives. Zombies rarely respect intermissions.",
        "Keep Away is not a suggestion. The glove makes that clear.","Blinded zombies wander forever. They still dislike close encounters.",
        "Eli's freeze signal reaches seven meters. Teeth are closer than that.","Soph fires three plasma shots. The rifle remains available.",
        "Your stamina has limits. The zombies have poor scheduling skills.","A quiet zombie is not necessarily a polite zombie.",
        "Credits open gates. Asking nicely has not worked yet.","A clear escape route beats an impressive last stand.",
        "Do not reload in a dead end. It is an unnecessarily accurate name.","Syringes heal forty health. They do not fix bad decisions.",
        "Keep your distance from blinded zombies. Their hearing still works.","Shotguns prefer introductions at close range.",
        "Rifles reach farther. Your attention should too.","When the music stops, the audience becomes difficult again.",
        "Use the quiet between waves. It was expensive to arrange.","The village has exits. Some require a financial conversation.",
        "Running away is a tactical reposition. We checked.","Count your shots. The magazine is already doing it.",
        "A plasma flare is orange first, almost white at its brightest.","The horde has numbers. Your operator has tricks.",
        "A gate is useful cover until you are on the wrong side.","Headshots are easier when you stop admiring the scenery.",
        "Eli's playlist lasts fifteen seconds. Make them count.","The glove can lift a zombie. Its social prospects remain unchanged.",
        "Breathing hard means your sprint budget is exhausted.","Never assume the last zombie is the only zombie.",
        "A good reload window is one you create with an ability.","The village was peaceful before the uninvited walking tour."
    };

    static readonly Color Blood = new Color(0.29f, 0.012f, 0.028f, 0.96f);
    static readonly Color BloodBorder = new Color(0.72f, 0.045f, 0.085f);
    static readonly Color Panel = new Color(0.04f, 0.035f, 0.04f, 0.92f);
    static readonly Color Muted = new Color(0.72f, 0.75f, 0.78f);
    // Same keys and defaults as HDRPSlicePlayer.
    const string MasterKey = "hdrp.settings.master_volume";
    const string MusicKey = "hdrp.settings.music_volume";
    const string EffectsKey = "hdrp.settings.effects_volume";
    const float MusicBase = 0.35f;

    void ApplyAudioPrefs()
    {
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
        ElderboomAudio.Effects = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 0.85f));
        if (music != null) music.volume = MusicBase * Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.8f));
    }

    void Awake()
    {
        Time.timeScale = 1;
        ElderboomLan.Shutdown(); // back in the main menu: leave any LAN game
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ElderboomDisplaySettings.ApplySaved();
        logo = Resources.Load<Texture2D>("Elderboom/UI/logo");
        frames = Resources.LoadAll<Texture2D>("Elderboom/UI/MenuFrames");
        var clip = Resources.Load<AudioClip>("Elderboom/UI/haunted_corridor");
        if (clip != null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.clip = clip;
            music.loop = true;
            music.volume = MusicBase * PlayerPrefs.GetFloat(MusicKey, 0.8f);
            music.Play();
        }
        ApplyAudioPrefs();
        BuildUi();
        StartMenuVideo();
    }

    // Animated backdrop: a seamless looping video (StreamingAssets/elderboom_menu_loop.mp4); the old frame
    // sequence stays as the fallback when the video cannot play.
    VideoPlayer menuVideo;
    RenderTexture menuVideoTexture;
    bool menuVideoLive;
    void StartMenuVideo()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "elderboom_menu_loop.mp4");
        if (!File.Exists(path) && !path.Contains("://")) return;
        menuVideo = gameObject.AddComponent<VideoPlayer>();
        menuVideo.playOnAwake = false; menuVideo.url = path; menuVideo.isLooping = true; menuVideo.skipOnDrop = true;
        menuVideo.audioOutputMode = VideoAudioOutputMode.None; menuVideo.renderMode = VideoRenderMode.RenderTexture;
        menuVideoTexture = new RenderTexture(1920, 1080, 0); menuVideo.targetTexture = menuVideoTexture;
        menuVideo.prepareCompleted += v => { v.Play(); menuVideoLive = true; if (frameLayer != null) frameLayer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(menuVideoTexture)); };
        menuVideo.errorReceived += (v, e) => { menuVideoLive = false; Debug.LogWarning("Menu video: " + e); };
        menuVideo.Prepare();
    }

    void Update()
    {
        if (frames != null && frames.Length > 1 && Time.unscaledTime >= frameAt)
        {
            frameAt = Time.unscaledTime + 1f / 24f;
            frameIndex = (frameIndex + 1) % frames.Length;
        }
        spinnerAngle += Time.unscaledDeltaTime * 210f;
        HandleBackAndRebind();
        UpdateUi();
    }

    IEnumerator Load()
    {
        page = Page.Loading;
        tip = tips[UnityEngine.Random.Range(0, tips.Length)];
        ShowPage();
        if (music != null) music.Stop();
        if (menuVideo != null) menuVideo.Stop();
        DontDestroyOnLoad(gameObject);
        loadedAt = Time.realtimeSinceStartup;
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.url = Path.Combine(Application.streamingAssetsPath, "elderboom_loading.mp4");
        background = new RenderTexture(1920, 1080, 0);
        video.targetTexture = background;
        loadingBackground.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(background));
        video.renderMode = VideoRenderMode.RenderTexture;
        video.isLooping = true;
        video.audioOutputMode = VideoAudioOutputMode.None;
        video.playbackSpeed = .75f;
        video.Prepare();
        float until = Time.realtimeSinceStartup + 3;
        while (!video.isPrepared && Time.realtimeSinceStartup < until) yield return null;
        if (video.isPrepared) video.Play();
        LaunchRequested = true;
        var operation = SceneManager.LoadSceneAsync("Elderboom_Unity_Port");
        operation.allowSceneActivation = false;
        while (operation.progress < .9f || Time.realtimeSinceStartup - loadedAt < 1.5f) yield return null;
        operation.allowSceneActivation = true;
        while (!operation.isDone) yield return null;
        yield return new WaitForEndOfFrame();
        video.Stop();
        background.Release();
        Destroy(background);
        Destroy(gameObject);
    }

    // ---- UI Toolkit -------------------------------------------------------------------------
    // Layout is authored at 1920x1080 (the panel scales with screen height).
    const float Margin = 120f;
    PanelSettings panelSettings;
    VisualElement uiRoot, frameLayer, homePage, operatorsPage, difficultyPage, settingsPage, loadingPage, loadingBackground, spinner;
    VisualElement operatorDetails, operatorArt, settingsBody;
    Label homeDescription, settingsDescription, tipLabel, deployLabel;
    readonly List<(VisualElement item, string id)> operatorItems = new List<(VisualElement, string)>();
    readonly List<(VisualElement card, string id)> difficultyCards = new List<(VisualElement, string)>();
    readonly Dictionary<string, Button> bindingButtons = new Dictionary<string, Button>();
    int shownFrame = -1, settingsTab;
    string rebinding;
    bool rebindWaitRelease;
    static KeyCode[] keyCodes;

    void BuildUi()
    {
        uiRoot = ElderboomUi.CreateRoot(gameObject, "MainMenuUi", 0, out panelSettings, out _);
        // Backdrop: base, animated frames, readability gradients and a vignette.
        backdropBase = ElderboomUi.Fill(uiRoot, new Color(0.008f, 0.006f, 0.006f, 1f));
        frameLayer = ElderboomUi.Fill(uiRoot, Color.clear);
        frameLayer.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
        backdropTint = ElderboomUi.Fill(uiRoot, new Color(0.05f, 0.004f, 0.004f, 0.25f));
        ElderboomUi.Gradient(uiRoot, new Color(0, 0, 0, .94f), new Color(0, 0, 0, 0), true, .7f);
        ElderboomUi.Gradient(uiRoot, new Color(0, 0, 0, 0), new Color(0, 0, 0, .9f), false, 2.2f);
        var vignette = ElderboomUi.Fill(uiRoot, Color.clear);
        vignette.style.backgroundImage = new StyleBackground(ElderboomFeel.VignetteTexture);
        vignette.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
        vignette.style.unityBackgroundImageTintColor = new Color(0, 0, 0, .75f);

        BuildHome();
        BuildOperators();
        BuildDifficulty();
        BuildSettings();
        BuildLan();
        BuildLoading();
        RefreshSelection();
        ShowPage();
    }

    static VisualElement MakePage(VisualElement root)
    {
        var page = new VisualElement { pickingMode = PickingMode.Ignore };
        page.AddToClassList("eb-fill");
        root.Add(page);
        return page;
    }

    static VisualElement Absolute(VisualElement parent, float? left = null, float? top = null, float? right = null, float? bottom = null, float? width = null)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        if (left.HasValue) e.style.left = left.Value;
        if (top.HasValue) e.style.top = top.Value;
        if (right.HasValue) e.style.right = right.Value;
        if (bottom.HasValue) e.style.bottom = bottom.Value;
        if (width.HasValue) e.style.width = width.Value;
        parent.Add(e);
        return e;
    }

    static T Enter<T>(T e, bool up = false) where T : VisualElement
    {
        e.AddToClassList("eb-enter");
        if (up) e.AddToClassList("eb-enter--up");
        return e;
    }

    static VisualElement AccentBar()
    {
        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("eb-accent-bar");
        return bar;
    }

    // Steps "01 OPERATOR — 02 DIFFICULTY — 03 DEPLOY" and a page title.
    VisualElement Header(VisualElement page, int step, string title, string subtitle)
    {
        var header = Absolute(page, Margin, 86, width: 1200);
        if (step > 0)
        {
            var steps = Enter(new VisualElement { pickingMode = PickingMode.Ignore });
            steps.AddToClassList("eb-row");
            steps.style.marginBottom = 8;
            string[] names = { "01  OPERATOR", "02  DIFFICULTY", "03  DEPLOY" };
            for (int i = 0; i < names.Length; i++)
            {
                var l = ElderboomUi.Text(steps, names[i], "eb-step", "eb-bold");
                if (i + 1 == step) l.AddToClassList("eb-step--active");
                else if (i + 1 < step) l.AddToClassList("eb-step--done");
            }
            header.Add(steps);
        }
        header.Add(Enter(ElderboomUi.Text(null, title, "eb-h2")));
        if (!string.IsNullOrEmpty(subtitle)) header.Add(Enter(ElderboomUi.Text(null, subtitle, "eb-caption")));
        header.Add(Enter(AccentBar()));
        return header;
    }

    VisualElement Footer(VisualElement page, params (string text, Action action, bool primary)[] buttons)
    {
        var bar = Absolute(page, Margin, bottom: 52, right: Margin);
        bar.AddToClassList("eb-row");
        bar.style.alignItems = Align.Center;
        bar.style.justifyContent = Justify.SpaceBetween;
        var hints = ElderboomUi.KeyHints(bar, ("ENTER", "SELECT"), ("ESC", "BACK"));
        hints.style.marginLeft = -26;
        var right = new VisualElement();
        right.AddToClassList("eb-row");
        bar.Add(right);
        foreach (var (text, action, primary) in buttons)
        {
            var b = ElderboomUi.Button(right, text, action, primary, 210);
            b.style.marginLeft = 14;
        }
        return bar;
    }

    // ---- Home ----
    void BuildHome()
    {
        homePage = MakePage(uiRoot);
        var column = Absolute(homePage, Margin, 0, bottom: 0, width: 780);
        column.style.justifyContent = Justify.Center;
        // Wordmark: the small logo as an emblem beside large spaced type (the logo
        // texture is 163 px, too small to be the hero element).
        var brand = Enter(new VisualElement { pickingMode = PickingMode.Ignore });
        brand.AddToClassList("eb-row");
        brand.style.alignItems = Align.Center;
        if (logo != null)
        {
            var emblem = new Image { image = logo, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            emblem.style.width = 112; emblem.style.height = 112; emblem.style.marginRight = 22; emblem.style.marginLeft = -8;
            brand.Add(emblem);
        }
        var words = new VisualElement { pickingMode = PickingMode.Ignore };
        var wordmark = ElderboomUi.Text(words, "ELDERBOOM", "eb-h1");
        wordmark.style.fontSize = 92; wordmark.style.letterSpacing = 14; wordmark.style.marginBottom = -6;
        ElderboomUi.Text(words, "SURVIVE THE NIGHT   ·   ELDERBOOM VILLAGE", "eb-caption").style.marginLeft = 4;
        brand.Add(words);
        column.Add(brand);
        var bar = Enter(AccentBar()); bar.style.marginTop = 18; bar.style.marginBottom = 44; column.Add(bar);
        var entries = new (string label, string tag, string description, Action action)[]
        {
            ("PLAY", null, "Choose an operator and hold the village against the waves.", () => { LeaveLan(); GoTo(Page.Operators); }),
            ("MULTIPLAYER", "CO-OP", "Play with friends: online with a code, or on the same Wi-Fi.", () => GoTo(Page.Lan)),
            ("SETTINGS", null, "Controls, sensitivity, display and audio.", () => GoTo(Page.Settings)),
            ("QUIT", null, "Close Elderboom.", () => Application.Quit())
        };
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var item = ElderboomUi.MenuItem(column, (i + 1).ToString("00"), entry.label, entry.action, entry.tag, false, () => homeDescription.text = entry.description);
            if (entry.tag == "SOON") item.AddToClassList("eb-menu-item--disabled");
            Enter(item);
        }
        homeDescription = Enter(ElderboomUi.Text(column, entries[0].description, "eb-body", "eb-muted"));
        homeDescription.style.marginTop = 26; homeDescription.style.height = 26; homeDescription.style.marginLeft = 21;
        var footer = Absolute(homePage, Margin, bottom: 52, right: Margin);
        footer.AddToClassList("eb-row"); footer.style.justifyContent = Justify.SpaceBetween; footer.style.alignItems = Align.Center;
        ElderboomUi.Text(footer, "ELDERBOOM   ·   UNITY HDRP   ·   BUILD " + Application.version, "eb-caption");
        ElderboomUi.KeyHints(footer, ("ENTER", "SELECT"), ("ESC", "BACK"));
    }

    // ---- Operators ----
    void BuildOperators()
    {
        operatorsPage = MakePage(uiRoot);
        operatorArt = Absolute(operatorsPage, right: 0, top: 0, bottom: 0, width: 1000);
        ElderboomUi.Gradient(operatorsPage, new Color(0, 0, 0, .9f), new Color(0, 0, 0, 0), true, .55f);
        Header(operatorsPage, 1, "CHOOSE YOUR OPERATOR", null);
        var list = Absolute(operatorsPage, Margin, 268, width: 360);
        var operators = ElderboomOperators.All;
        for (int i = 0; i < operators.Count; i++)
        {
            var op = operators[i];
            var item = Enter(ElderboomUi.MenuItem(list, (i + 1).ToString("00"), op.displayName, () => { Operator = op.id; RefreshSelection(); }, null, true,
                () => { if (Operator != op.id) { Operator = op.id; RefreshSelection(); } }));
            operatorItems.Add((item, op.id));
        }
        operatorDetails = Absolute(operatorsPage, Margin + 440, 262, width: 620);
        Footer(operatorsPage, ("BACK", () => GoTo(LanFlow ? Page.Lan : Page.Home), false), ("CONTINUE", () => { if (lanJoin != null || lanJoinCode != null) { if (!deploying) StartCoroutine(JoinAndDeploy()); } else GoTo(Page.Difficulty); }, true));
    }

    void FillOperatorDetails()
    {
        var op = ElderboomOperators.Find(Operator) ?? ElderboomOperators.All[0];
        operatorDetails.Clear();
        operatorArt.Clear();
        if (ShowOperatorPreview(op.id))
        {
            // The operator stands in the full-screen 3D set behind the menu: no portrait.
        }
        else if (op.portrait != null)
        {
            var art = new Image { image = op.portrait, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            art.AddToClassList("eb-fill");
            art.style.top = 60;
            operatorArt.Add(art);
            ElderboomUi.AnimateIn(art, 30);
        }
        else
        {
            // No portrait yet: oversized operator name as background typography.
            var word = ElderboomUi.Text(operatorArt, op.displayName, "eb-bold");
            word.style.fontSize = 300; word.style.letterSpacing = 20; word.style.color = new Color(1, 1, 1, .045f);
            word.style.position = Position.Absolute; word.style.right = 40; word.style.bottom = 120;
            ElderboomUi.AnimateIn(word, 30);
        }
        long d = 0;
        Action<VisualElement> add = e => { operatorDetails.Add(e); ElderboomUi.AnimateIn(e, d, true); d += 35; };
        add(ElderboomUi.Text(null, string.IsNullOrEmpty(op.cardTitle) ? op.displayName : op.cardTitle, "eb-h2"));
        add(ElderboomUi.Text(null, op.cardTag, "eb-caption"));
        add(AccentBar());
        var body = ElderboomUi.Text(null, op.cardBody, "eb-body"); body.style.marginBottom = 26; add(body);

        add(ElderboomUi.Text(null, "LOADOUT", "eb-h3"));
        var loadout = new VisualElement { pickingMode = PickingMode.Ignore }; loadout.AddToClassList("eb-row"); loadout.style.marginTop = 8; loadout.style.marginBottom = 26;
        foreach (var (slot, id) in new[] { ("PRIMARY", op.primaryWeapon), ("SIDEARM", op.secondaryWeapon), ("EXTRA", op.extraWeapon) })
        {
            if (string.IsNullOrEmpty(id)) continue;
            var weapon = ElderboomWeapons.Find(id);
            var cell = new VisualElement { pickingMode = PickingMode.Ignore }; cell.style.marginRight = 34;
            ElderboomUi.Text(cell, slot, "eb-caption");
            var name = ElderboomUi.Text(cell, weapon != null ? weapon.name : id.ToUpperInvariant(), "eb-bold"); name.style.fontSize = 21;
            loadout.Add(cell);
        }
        add(loadout);

        add(ElderboomUi.Text(null, "ABILITIES", "eb-h3"));
        foreach (var ability in op.abilities)
        {
            if (ability == null) continue;
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.marginTop = 10; row.style.paddingLeft = 14; row.style.borderLeftWidth = 2; row.style.borderLeftColor = new Color(1, 1, 1, .12f);
            var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("eb-row"); line.style.alignItems = Align.Center;
            var name = ElderboomUi.Text(line, ability.displayName.ToUpperInvariant(), "eb-bold"); name.style.fontSize = 19; name.style.letterSpacing = 2;
            var cd = ElderboomUi.Text(line, Mathf.RoundToInt(ability.cooldown) + " S COOLDOWN", "eb-caption"); cd.style.marginLeft = 14;
            row.Add(line);
            if (!string.IsNullOrEmpty(ability.description)) { var desc = ElderboomUi.Text(row, ability.description, "eb-muted"); desc.style.fontSize = 15; }
            add(row);
        }
    }

    // ---- Difficulty ----
    void BuildDifficulty()
    {
        difficultyPage = MakePage(uiRoot);
        Header(difficultyPage, 2, "CHOOSE DIFFICULTY", "HORDES PURSUE YOU ACROSS THE WHOLE MAP AT EVERY DIFFICULTY");
        var row = Absolute(difficultyPage, Margin, 300);
        row.AddToClassList("eb-row");
        foreach (var (id, body, level) in new[]
        {
            ("EASY", "Slower, weaker zombies. Reduced damage and smaller waves.", 1),
            ("NORMAL", "Aggressive pursuits and balanced waves. The standard survival experience.", 2),
            ("HARD", "Faster, tougher zombies. More frequent attacks and larger hordes.", 3)
        })
        {
            string value = id;
            var card = Enter(new VisualElement { focusable = true }, true);
            card.AddToClassList("eb-card");
            card.style.width = 400; card.style.height = 330; card.style.marginRight = 24;
            ElderboomUi.Interactive(card, () => { Difficulty = value; RefreshSelection(); });
            ElderboomUi.Text(card, "SURVIVAL", "eb-caption");
            ElderboomUi.Text(card, id, "eb-h2");
            card.Add(AccentBar());
            var text = ElderboomUi.Text(card, body, "eb-body"); text.style.height = 84;
            foreach (var stat in new[] { "ZOMBIE SPEED", "DAMAGE TAKEN", "HORDE SIZE" })
            {
                var s = ElderboomUi.Text(card, stat, "eb-caption"); s.style.marginTop = 14;
                ElderboomUi.Meter(card, level, 3);
            }
            row.Add(card);
            difficultyCards.Add((card, id));
        }
        Footer(difficultyPage, ("BACK", () => GoTo(Page.Operators), false), ("DEPLOY", () => { if (!deploying) StartCoroutine(Deploy()); }, true));
    }

    // ---- Settings ----
    void BuildSettings()
    {
        settingsPage = MakePage(uiRoot);
        Header(settingsPage, 0, "SETTINGS", null);
        var tabsHost = Absolute(settingsPage, Margin, 196, width: 980);
        tabsHost.pickingMode = PickingMode.Position;
        ElderboomUi.Tabs(tabsHost, new[] { "GAMEPLAY", "DISPLAY", "AUDIO", "CONTROLS" }, () => settingsTab, i => { settingsTab = i; FillSettings(); });
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("eb-scroll");
        scroll.style.position = Position.Absolute; scroll.style.left = Margin; scroll.style.top = 252; scroll.style.width = 980; scroll.style.bottom = 140;
        settingsPage.Add(scroll);
        settingsBody = scroll.contentContainer;
        var side = Absolute(settingsPage, Margin + 1040, 252, width: 520);
        ElderboomUi.Text(side, "DETAILS", "eb-h3");
        side.Add(AccentBar());
        settingsDescription = ElderboomUi.Text(side, "", "eb-body", "eb-muted");
        Footer(settingsPage, ("BACK", () => { PlayerPrefs.Save(); GoTo(Page.Home); }, false));
    }

    void FillSettings()
    {
        settingsBody.Clear();
        bindingButtons.Clear();
        rebinding = null;
        string first = null;
        Action<string> describe = text => settingsDescription.text = text;
        describe("");
        if (settingsTab == 0)
        {
            PrefSlider("Mouse sensitivity", "How far the view turns for the same mouse movement.", "hdrp.settings.mouse_sensitivity", 1.8f, .25f, 6f, v => v.ToString("0.00"), describe);
            PrefSlider("Field of view", "Horizontal view angle. Wider shows more, narrower magnifies.", "hdrp.settings.field_of_view", 72f, 60f, 100f, v => Mathf.RoundToInt(v) + "°", describe);
        }
        else if (settingsTab == 1)
        {
            var c = ElderboomUi.Setting(settingsBody, "Display mode", "Fullscreen fills the display; windowed uses the selected resolution.", describe);
            ElderboomUi.SelectorControl(c, new[] { "WINDOWED", "FULLSCREEN" }, () => ElderboomDisplaySettings.Fullscreen ? 1 : 0,
                v => SaveDisplay(v == 1, ElderboomDisplaySettings.Resolution, QualityLevel()));
            var resolutions = new string[ElderboomDisplaySettings.Resolutions.Length];
            for (int i = 0; i < resolutions.Length; i++) resolutions[i] = ElderboomDisplaySettings.Resolutions[i].x + " × " + ElderboomDisplaySettings.Resolutions[i].y;
            c = ElderboomUi.Setting(settingsBody, "Resolution", "Window size when not in fullscreen.", describe);
            ElderboomUi.SelectorControl(c, resolutions, () => ElderboomDisplaySettings.Resolution, v => SaveDisplay(ElderboomDisplaySettings.Fullscreen, v, QualityLevel()));
            c = ElderboomUi.Setting(settingsBody, "Graphics quality", "Low favours frame rate, high favours image quality.", describe);
            ElderboomUi.SelectorControl(c, new[] { "PERFORMANCE", "LOW", "MEDIUM", "HIGH" }, () => QualityLevel() == ElderboomDisplaySettings.Performance ? 0 : QualityLevel() + 1,
                v => SaveDisplay(ElderboomDisplaySettings.Fullscreen, ElderboomDisplaySettings.Resolution, v == 0 ? ElderboomDisplaySettings.Performance : v - 1));
            PrefSlider("Brightness", "Overall exposure of the night scenes.", "hdrp.settings.brightness", 1f, .5f, 1.5f, v => Mathf.RoundToInt(v * 100f) + "%", describe);
            PrefSlider("HUD size", "Scale of the in-game HUD.", "hdrp.settings.hud_scale", 1f, .75f, 1.25f, v => Mathf.RoundToInt(v * 100f) + "%", describe);
            c = ElderboomUi.Setting(settingsBody, "Performance overlay", "Shows frame rate, frame time, memory and zombie count in game.", describe);
            ElderboomUi.SwitchControl(c, () => PlayerPrefs.GetInt("hdrp.settings.performance", 0) == 1, v => PlayerPrefs.SetInt("hdrp.settings.performance", v ? 1 : 0));
        }
        else if (settingsTab == 2)
        {
            PrefSlider("Master volume", "Volume of everything.", MasterKey, 1f, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", describe);
            PrefSlider("Music volume", "Menu and ambient music.", MusicKey, .8f, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", describe);
            PrefSlider("Effects volume", "Weapons, zombies, abilities and interface.", EffectsKey, .85f, 0f, 1f, v => Mathf.RoundToInt(v * 100f) + "%", describe);
        }
        else
        {
            foreach (var entry in ElderboomInput.DefaultBindings)
            {
                string id = entry.id;
                var c = ElderboomUi.Setting(settingsBody, entry.label, "Select, then press a key or mouse button. ESC cancels. A key already in use is swapped.", describe);
                var b = ElderboomUi.Button(c, "", () => { rebinding = id; rebindWaitRelease = true; RefreshBindings(); }, false, 220);
                b.style.height = 38;
                bindingButtons[id] = b;
            }
            var reset = ElderboomUi.Button(settingsBody, "RESTORE DEFAULT KEYS", () => { ElderboomInput.RestoreDefaults(); RefreshBindings(); });
            reset.style.marginTop = 22; reset.style.alignSelf = Align.FlexStart;
            RefreshBindings();
        }
        long delay = 0;
        foreach (var row in settingsBody.Children()) { ElderboomUi.AnimateIn(row, delay, true); delay += 25; }
        // Until the pointer moves, the details panel describes the first setting.
        first = settingsTab switch
        {
            0 => "How far the view turns for the same mouse movement.",
            1 => "Fullscreen fills the display; windowed uses the selected resolution.",
            2 => "Volume of everything.",
            _ => "Select, then press a key or mouse button. ESC cancels. A key already in use is swapped."
        };
        describe(first);
    }

    void PrefSlider(string title, string description, string key, float fallback, float min, float max, Func<float, string> format, Action<string> describe)
    {
        var c = ElderboomUi.Setting(settingsBody, title, description, describe);
        ElderboomUi.SliderControl(c, PlayerPrefs.GetFloat(key, fallback), min, max, format, v => { PlayerPrefs.SetFloat(key, v); ApplyAudioPrefs(); });
    }

    static int QualityLevel() => ElderboomDisplaySettings.Quality;

    static void SaveDisplay(bool fullscreen, int resolution, int quality)
    {
        ElderboomDisplaySettings.SaveDisplay(fullscreen, resolution, quality);
        int levels = QualitySettings.names.Length;
        if (levels > 0) QualitySettings.SetQualityLevel(ElderboomDisplaySettings.UnityLevel(quality), true);
        ElderboomPerformanceMode.Set(quality == ElderboomDisplaySettings.Performance);
        if (!Application.isEditor) ElderboomDisplaySettings.ApplySaved();
    }

    void RefreshBindings()
    {
        foreach (var pair in bindingButtons)
        {
            bool waiting = rebinding == pair.Key;
            ElderboomUi.SetText(pair.Value, waiting ? "PRESS A KEY…" : ElderboomInput.Label(pair.Key).ToUpperInvariant());
            ElderboomUi.SetSelected(pair.Value, waiting);
        }
    }

    // Escape goes back a page (or cancels a rebind); rebinding polls the Input Manager.
    void HandleBackAndRebind()
    {
        if (!string.IsNullOrEmpty(rebinding))
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { rebinding = null; RefreshBindings(); return; }
            if (rebindWaitRelease) { if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) rebindWaitRelease = false; return; }
            keyCodes ??= (KeyCode[])Enum.GetValues(typeof(KeyCode));
            foreach (var key in keyCodes)
            {
                if (key == KeyCode.None || key == KeyCode.Escape || key >= KeyCode.JoystickButton0 || !Input.GetKeyDown(key)) continue;
                ElderboomInput.Assign(rebinding, key);
                ElderboomInput.Save();
                rebinding = null;
                RefreshBindings();
                ElderboomUi.Confirm();
                return;
            }
            return;
        }
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (page == Page.Lan) { ElderboomUi.Back(); LeaveLan(); GoTo(Page.Home); }
        else if (page == Page.Operators && LanFlow) { ElderboomUi.Back(); GoTo(Page.Lan); }
        else if (page == Page.Operators || page == Page.Settings) { if (page == Page.Settings) PlayerPrefs.Save(); ElderboomUi.Back(); GoTo(Page.Home); }
        else if (page == Page.Difficulty) { ElderboomUi.Back(); GoTo(Page.Operators); }
    }

    // ---- Loading ----
    void BuildLoading()
    {
        loadingPage = MakePage(uiRoot);
        loadingBackground = ElderboomUi.Fill(loadingPage, Color.black);
        loadingBackground.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
        ElderboomUi.Gradient(loadingPage, new Color(0, 0, 0, 0), new Color(0, 0, 0, .95f), false, 1.6f);
        var block = Absolute(loadingPage, Margin, bottom: 110, width: 1100);
        ElderboomUi.Text(block, "DEPLOYING", "eb-caption");
        deployLabel = ElderboomUi.Text(block, "", "eb-h2");
        block.Add(AccentBar());
        ElderboomUi.Text(block, "TIP", "eb-h3");
        tipLabel = ElderboomUi.Text(block, "", "eb-body");
        tipLabel.style.fontSize = 22; tipLabel.style.marginTop = 6;
        var corner = Absolute(loadingPage, right: Margin, bottom: 118);
        corner.AddToClassList("eb-row"); corner.style.alignItems = Align.Center;
        ElderboomUi.Text(corner, "LOADING", "eb-caption").style.marginRight = 22;
        spinner = new VisualElement { pickingMode = PickingMode.Ignore };
        spinner.style.width = 0; spinner.style.height = 0;
        for (int i = 0; i < 12; i++)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            float a = i * 30f * Mathf.Deg2Rad;
            dot.style.position = Position.Absolute; dot.style.left = Mathf.Cos(a) * 13f - 1.5f; dot.style.top = Mathf.Sin(a) * 13f - 3f;
            dot.style.width = 3; dot.style.height = 6;
            dot.style.backgroundColor = Color.Lerp(new Color(1, 1, 1, .08f), new Color(.84f, .1f, .18f, 1f), (i + 1) / 12f);
            dot.style.rotate = new StyleRotate(new Rotate(new Angle(i * 30f + 90f, AngleUnit.Degree)));
            spinner.Add(dot);
        }
        corner.Add(spinner);
    }

    void GoTo(Page next) { page = next; rebinding = null; ShowPage(); }

    void ShowPage()
    {
        if (uiRoot == null) return;
        ElderboomUi.Show(homePage, page == Page.Home);
        ElderboomUi.Show(operatorsPage, page == Page.Operators);
        ElderboomUi.Show(difficultyPage, page == Page.Difficulty);
        ElderboomUi.Show(settingsPage, page == Page.Settings);
        ElderboomUi.Show(loadingPage, page == Page.Loading);
        ElderboomUi.Show(lanPage, page == Page.Lan);
        VisualElement current = page == Page.Home ? homePage : page == Page.Operators ? operatorsPage : page == Page.Difficulty ? difficultyPage : page == Page.Settings ? settingsPage : page == Page.Lan ? lanPage : null;
        if (page == Page.Lan) { ElderboomLan.StartDiscovery(); lanSignature = null; RefreshLan(); }
        if (page == Page.Operators) FillOperatorDetails();
        else if (page == Page.Difficulty) ShowOperatorPreview(Operator);
        else if (!deploying) SetPreviewActive(false);
        if (page == Page.Settings) FillSettings();
        if (page == Page.Loading)
        {
            var op = ElderboomOperators.Find(Operator);
            deployLabel.text = (op != null ? op.displayName : Operator) + "   ·   " + Difficulty;
            tipLabel.text = tip;
        }
        if (current != null)
        {
            ElderboomUi.ReplayIn(current);
            // Keyboard: start on the first list entry of the page.
            current.schedule.Execute(() => current.Query(className: "eb-menu-item").First()?.Focus()).StartingIn(60);
        }
    }

    void RefreshSelection()
    {
        foreach (var (item, id) in operatorItems) ElderboomUi.SetSelected(item, Operator == id, "eb-menu-item--selected");
        foreach (var (card, id) in difficultyCards) ElderboomUi.SetSelected(card, Difficulty == id, "eb-card--selected");
        if (page == Page.Operators && operatorDetails != null) FillOperatorDetails();
    }

    void UpdateUi()
    {
        if (uiRoot == null) return;
        if (!menuVideoLive && frames != null && frames.Length > 0 && frameIndex != shownFrame)
        {
            shownFrame = frameIndex;
            frameLayer.style.backgroundImage = new StyleBackground(frames[frameIndex]);
        }
        if (page == Page.Lan && Time.unscaledTime >= lanRefreshAt) { lanRefreshAt = Time.unscaledTime + .5f; RefreshLan(); }
        if (page == Page.Loading) spinner.style.rotate = new StyleRotate(new Rotate(new Angle(spinnerAngle, AngleUnit.Degree)));
    }

    void OnDestroy()
    {
        if (panelSettings != null) Destroy(panelSettings);
        if (menuVideoTexture != null) { menuVideoTexture.Release(); Destroy(menuVideoTexture); }
        DestroyPreviewStage();
    }

    // ---- Operator 3D set ----------------------------------------------------------------------
    // Operators with a full-body model are shown in a full-screen 3D set (ElderboomOperatorStage, a night corner of
    // the village) instead of a portrait: hanging from a bar while selected, letting go when the match is deployed.
    // The set replaces the animated backdrop on the Operators and Difficulty pages.
    ElderboomOperatorStage stage;
    VisualElement backdropBase, backdropTint;
    bool deploying, stageFailed;

    bool ShowOperatorPreview(string op)
    {
        if (!ElderboomCharacterBody.Has(op) || stageFailed) { SetPreviewActive(false); return false; }
        if (stage == null)
        {
            stage = ElderboomOperatorStage.Create();
            if (stage == null) { stageFailed = true; return false; }
        }
        SetPreviewActive(true);
        if (!stage.ShowOperator(op)) { SetPreviewActive(false); return false; }
        return true;
    }

    void SetPreviewActive(bool on)
    {
        if (stage != null) stage.SetVisible(on);
        bool showing = on && stage != null;
        var display = showing ? DisplayStyle.None : DisplayStyle.Flex;
        if (backdropBase != null) backdropBase.style.display = display;
        if (frameLayer != null) frameLayer.style.display = display;
        if (backdropTint != null) backdropTint.style.display = display;
    }

    // DEPLOY: an operator with a full-body model lets go of the bar before the loading page.
    IEnumerator Deploy()
    {
        deploying = true;
        if (lanHosting && hostOnline)
        {
            // Online: wait for the join code before loading (a few seconds at most).
            ElderboomLan.StartHostOnline(Difficulty);
            var lan = ElderboomLan.Instance;
            while (lan != null && !lan.HostReady && !lan.Failed) yield return null;
            if (lan == null || !lan.HostReady) { deploying = false; string why = lan != null ? lan.Status : ""; GoTo(Page.Lan); lanStatus.text = why; yield break; }
        }
        else if (lanHosting && !ElderboomLan.StartHost(Difficulty)) { deploying = false; string why = ElderboomLan.Instance != null ? ElderboomLan.Instance.Status : ""; GoTo(Page.Lan); lanStatus.text = why; yield break; }
        if (ElderboomCharacterBody.Has(Operator) && ShowOperatorPreview(Operator))
        {
            ElderboomUi.Show(difficultyPage, false);
            yield return new WaitForSecondsRealtime(.5f); // a beat of the hang before letting go
            float length = stage.Drop();
            if (length > 0f) yield return new WaitForSecondsRealtime(length + .25f);
        }
        SetPreviewActive(false);
        yield return Load();
    }

    // ---- Multiplayer (ElderboomLan) ---------------------------------------------------------
    // ONLINE: the host gets a code (Unity Relay), friends type it from any network. No account needed for them.
    // WI-FI:  games hosted on the same network are listed (or type the host's IP).
    // Either way: pick an operator; the host also picks the difficulty, a joining player deploys at once.
    VisualElement lanPage, lanList;
    Label lanStatus;
    TextField lanAddress, lanCode;
    System.Net.IPEndPoint lanJoin;
    string lanJoinCode;
    bool lanHosting, hostOnline;
    string lanSignature;
    float lanRefreshAt;
    bool LanFlow => lanJoin != null || lanJoinCode != null || lanHosting;

    void BuildLan()
    {
        lanPage = MakePage(uiRoot);
        ElderboomUi.Gradient(lanPage, new Color(0, 0, 0, .9f), new Color(0, 0, 0, 0), true, .55f);
        Header(lanPage, 1, "MULTIPLAYER", "CO-OP ONLINE WITH A CODE, OR ON THE SAME WI-FI");
        var column = Absolute(lanPage, Margin, 250, width: 820);
        Enter(ElderboomUi.MenuItem(column, "01", "HOST ONLINE", () => { lanHosting = true; hostOnline = true; lanJoin = null; lanJoinCode = null; GoTo(Page.Operators); }, "CODE", false,
            () => lanStatus.text = "Start a game and get a code: friends type it to join from anywhere."));
        Enter(ElderboomUi.MenuItem(column, "02", "HOST ON THIS WI-FI", () => { lanHosting = true; hostOnline = false; lanJoin = null; lanJoinCode = null; GoTo(Page.Operators); }, "LAN", false,
            () => lanStatus.text = "Start a game others on this network can join, also once it is under way."));

        var codeRow = new VisualElement();
        codeRow.AddToClassList("eb-row");
        codeRow.style.marginTop = 30; codeRow.style.alignItems = Align.Center;
        lanCode = new TextField { value = "", maxLength = 12 };
        lanCode.style.width = 300; lanCode.style.height = 44; lanCode.style.fontSize = 22; lanCode.style.marginRight = 16;
        codeRow.Add(lanCode);
        ElderboomUi.Button(codeRow, "JOIN WITH CODE", () =>
        {
            string code = (lanCode.value ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) { lanStatus.text = "Type the code shown on the host's screen."; return; }
            lanJoinCode = code; lanJoin = null; lanHosting = false; hostOnline = false;
            GoTo(Page.Operators);
        }, true, 260);
        column.Add(codeRow);

        var found = Enter(ElderboomUi.Text(column, "GAMES ON THIS WI-FI", "eb-caption"));
        found.style.marginTop = 34; found.style.marginBottom = 8;
        lanList = new VisualElement();
        column.Add(lanList);
        var manual = new VisualElement();
        manual.AddToClassList("eb-row");
        manual.style.marginTop = 20; manual.style.alignItems = Align.Center;
        lanAddress = new TextField { value = PlayerPrefs.GetString("elderboom.lan.last_ip", "") };
        lanAddress.style.width = 300; lanAddress.style.height = 44; lanAddress.style.fontSize = 22; lanAddress.style.marginRight = 16;
        manual.Add(lanAddress);
        ElderboomUi.Button(manual, "JOIN BY IP", () =>
        {
            string text = (lanAddress.value ?? "").Trim();
            if (System.Net.IPAddress.TryParse(text, out var ip)) { PlayerPrefs.SetString("elderboom.lan.last_ip", text); ChooseLanGame(new System.Net.IPEndPoint(ip, ElderboomLan.GamePort)); }
            else lanStatus.text = "Type the host's IP address, for example 192.168.1.20 (shown on the host's screen).";
        }, false, 260);
        column.Add(manual);
        lanStatus = ElderboomUi.Text(column, "", "eb-body", "eb-muted");
        lanStatus.style.marginTop = 24;
        Footer(lanPage, ("BACK", () => { LeaveLan(); GoTo(Page.Home); }, false));
    }

    void RefreshLan()
    {
        var lan = ElderboomLan.Instance;
        var games = lan != null ? lan.Games : null;
        string signature = "";
        if (games != null) foreach (var g in games) signature += g.endPoint + "|" + g.name + "|" + g.players + "|" + g.difficulty + ";";
        if (signature != lanSignature)
        {
            lanSignature = signature;
            lanList.Clear();
            if (games == null || games.Count == 0)
                ElderboomUi.Text(lanList, "Searching...  a game hosted on this Wi-Fi appears here.", "eb-body", "eb-muted");
            else
                for (int i = 0; i < games.Count; i++)
                {
                    var g = games[i];
                    var endPoint = g.endPoint;
                    ElderboomUi.MenuItem(lanList, (i + 3).ToString("00"), "JOIN " + g.name.ToUpperInvariant(), () => ChooseLanGame(endPoint),
                        g.players + "P  " + g.difficulty, true, () => lanStatus.text = "Host " + endPoint.Address + "  ·  " + g.players + " playing  ·  " + g.difficulty);
                }
        }
        if (lanStatus != null && string.IsNullOrEmpty(lanStatus.text))
            lanStatus.text = "Online: no VPN or open ports needed.   Wi-Fi: your address is " + ElderboomLan.LocalAddress + ", allow incoming connections if asked.";
    }

    void ChooseLanGame(System.Net.IPEndPoint endPoint)
    {
        lanJoin = endPoint; lanJoinCode = null; lanHosting = false; hostOnline = false;
        GoTo(Page.Operators);
    }

    void LeaveLan()
    {
        lanJoin = null; lanJoinCode = null; lanHosting = false; hostOnline = false;
        ElderboomLan.Shutdown();
    }

    // Connects with the chosen operator, takes the host's difficulty and deploys.
    IEnumerator JoinAndDeploy()
    {
        deploying = true;
        if (lanJoinCode != null) ElderboomLan.JoinOnline(lanJoinCode, Operator);
        else ElderboomLan.Join(lanJoin, Operator);
        var lan = ElderboomLan.Instance;
        while (lan != null && !lan.Accepted && !lan.Failed) yield return null;
        if (lan == null || !lan.Accepted)
        {
            deploying = false;
            string why = lan != null ? lan.Status : "Connection failed.";
            ElderboomLan.Shutdown();
            GoTo(Page.Lan);
            lanStatus.text = why + (lanJoinCode != null ? "  Check the code; is the host in game?" : "  Same Wi-Fi? Is the host in game?");
            yield break;
        }
        ElderboomLan.StopDiscovery();
        Difficulty = lan.HostDifficulty;
        yield return Deploy();
    }

    void DestroyPreviewStage()
    {
        if (stage != null) Destroy(stage.gameObject);
    }
}
