using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// UI Toolkit kit for the menus. Components are styled by
// Resources/Elderboom/UI/ElderboomMenu.uss (tokens, typography, states, transitions);
// this file builds them and wires sound, hover, keyboard submit and entrance motion.
public static class ElderboomUi
{
    // ---- Panels ---------------------------------------------------------------------------
    public static VisualElement CreateRoot(GameObject owner, string name, int sortingOrder, out PanelSettings panel, out UIDocument document)
    {
        // A child of the owner, never of another UIDocument: documents under a GameObject
        // that has a UIDocument become nested in that panel.
        var host = new GameObject(name);
        host.transform.SetParent(owner.transform, false);
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.name = name + "Panel";
        // Layout is authored for 1080p and scales with the screen height.
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1920, 1080);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 1f;
        panel.sortingOrder = sortingOrder;
        panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Elderboom/UI/ElderboomRuntimeTheme");
        document = host.AddComponent<UIDocument>();
        document.panelSettings = panel;
        var sheet = Resources.Load<StyleSheet>("Elderboom/UI/ElderboomMenu");
        if (sheet != null) document.rootVisualElement.styleSheets.Add(sheet);
        var root = new VisualElement { name = name };
        root.AddToClassList("eb-root");
        document.rootVisualElement.Add(root);
        document.rootVisualElement.RegisterCallback<NavigationMoveEvent>(_ => ElderboomBloodHighlight.KeyboardNavigation = true, TrickleDown.TrickleDown);
        document.rootVisualElement.RegisterCallback<PointerMoveEvent>(_ => ElderboomBloodHighlight.KeyboardNavigation = false, TrickleDown.TrickleDown);
        return root;
    }

    public static VisualElement Fill(VisualElement parent, Color color)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.AddToClassList("eb-fill");
        e.style.backgroundColor = color;
        parent.Add(e);
        return e;
    }

    // Full-size overlay with a gradient texture (UI Toolkit has no gradient fill).
    public static VisualElement Gradient(VisualElement parent, Color from, Color to, bool horizontal, float power = 1f)
    {
        var e = Fill(parent, Color.clear);
        e.style.backgroundImage = new StyleBackground(GradientTexture(from, to, horizontal, power));
        e.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
        return e;
    }

    static readonly Dictionary<(Color, Color, bool, float), Texture2D> gradients = new Dictionary<(Color, Color, bool, float), Texture2D>();
    public static Texture2D GradientTexture(Color from, Color to, bool horizontal, float power = 1f)
    {
        var key = (from, to, horizontal, power);
        if (gradients.TryGetValue(key, out var cached) && cached != null) return cached;
        const int n = 256;
        var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[n];
        for (int i = 0; i < n; i++)
        {
            float t = Mathf.Pow(i / (n - 1f), power);
            // Vertical textures are stored bottom-up; index 0 must be the top.
            pixels[horizontal ? i : n - 1 - i] = Color.Lerp(from, to, t);
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        gradients[key] = tex;
        return tex;
    }

    // ---- Text -----------------------------------------------------------------------------
    public static Label Text(VisualElement parent, string text, params string[] classes)
    {
        var l = new Label(text) { pickingMode = PickingMode.Ignore };
        l.AddToClassList("eb-label");
        foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
        parent?.Add(l);
        return l;
    }

    // ---- Interactive components -----------------------------------------------------------
    // Big list entry: "01  PLAY" with accent bar, slide and optional tag. Focusable, Enter submits.
    public static VisualElement MenuItem(VisualElement parent, string index, string label, Action onClick, string tag = null, bool small = false, Action onHover = null)
    {
        var item = new VisualElement { focusable = true };
        item.AddToClassList("eb-menu-item");
        AddBlood(item);
        if (small) item.AddToClassList("eb-menu-item--small");
        if (!string.IsNullOrEmpty(index)) Text(item, index, "eb-menu-index");
        Text(item, label, "eb-menu-label");
        if (!string.IsNullOrEmpty(tag)) Text(item, tag, "eb-menu-tag");
        Interactive(item, onClick, onHover);
        parent?.Add(item);
        return item;
    }

    public static Button Button(VisualElement parent, string text, Action onClick, bool primary = false, float width = 0)
    {
        var b = new Button();
        b.AddToClassList("eb-button");
        if (primary) b.AddToClassList("eb-button--primary");
        else AddBlood(b);
        AddText(b, text);
        if (width > 0) b.style.width = width;
        b.clicked += () => { Confirm(); onClick?.Invoke(); };
        b.RegisterCallback<PointerEnterEvent>(_ => Hover());
        parent?.Add(b);
        return b;
    }

    // The Godot menu's animated liquid-blood active state (see ElderboomBloodHighlight).
    public static ElderboomBloodHighlight AddBlood(VisualElement e)
    {
        var fx = new ElderboomBloodHighlight(e);
        e.Insert(0, fx);
        e.AddToClassList("eb-has-blood");
        return fx;
    }

    // Button text lives in a child label (drawn above the liquid layer).
    static void AddText(Button b, string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("eb-button-text");
        b.Add(label);
        b.userData = label;
    }

    public static void SetText(Button b, string text)
    {
        if (b.userData is Label label) label.text = text; else b.text = text;
    }

    // Click, Enter/gamepad submit and hover sound for a plain VisualElement.
    public static void Interactive(VisualElement e, Action onClick, Action onHover = null)
    {
        e.RegisterCallback<ClickEvent>(_ => { Confirm(); onClick?.Invoke(); });
        e.RegisterCallback<NavigationSubmitEvent>(_ => { Confirm(); onClick?.Invoke(); });
        e.RegisterCallback<PointerEnterEvent>(_ => { Hover(); onHover?.Invoke(); });
        e.RegisterCallback<FocusInEvent>(_ => onHover?.Invoke());
    }

    // Text tabs with an accent underline. Returns a refresh action.
    public static Action Tabs(VisualElement parent, string[] labels, Func<int> get, Action<int> set)
    {
        var bar = new VisualElement();
        bar.AddToClassList("eb-tabs");
        parent.Add(bar);
        var tabs = new List<Button>();
        Action refresh = () => { for (int i = 0; i < tabs.Count; i++) SetSelected(tabs[i], get() == i, "eb-tab--selected"); };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var t = new Button();
            t.AddToClassList("eb-tab");
            AddBlood(t);
            AddText(t, labels[i]);
            t.clicked += () => { if (get() == index) return; Confirm(); set(index); refresh(); };
            t.RegisterCallback<PointerEnterEvent>(_ => Hover());
            bar.Add(t);
            tabs.Add(t);
        }
        refresh();
        return refresh;
    }

    // Label on the left, control on the right; reports its description on hover/focus.
    public static VisualElement Setting(VisualElement parent, string label, string description, Action<string> describe)
    {
        var row = new VisualElement();
        row.AddToClassList("eb-setting");
        Text(row, label, "eb-setting-label");
        var control = new VisualElement();
        control.AddToClassList("eb-setting-control");
        row.Add(control);
        if (describe != null)
        {
            row.RegisterCallback<PointerEnterEvent>(_ => describe(description));
            row.RegisterCallback<FocusInEvent>(_ => describe(description));
        }
        parent.Add(row);
        return control;
    }

    public static Slider SliderControl(VisualElement control, float value, float min, float max, Func<float, string> format, Action<float> onChange)
    {
        var slider = new Slider(min, max) { value = value };
        slider.AddToClassList("eb-slider");
        control.Add(slider);
        var shown = Text(control, format(value), "eb-value");
        // Fill bar from the left edge to the dragger.
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("eb-slider-fill");
        slider.Add(fill);
        Action<float> place = v => fill.style.width = Length.Percent(100f * Mathf.InverseLerp(min, max, v));
        place(value);
        slider.RegisterValueChangedCallback(e => { shown.text = format(e.newValue); place(e.newValue); onChange?.Invoke(e.newValue); });
        slider.RegisterCallback<PointerEnterEvent>(_ => Hover());
        return slider;
    }

    public static void SwitchControl(VisualElement control, Func<bool> get, Action<bool> set)
    {
        var text = Text(control, "", "eb-switch-text");
        var track = new VisualElement { focusable = true };
        track.AddToClassList("eb-switch");
        var knob = new VisualElement { pickingMode = PickingMode.Ignore };
        knob.AddToClassList("eb-switch-knob");
        track.Add(knob);
        control.Add(track);
        Action refresh = () => { bool on = get(); SetSelected(track, on, "eb-switch--on"); text.text = on ? "ON" : "OFF"; };
        Interactive(track, () => { set(!get()); refresh(); });
        refresh();
    }

    public static Action SelectorControl(VisualElement control, string[] options, Func<int> get, Action<int> set)
    {
        Label value = null;
        Action refresh = () => value.text = options[Mathf.Clamp(get(), 0, options.Length - 1)];
        Arrow(control, "◀", () => { set((get() + options.Length - 1) % options.Length); refresh(); });
        value = Text(control, "", "eb-selector-value", "eb-bold");
        Arrow(control, "▶", () => { set((get() + 1) % options.Length); refresh(); });
        refresh();
        return refresh;
    }

    static void Arrow(VisualElement parent, string glyph, Action onClick)
    {
        var b = new Button { text = glyph };
        b.AddToClassList("eb-arrow");
        b.clicked += () => { Hover(); onClick(); };
        parent.Add(b);
    }

    public static VisualElement KeyHints(VisualElement parent, params (string key, string text)[] hints)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("eb-row");
        foreach (var (key, text) in hints)
        {
            var hint = new VisualElement { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("eb-hint");
            Text(hint, key, "eb-keycap", "eb-bold");
            Text(hint, text, "eb-hint-text");
            row.Add(hint);
        }
        parent.Add(row);
        return row;
    }

    public static VisualElement Meter(VisualElement parent, int level, int max)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("eb-meter");
        for (int i = 0; i < max; i++)
        {
            var pip = new VisualElement { pickingMode = PickingMode.Ignore };
            pip.AddToClassList("eb-meter-pip");
            if (i < level) pip.AddToClassList("eb-meter-pip--on");
            row.Add(pip);
        }
        parent.Add(row);
        return row;
    }

    // ---- Motion ---------------------------------------------------------------------------
    // Hidden at first, then slides/fades in after delayMs (transition defined in USS).
    public static void AnimateIn(VisualElement e, long delayMs, bool up = false)
    {
        e.RemoveFromClassList("eb-in");
        e.AddToClassList("eb-enter");
        if (up) e.AddToClassList("eb-enter--up");
        e.schedule.Execute(() => e.AddToClassList("eb-in")).StartingIn(Math.Max(16, delayMs));
    }

    // Re-plays entrance motion on every child that has it (used when a page opens).
    public static void ReplayIn(VisualElement page, long stepMs = 45)
    {
        long delay = 0;
        page.Query(className: "eb-enter").ForEach(e =>
        {
            e.RemoveFromClassList("eb-in");
            long d = delay; delay += stepMs;
            e.schedule.Execute(() => e.AddToClassList("eb-in")).StartingIn(16 + d);
        });
    }

    public static void Show(VisualElement e, bool visible)
    {
        if (e == null) return;
        var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (e.style.display != d) e.style.display = d;
    }

    public static void SetSelected(VisualElement e, bool selected, string cls = "eb-button--selected")
    {
        if (selected) e.AddToClassList(cls); else e.RemoveFromClassList(cls);
    }

    // ---- Sound ----------------------------------------------------------------------------
    // Short synthesized cues on a 2D source that ignores AudioListener.pause, so the pause
    // menu is not silent. Volume follows the effects setting.
    static AudioSource source;
    static AudioClip hoverClip, confirmClip, backClip;
    static float lastHover;

    public static void Hover()
    {
        if (Time.unscaledTime - lastHover < .045f) return;
        lastHover = Time.unscaledTime;
        Play(hoverClip ??= Tone("ui_hover", .022f, 2300f, 2600f, .5f), .16f);
    }
    public static void Confirm() => Play(confirmClip ??= Tone("ui_confirm", .09f, 880f, 1320f, .9f), .3f);
    public static void Back() => Play(backClip ??= Tone("ui_back", .08f, 900f, 560f, .9f), .26f);
    public static void Click() => Confirm();

    static void Play(AudioClip clip, float volume)
    {
        if (source == null)
        {
            var go = new GameObject("ElderboomUiAudio") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.playOnAwake = false;
        }
        source.PlayOneShot(clip, volume * ElderboomAudio.Effects);
    }

    // A pitch sweep with a fast attack and exponential decay, plus a soft octave.
    static AudioClip Tone(string name, float seconds, float fromHz, float toHz, float body)
    {
        const int rate = 44100;
        int count = Mathf.CeilToInt(seconds * rate);
        var data = new float[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count;
            float hz = Mathf.Lerp(fromHz, toHz, t);
            phase += 2 * Math.PI * hz / rate;
            float envelope = Mathf.Min(1f, i / (rate * .002f)) * Mathf.Exp(-t * 5.5f);
            data[i] = (float)(Math.Sin(phase) * .8 + Math.Sin(phase * .5) * .2 * body) * envelope * .6f;
        }
        var clip = AudioClip.Create(name, count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
