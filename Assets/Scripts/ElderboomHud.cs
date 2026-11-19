using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// In-game HUD built with UI Toolkit (previously IMGUI). The element tree is built
// once; Update only moves elements and rewrites text when a value changes.
// Layout uses the old 1920-wide reference units: the panel scale is the same
// screen-width scale times the HUD size setting.
public sealed class ElderboomHud : MonoBehaviour
{
    static readonly Color HudRed = new Color(0.92f, 0.035f, 0.025f, 0.96f);
    static readonly Color HudText = new Color(0.91f, 0.88f, 0.85f, 1f);
    static readonly Color HudMuted = new Color(0.67f, 0.63f, 0.62f, 1f);
    const int MaxDots = 48, MaxPopups = 16, MaxWedges = 4;

    HDRPSlicePlayer player;
    UIDocument document;
    PanelSettings panel;
    Font font, fontBold;
    Texture2D atlas;
    Sprite healthIcon, abilityIcon, creditIcon;

    VisualElement root, vignette, map, crossDot, healthFill, abilityIconElement, healthBack, compass, compassMarker, weaponIcon;
    Label healthValue;
    readonly List<(Label label, float angle)> compassLabels = new List<(Label, float)>();
    readonly List<(VisualElement tick, float angle)> compassTicks = new List<(VisualElement, float)>();
    string lastIconId; int lastHealth = -1; Texture2D weaponIconTexture;
    const float CompassWidth = 600f, CompassPxPerDeg = 2.4f, HealthBarWidth = 220f;
    Label prompt, hint, scoreTitle, score, kills, doublePoints, instaKill, waveLine, notice, banner, abilityName, abilityStatus, slots, special, credits, syringes, weaponName, ammo;
    readonly List<VisualElement> dots = new List<VisualElement>();
    readonly List<Label> popups = new List<Label>();
    readonly List<VisualElement> wedges = new List<VisualElement>();
    readonly VisualElement[] shards = new VisualElement[4];

    // Last values written, so text is rebuilt only on change.
    int lastScore = -1, lastKills = -1, lastWave = -1, lastRemaining = -1, lastCredits = -1, lastSyringes = -1, lastAmmo = -1, lastReserve = -1, lastAbilitySeconds = -2, lastDouble = -1, lastInsta = -1, lastSlots = -1, lastSpecial = -1;
    string lastNotice, lastAbility, lastWeapon, lastPrompt, lastHint;
    bool lastReloading;

    void Start()
    {
        player = FindAnyObjectByType<HDRPSlicePlayer>();
        atlas = Resources.Load<Texture2D>("Elderboom/bfui_atlas");
        font = Resources.Load<Font>("Elderboom/UI/BarlowSemiCondensed-Regular");
        fontBold = Resources.Load<Font>("Elderboom/UI/BarlowSemiCondensed-SemiBold") ?? font;
        if (atlas != null)
        {
            healthIcon = AtlasSprite(new Rect(766, 454, 64, 64));
            abilityIcon = AtlasSprite(new Rect(614, 378, 64, 64));
            creditIcon = AtlasSprite(new Rect(6, 454, 64, 64));
        }
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.name = "ElderboomHudPanel";
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Elderboom/UI/ElderboomRuntimeTheme");
        panel.sortingOrder = 10;
        // Own child object: a UIDocument on an ancestor would make the menus' documents nest into it.
        var host = new GameObject("HudUi");
        host.transform.SetParent(transform, false);
        document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        Build(document.rootVisualElement);
    }

    void OnDestroy() { if (panel != null) Destroy(panel); }

    Sprite AtlasSprite(Rect source) =>
        Sprite.Create(atlas, new Rect(source.x, atlas.height - source.y - source.height, source.width, source.height), new Vector2(.5f, .5f), 100f);

    // ---- Build ----------------------------------------------------------------------------
    void Build(VisualElement documentRoot)
    {
        var sheet = Resources.Load<StyleSheet>("Elderboom/UI/ElderboomHud");
        if (sheet != null) documentRoot.styleSheets.Add(sheet);
        root = new VisualElement { pickingMode = PickingMode.Ignore };
        root.AddToClassList("hud-root");
        documentRoot.Add(root);

        vignette = Box(root, 0, 0, 0, 0, Color.clear);
        vignette.style.right = 0; vignette.style.bottom = 0; vignette.style.width = StyleKeyword.Auto; vignette.style.height = StyleKeyword.Auto;
        vignette.style.backgroundImage = new StyleBackground(ElderboomFeel.VignetteTexture);
        vignette.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));

        // Minimap: frame, range cross, player marker, pooled zombie dots.
        map = Box(root, 28, 26, 188, 188, new Color(0.04f, 0.05f, 0.06f, 0.55f));
        var frame = new Color(HudRed.r, HudRed.g, HudRed.b, .55f);
        map.style.borderTopWidth = map.style.borderBottomWidth = map.style.borderLeftWidth = map.style.borderRightWidth = 1.5f;
        map.style.borderTopColor = map.style.borderBottomColor = map.style.borderLeftColor = map.style.borderRightColor = frame;
        // 1.5 px so the cross survives the panel scale (1 px rounds away below 1x).
        Box(map, 91.75f, 2.5f, 1.5f, 180, new Color(1, 1, 1, .12f));
        Box(map, 2.5f, 91.75f, 180, 1.5f, new Color(1, 1, 1, .12f));
        Box(map, 90.5f, 90.5f, 4, 4, Color.white);
        Box(map, 91.5f, 83.5f, 2, 6, Color.white);
        for (int i = 0; i < MaxDots; i++) { var d = Box(map, 0, 0, 4, 4, HudRed); d.style.display = DisplayStyle.None; dots.Add(d); }
        map.style.display = DisplayStyle.None; // replaced by the compass
        BuildCompass();

        scoreTitle = Text(root, 13, false, HudMuted); Place(scoreTitle, right: 40, top: 26, width: 170, height: 18); scoreTitle.text = "SCORE";
        score = Text(root, 22, true, HudText); Place(score, right: 40, top: 43, width: 170, height: 28);
        kills = Text(root, 12, false, HudMuted); Place(kills, right: 40, top: 70, width: 170, height: 18);
        scoreTitle.style.unityTextAlign = score.style.unityTextAlign = kills.style.unityTextAlign = TextAnchor.MiddleRight;
        doublePoints = Text(root, 15, true, new Color(1f, .82f, .2f), TextAnchor.MiddleRight); Place(doublePoints, right: 28, top: 108, width: 232, height: 22);
        instaKill = Text(root, 15, true, new Color(1f, .3f, .25f), TextAnchor.MiddleRight); Place(instaKill, right: 28, top: 130, width: 232, height: 22);

        waveLine = Text(root, 14, true, HudMuted, TextAnchor.MiddleCenter); Place(waveLine, left: 0, right: 0, top: 66, height: 22);
        notice = Text(root, 15, false, HudText, TextAnchor.UpperCenter); Place(notice, top: 92, width: 720, height: 52); notice.style.whiteSpace = WhiteSpace.Normal;
        // Interaction prompt (stations, gates, altar, horse...) and controls hint, offered through ElderboomPrompt.
        prompt = Text(root, 17, true, HudText, TextAnchor.MiddleCenter); Place(prompt, width: 520, height: 62); prompt.style.whiteSpace = WhiteSpace.Normal;
        prompt.style.backgroundColor = new Color(0f, 0f, 0f, .55f); prompt.style.borderTopLeftRadius = prompt.style.borderTopRightRadius = prompt.style.borderBottomLeftRadius = prompt.style.borderBottomRightRadius = 4;
        prompt.style.display = DisplayStyle.None;
        hint = Text(root, 12, false, HudMuted); Place(hint, left: 24, bottom: 14, width: 900, height: 22); hint.style.display = DisplayStyle.None;
        banner = Text(root, 68, true, new Color(.92f, .06f, .04f), TextAnchor.MiddleCenter); Place(banner, left: 0, right: 0, height: 90);
        // Score popups: dripping horror lettering (Creepster, OFL), always blood red with a dark outline.
        var horror = Resources.Load<Font>("Elderboom/UI/Creepster-Regular");
        for (int i = 0; i < MaxPopups; i++)
        {
            var p = Text(root, 26, true, PopupRed, TextAnchor.MiddleCenter); p.style.width = 140; p.style.height = 36; p.style.display = DisplayStyle.None;
            if (horror != null) p.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(horror));
            p.style.unityTextOutlineColor = new Color(.08f, 0f, 0f, .9f); p.style.unityTextOutlineWidth = .6f;
            p.style.textShadow = new TextShadow { offset = new Vector2(1.5f, 2f), blurRadius = 3f, color = new Color(0f, 0f, 0f, .85f) };
            popups.Add(p);
        }

        // Bottom left (reference layout): "+ 100" and a thin white bar; ability, credits and syringes small above it.
        abilityName = Text(root, 14, true, HudText); Place(abilityName, left: 44, bottom: 160, width: 500, height: 22);
        abilityStatus = Text(root, 12, false, HudMuted); Place(abilityStatus, left: 44, bottom: 141, width: 500, height: 20);
        abilityIconElement = Icon(root, abilityIcon, Color.white, 44, 110, 22);
        Icon(root, creditIcon, Color.white, 82, 111, 20);
        credits = Text(root, 15, false, HudText); Place(credits, left: 106, bottom: 108, width: 90, height: 26);
        syringes = Text(root, 12, false, HudMuted); Place(syringes, left: 190, bottom: 110, width: 160, height: 22);
        BoxB(root, 49, 64, 4, 18, Color.white);   // plus sign
        BoxB(root, 42, 71, 18, 4, Color.white);
        healthValue = Text(root, 27, true, Color.white); Place(healthValue, left: 68, bottom: 56, width: 70, height: 34);
        healthBack = BoxB(root, 128, 68, HealthBarWidth, 9, new Color(1f, 1f, 1f, .2f));
        healthFill = Box(healthBack, 0, 0, HealthBarWidth, 9, Color.white);
        if (healthIcon == null) { } // the old red heart is no longer drawn
        // Bottom right: white side silhouette of the weapon, then "mag | reserve".
        slots = Text(root, 12, false, HudMuted, TextAnchor.MiddleRight); Place(slots, right: 44, bottom: 168, width: 422, height: 20);
        special = Text(root, 13, false, HudText, TextAnchor.MiddleRight); Place(special, right: 44, bottom: 148, width: 422, height: 20);
        weaponIcon = BoxB(root, 0, 96, 150, 40, Color.clear); weaponIcon.style.left = StyleKeyword.Auto; weaponIcon.style.right = 44;
        weaponIcon.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
        weaponName = Text(root, 13, false, HudMuted, TextAnchor.MiddleRight); Place(weaponName, right: 44, bottom: 100, width: 280, height: 22);
        ammo = Text(root, 30, true, Color.white, TextAnchor.MiddleRight); Place(ammo, right: 44, bottom: 52, width: 300, height: 40);
        ammo.enableRichText = true;

        for (int i = 0; i < MaxWedges; i++)
        {
            var w = Box(root, 0, 0, 128, 32, Color.clear);
            w.style.backgroundImage = new StyleBackground(ElderboomFeel.DamageWedge);
            w.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(new Length(64), new Length(214), 0));
            w.style.display = DisplayStyle.None; wedges.Add(w);
        }
        crossDot = Box(root, 0, 0, 3, 3, Color.white);
        for (int i = 0; i < 4; i++)
        {
            shards[i] = Box(root, 0, 0, 10, 4.2f, Color.white);
            shards[i].style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(new Length(0), new Length(2.1f), 0));
            shards[i].style.display = DisplayStyle.None;
        }
    }

    VisualElement Box(VisualElement parent, float left, float top, float width, float height, Color color)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.AddToClassList("hud-abs");
        e.style.left = left; e.style.top = top; e.style.width = width; e.style.height = height;
        e.style.backgroundColor = color;
        parent.Add(e);
        return e;
    }

    VisualElement BoxB(VisualElement parent, float left, float bottom, float width, float height, Color color)
    {
        var e = Box(parent, left, 0, width, height, color);
        e.style.top = StyleKeyword.Auto; e.style.bottom = bottom;
        return e;
    }

    // Compass band (reference layout): dark band fading at both ends, cardinal letters and 15-degree ticks.
    void BuildCompass()
    {
        compass = Box(root, 0, 24, CompassWidth, 34, Color.clear);
        compass.style.overflow = Overflow.Hidden;
        compass.style.backgroundImage = new StyleBackground(CompassBand());
        compass.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
        for (int a = 0; a < 360; a += 15)
        {
            if (a % 45 == 0) continue;
            var t = Box(compass, 0, 13, 1.5f, a % 45 == 0 ? 10 : 7, new Color(1f, 1f, 1f, .55f));
            compassTicks.Add((t, a));
        }
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        for (int i = 0; i < 8; i++)
        {
            bool north = i == 0;
            var l = Text(compass, north ? 20 : 15, north, north ? Color.white : new Color(.92f, .92f, .92f, .9f), TextAnchor.MiddleCenter);
            l.style.top = 0; l.style.width = 44; l.style.height = 34; l.text = names[i];
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, .6f) };
            compassLabels.Add((l, i * 45f));
        }
        compassMarker = Box(root, 0, 17, 8, 8, Color.white);
        compassMarker.style.rotate = new StyleRotate(new Rotate(new Angle(45f, AngleUnit.Degree)));
    }

    static Texture2D compassBand;
    static Texture2D CompassBand()
    {
        if (compassBand != null) return compassBand;
        compassBand = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 256; x++)
        {
            float t = x / 255f, e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .22f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / .22f));
            compassBand.SetPixel(x, 0, new Color(.05f, .06f, .07f, .5f * e));
        }
        compassBand.Apply();
        return compassBand;
    }

    void UpdateCompass(float screenWidth)
    {
        float left = screenWidth * .5f - CompassWidth * .5f;
        compass.style.left = left;
        compassMarker.style.left = screenWidth * .5f - 4f;
        float heading = player.transform.eulerAngles.y;
        float half = CompassWidth * .5f;
        foreach (var (label, angle) in compassLabels)
        {
            float d = Mathf.DeltaAngle(heading, angle);
            bool on = Mathf.Abs(d) < 120f;
            Show(label, on);
            if (!on) continue;
            label.style.left = half + d * CompassPxPerDeg - 22f;
            label.style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, 118f, Mathf.Abs(d)));
        }
        foreach (var (tick, angle) in compassTicks)
        {
            float d = Mathf.DeltaAngle(heading, angle);
            bool on = Mathf.Abs(d) < 120f;
            Show(tick, on);
            if (!on) continue;
            tick.style.left = half + d * CompassPxPerDeg - .75f;
            tick.style.opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, 118f, Mathf.Abs(d)));
        }
    }

    void UpdateWeaponIcon()
    {
        var def = player.CurrentWeaponDefinition;
        string id = def != null ? def.id : null;
        if (id == lastIconId) return;
        lastIconId = id;
        weaponIconTexture = id != null ? Resources.Load<Texture2D>("Elderboom/UI/WeaponIcons/" + id) : null;
        bool has = weaponIconTexture != null;
        Show(weaponIcon, has); Show(weaponName, !has);
        if (!has) return;
        weaponIcon.style.backgroundImage = new StyleBackground(weaponIconTexture);
        float height = 40f, width = height * weaponIconTexture.width / Mathf.Max(1f, weaponIconTexture.height);
        if (width > 170f) { height *= 170f / width; width = 170f; }
        weaponIcon.style.width = width; weaponIcon.style.height = height;
    }

    VisualElement Icon(VisualElement parent, Sprite sprite, Color tint, float left, float bottom, float size)
    {
        var e = Box(parent, left, 0, size, size, Color.clear);
        e.style.top = StyleKeyword.Auto; e.style.bottom = bottom;
        if (sprite != null) e.style.backgroundImage = new StyleBackground(sprite);
        e.style.unityBackgroundImageTintColor = tint;
        return e;
    }

    Label Text(VisualElement parent, int size, bool bold, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var l = new Label { pickingMode = PickingMode.Ignore };
        l.AddToClassList("hud-text");
        l.style.fontSize = size;
        var f = bold && fontBold != null ? fontBold : font;
        if (f != null) l.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));
        l.style.color = color;
        l.style.unityTextAlign = anchor;
        parent.Add(l);
        return l;
    }

    static void Place(VisualElement e, float? left = null, float? right = null, float? top = null, float? bottom = null, float? width = null, float? height = null)
    {
        if (left.HasValue) e.style.left = left.Value;
        if (right.HasValue) e.style.right = right.Value;
        if (top.HasValue) e.style.top = top.Value;
        if (bottom.HasValue) e.style.bottom = bottom.Value;
        if (width.HasValue) e.style.width = width.Value;
        if (height.HasValue) e.style.height = height.Value;
    }

    static void Show(VisualElement e, bool visible) { var d = visible ? DisplayStyle.Flex : DisplayStyle.None; if (e.style.display != d) e.style.display = d; }

    static void SetText(Label l, string text) { if (l.text != text) l.text = text; }

    // ---- Update ---------------------------------------------------------------------------
    void LateUpdate()
    {
        if (root == null) return;
        var game = ElderboomGame.Active;
        bool visible = game != null && game.IsPlaying && player != null && !player.SettingsOpen && !player.IsDead;
        Show(root, visible);
        if (!visible) return;

        float s = Mathf.Max(.65f, Screen.width / 1920f) * Mathf.Clamp(HudScaleSetting(), 0.75f, 1.25f);
        if (!Mathf.Approximately(panel.scale, s)) panel.scale = s;
        float w = Screen.width / s, h = Screen.height / s;

        UpdateVignette();
        UpdateCompass(w);

        if (player.Score != lastScore) { lastScore = player.Score; score.text = lastScore.ToString("000000"); }
        if (game.Kills != lastKills) { lastKills = game.Kills; kills.text = "KILLS  " + lastKills; }
        int dp = game.DoublePoints ? Mathf.CeilToInt(game.DoublePointsRemaining) : -1;
        if (dp != lastDouble) { lastDouble = dp; Show(doublePoints, dp >= 0); if (dp >= 0) doublePoints.text = "DOUBLE POINTS  " + dp + "s"; }
        int ik = game.InstaKill ? Mathf.CeilToInt(game.InstaKillRemaining) : -1;
        if (ik != lastInsta) { lastInsta = ik; Show(instaKill, ik >= 0); if (ik >= 0) instaKill.text = "INSTA-KILL  " + ik + "s"; }
        instaKill.style.top = dp >= 0 ? 130 : 108;

        if (game.Wave != lastWave || game.Remaining != lastRemaining) { lastWave = game.Wave; lastRemaining = game.Remaining; waveLine.text = "WAVE " + lastWave + "  ·  " + lastRemaining + " HOSTILES"; }
        if (game.Notice != lastNotice) { lastNotice = game.Notice; notice.text = lastNotice; }
        notice.style.left = w * .5f - 360;
        string offer = ElderboomPrompt.Current;
        if (offer != lastPrompt) { lastPrompt = offer; Show(prompt, offer != null); if (offer != null) prompt.text = offer; }
        if (offer != null) { prompt.style.left = w * .5f - 260; prompt.style.top = h * .67f; }
        string line = ElderboomPrompt.CurrentHint;
        if (line != lastHint) { lastHint = line; Show(hint, line != null); if (line != null) hint.text = line; }

        float bannerAge = game.WaveBannerAge;
        Show(banner, bannerAge < 2.6f);
        if (bannerAge < 2.6f)
        {
            float alpha = bannerAge < .3f ? bannerAge / .3f : Mathf.Clamp01((2.6f - bannerAge) / .6f);
            SetText(banner, "WAVE " + game.Wave);
            banner.style.top = h * .26f;
            banner.style.opacity = alpha; // fades text and shadow together
        }
        UpdatePopups(game, s);

        if (game.AbilityName != lastAbility) { lastAbility = game.AbilityName; abilityName.text = lastAbility.ToUpperInvariant(); }
        int recharge = game.AbilityRemaining > 0 ? Mathf.CeilToInt(game.AbilityRemaining) : -1;
        if (recharge != lastAbilitySeconds)
        {
            lastAbilitySeconds = recharge;
            abilityStatus.text = recharge > 0 ? "RECHARGING " + recharge + "s" : player.ActionLabel("shockwave") + " READY · " + player.ActionLabel("cycle_ability") + " SELECT";
        }
        float tilt = game.AbilityCount == 2 ? (game.SelectedAbility == 0 ? -18 : 18) : (game.SelectedAbility - 1) * 18;
        abilityIconElement.style.rotate = new StyleRotate(new Rotate(new Angle(tilt, AngleUnit.Degree)));
        abilityIconElement.style.unityBackgroundImageTintColor = recharge > 0 ? new Color(0.62f, 0.65f, 0.68f, 0.55f) : new Color(0.94f, 0.93f, 0.90f, 0.96f);

        var arsenal = player.GetComponent<ElderboomArsenal>();
        Show(slots, arsenal != null);
        if (arsenal != null)
        {
            int key = arsenal.Count * 10 + arsenal.MaxSlots;
            if (key != lastSlots) { lastSlots = key; slots.text = "WEAPON SLOTS " + arsenal.Count + "/" + arsenal.MaxSlots + "  ·  WHEEL TO SWITCH"; }
        }
        int sp = arsenal != null && arsenal.HasTemporary ? Mathf.CeilToInt(arsenal.TemporaryRemaining) : -1;
        if (sp != lastSpecial) { lastSpecial = sp; Show(special, sp >= 0); if (sp >= 0) special.text = "SPECIAL WEAPON " + sp + "s"; }

        if (game.Credits != lastCredits) { lastCredits = game.Credits; credits.text = lastCredits.ToString(); }
        float health01 = Mathf.Clamp01(player.Health / 100f);
        healthFill.style.width = HealthBarWidth * health01;
        healthFill.style.backgroundColor = player.Health < 35f ? HudRed : Color.white;
        int hp = Mathf.CeilToInt(Mathf.Max(0f, player.Health));
        if (hp != lastHealth) { lastHealth = hp; healthValue.text = hp.ToString(); }
        if (player.Syringes != lastSyringes) { lastSyringes = player.Syringes; syringes.text = "SYRINGES  " + lastSyringes.ToString("00"); }
        if (player.WeaponDisplayName != lastWeapon) { lastWeapon = player.WeaponDisplayName; weaponName.text = lastWeapon; }
        UpdateWeaponIcon();
        if (player.Ammo != lastAmmo || player.ReserveAmmo != lastReserve || player.Reloading != lastReloading)
        {
            lastAmmo = player.Ammo; lastReserve = player.ReserveAmmo; lastReloading = player.Reloading;
            ammo.text = lastReloading ? "<size=60%>RELOADING</size>" : lastAmmo + " <color=#FFFFFF88>|</color> <size=70%>" + lastReserve + "</size>";
        }

        float cx = w * .5f, cy = h * .5f;
        UpdateDamageDirections(cx, cy);
        bool marker = player.HitMarkerVisible;
        Show(crossDot, !marker && !player.IsAiming); // no pointer while aiming down sights
        crossDot.style.left = cx - 1; crossDot.style.top = cy - 1;
        UpdateHitMarker(marker, cx, cy);
    }

    // Red edge pulse on every hit, plus a heartbeat-timed pulse below 35 health.
    void UpdateVignette()
    {
        float low = player.Health < 35f ? 1f - player.Health / 35f : 0f;
        float beat = low > 0f ? (.22f + .3f * Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * 2.7f)), 6f)) * low : 0f;
        float alpha = Mathf.Clamp01(player.DamagePulse * .75f + beat);
        Show(vignette, alpha >= .01f);
        if (alpha >= .01f) vignette.style.unityBackgroundImageTintColor = new Color(.6f, .02f, .02f, alpha);
    }

    void UpdateMinimap(ElderboomGame game)
    {
        int used = 0;
        foreach (var zombie in game.Zombies)
        {
            if (used >= dots.Count) break;
            if (zombie == null || !zombie.IsAlive || !zombie.gameObject.activeInHierarchy) continue;
            Vector3 delta = Quaternion.Inverse(player.transform.rotation) * (zombie.transform.position - player.transform.position);
            if (delta.magnitude > 35) continue;
            bool brute = zombie.Variant == "brute";
            float size = brute ? 7f : 4f;
            var dot = dots[used++];
            Show(dot, true);
            // Map space: 188 px box with a 1.5 px border; centre at (93.5, 91.5) inside it.
            dot.style.left = 93.5f + delta.x * 2.2f - size * .5f;
            dot.style.top = 93.5f - delta.z * 2.2f - size * .5f;
            dot.style.width = size; dot.style.height = size;
            dot.style.backgroundColor = zombie.PlasmaBlinded ? Color.gray : zombie.Variant == "runner" ? new Color(1f, .55f, .1f) : HudRed;
        }
        for (int i = used; i < dots.Count; i++) Show(dots[i], false);
    }

    // "+100" rising from each kill.
    static readonly Color PopupRed = new Color(.86f, .04f, .04f);
    void UpdatePopups(ElderboomGame game, float scale)
    {
        int used = 0;
        var camera = player.ViewCamera;
        if (camera != null)
            foreach (var popup in game.Popups)
            {
                if (used >= popups.Count) break;
                float age = Time.time - popup.at;
                var screen = camera.WorldToScreenPoint(popup.position);
                if (screen.z <= 0f) continue;
                var label = popups[used++];
                Show(label, true);
                SetText(label, "+" + popup.amount);
                label.style.color = PopupRed;
                label.style.left = screen.x / scale - 70;
                label.style.top = (Screen.height - screen.y) / scale - age * 40f - 18;
                label.style.opacity = Mathf.Clamp01(1f - age / 1.2f);
            }
        for (int i = used; i < popups.Count; i++) Show(popups[i], false);
    }

    // Arc markers around the reticle pointing toward recent attackers (0 = in front).
    void UpdateDamageDirections(float cx, float cy)
    {
        int used = 0;
        var hits = player.RecentHits;
        Vector3 forward = player.transform.forward; forward.y = 0f;
        foreach (var hit in hits)
        {
            if (used >= wedges.Count) break;
            float age = Time.time - hit.at;
            if (age > 1.3f) continue;
            Vector3 to = hit.from - player.transform.position; to.y = 0f;
            if (to.sqrMagnitude < .01f) continue;
            var wedge = wedges[used++];
            Show(wedge, true);
            wedge.style.left = cx - 64f; wedge.style.top = cy - 214f;
            wedge.style.rotate = new StyleRotate(new Rotate(new Angle(Vector3.SignedAngle(forward, to, Vector3.up), AngleUnit.Degree)));
            wedge.style.unityBackgroundImageTintColor = new Color(.95f, .08f, .05f, Mathf.Clamp01(1f - age / 1.3f) * .9f);
        }
        for (int i = used; i < wedges.Count; i++) Show(wedges[i], false);
    }

    // Same four falling shards as ElderboomHitMarker.Draw, as rotated bars.
    void UpdateHitMarker(bool visible, float cx, float cy)
    {
        for (int i = 0; i < 4; i++)
        {
            Show(shards[i], visible);
            if (!visible) continue;
            player.HitMarker.Segment(i, Time.time, out var a, out var b, out var color);
            var shard = shards[i];
            shard.style.left = cx + a.x; shard.style.top = cy + a.y - 2.1f;
            shard.style.width = Vector2.Distance(a, b);
            shard.style.rotate = new StyleRotate(new Rotate(new Angle(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, AngleUnit.Degree)));
            shard.style.backgroundColor = color;
        }
    }
    // Read from PlayerPrefs at most twice a second (the settings menu may change it while playing).
    float hudScaleSetting = 1f, hudScaleReadAt = -1f;
    float HudScaleSetting()
    {
        if (Time.unscaledTime >= hudScaleReadAt) { hudScaleReadAt = Time.unscaledTime + .5f; hudScaleSetting = PlayerPrefs.GetFloat("hdrp.settings.hud_scale", 1f); }
        return hudScaleSetting;
    }
}
