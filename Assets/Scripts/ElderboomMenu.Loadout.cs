using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Weapon customization page (Battlefield-style loadout screen): the weapon in 3D (ElderboomLoadoutStage), a box per
// attachment slot with a callout line to where it mounts, the choices for the selected slot on the left and the
// weapon's stats bottom right. Every change is saved at once (ElderboomWeaponMods); the weapon stations and the
// test-range dispenser then hand out the customized weapon.
public sealed partial class ElderboomMenu
{
    VisualElement loadoutPage, loadoutCallouts, loadoutOptions, loadoutStats;
    Label loadoutTitle, loadoutName, loadoutBadge, loadoutOptionsTitle, loadoutDescription;
    ScrollView loadoutList;
    Action refreshLoadoutTabs;
    readonly Dictionary<string, VisualElement> slotBoxes = new Dictionary<string, VisualElement>();
    ElderboomLoadoutStage loadoutStage;
    Page loadoutReturn = Page.Home;
    string loadoutWeapon, loadoutSlot = ElderboomWeaponMods.Optic;
    Dictionary<string, string> loadoutSet = new Dictionary<string, string>();
    bool loadoutDragging;
    float loadoutDragX;

    static readonly Color SlotLine = new Color(1f, 1f, 1f, .38f);
    static readonly Color Accent = new Color(.84f, .1f, .18f);
    static readonly Color Positive = new Color(.42f, .86f, .48f);
    static readonly Color Negative = new Color(.95f, .33f, .3f);

    // Box positions on the 1920x1080 layout (the weapon sits right of centre, muzzle to the right).
    static readonly Dictionary<string, Vector2> SlotPositions = new Dictionary<string, Vector2>
    {
        { ElderboomWeaponMods.Optic, new Vector2(800, 170) },
        { ElderboomWeaponMods.Top, new Vector2(1170, 130) },
        { ElderboomWeaponMods.Muzzle, new Vector2(1650, 300) },
        { ElderboomWeaponMods.Right, new Vector2(1490, 590) },
        { ElderboomWeaponMods.Under, new Vector2(950, 700) },
        { ElderboomWeaponMods.Charm, new Vector2(690, 700) },
        { ElderboomWeaponMods.Stock, new Vector2(600, 480) },
        { ElderboomWeaponMods.Grip, new Vector2(690, 850) },
        { ElderboomWeaponMods.Mag, new Vector2(950, 850) },
        { ElderboomWeaponMods.Slide, new Vector2(1170, 130) }, // pistols only: never with a top accessory
    };
    const float BoxW = 176, BoxH = 116;

    List<string> LoadoutWeapons()
    {
        var list = new List<string>();
        foreach (var id in ElderboomWeaponMods.CustomizableIds) if (ElderboomWeapons.Find(id) != null) list.Add(id);
        return list;
    }

    static string FirstSlot(string weaponId)
    {
        foreach (var s in ElderboomWeaponMods.Slots) if (ElderboomWeaponMods.HasSlot(weaponId, s)) return s;
        return ElderboomWeaponMods.Charm;
    }

    void OpenLoadout(Page from)
    {
        loadoutReturn = from;
        var weapons = LoadoutWeapons();
        if (weapons.Count == 0) return;
        if (loadoutWeapon == null || !weapons.Contains(loadoutWeapon)) loadoutWeapon = weapons[0];
        if (!ElderboomWeaponMods.HasSlot(loadoutWeapon, loadoutSlot)) loadoutSlot = FirstSlot(loadoutWeapon);
        GoTo(Page.Loadout);
    }

    void CloseLoadout()
    {
        ElderboomUi.Back();
        GoTo(loadoutReturn);
    }

    void BuildLoadout()
    {
        loadoutPage = MakePage(uiRoot);

        // Drag anywhere on the free area to turn the weapon.
        var drag = new VisualElement { pickingMode = PickingMode.Position };
        drag.AddToClassList("eb-fill");
        loadoutPage.Add(drag);
        drag.RegisterCallback<PointerDownEvent>(e => { loadoutDragging = true; loadoutDragX = e.position.x; drag.CapturePointer(e.pointerId); });
        drag.RegisterCallback<PointerMoveEvent>(e => { if (!loadoutDragging) return; loadoutStage?.Drag(-(e.position.x - loadoutDragX)); loadoutDragX = e.position.x; });
        drag.RegisterCallback<PointerUpEvent>(e => { loadoutDragging = false; drag.ReleasePointer(e.pointerId); });

        ElderboomUi.Gradient(loadoutPage, new Color(0, 0, 0, .78f), new Color(0, 0, 0, 0), true, .55f).pickingMode = PickingMode.Ignore;

        loadoutCallouts = new VisualElement { pickingMode = PickingMode.Ignore };
        loadoutCallouts.AddToClassList("eb-fill");
        loadoutCallouts.generateVisualContent += DrawCallouts;
        loadoutPage.Add(loadoutCallouts);

        // Top bar: ‹  |  CUSTOMIZE A5
        var top = Absolute(loadoutPage, 56, 30, width: 900);
        top.AddToClassList("eb-row");
        top.style.alignItems = Align.Center;
        var back = ElderboomUi.Text(top, "‹", "eb-bold");
        back.style.fontSize = 44; back.style.paddingLeft = 8; back.style.paddingRight = 22; back.pickingMode = PickingMode.Position;
        ElderboomUi.Interactive(back, CloseLoadout);
        var divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.style.width = 1; divider.style.height = 40; divider.style.backgroundColor = new Color(1, 1, 1, .25f); divider.style.marginRight = 34;
        top.Add(divider);
        loadoutTitle = ElderboomUi.Text(top, "CUSTOMIZE", "eb-bold");
        loadoutTitle.style.fontSize = 32; loadoutTitle.style.letterSpacing = 5;

        // Weapon choice, big name and status.
        var left = Absolute(loadoutPage, Margin, 108, width: 640);
        // Weapon picker: one chip per customizable weapon, wrapping (rifles, SMGs, shotguns, pistols).
        var chips = new VisualElement { pickingMode = PickingMode.Ignore };
        chips.AddToClassList("eb-row"); chips.style.flexWrap = Wrap.Wrap; chips.style.width = 720;
        left.Add(chips);
        var chipList = new List<(VisualElement chip, string id)>();
        foreach (var id in LoadoutWeapons())
        {
            string w = id;
            var chip = new VisualElement { focusable = true, pickingMode = PickingMode.Position };
            chip.style.marginRight = 6; chip.style.marginBottom = 6;
            chip.style.paddingLeft = 12; chip.style.paddingRight = 12; chip.style.paddingTop = 5; chip.style.paddingBottom = 5;
            var t = ElderboomUi.Text(chip, ElderboomWeapons.Find(w).name, "eb-bold");
            t.style.fontSize = 14; t.style.letterSpacing = 2;
            ElderboomUi.Interactive(chip, () => { if (loadoutWeapon == w) return; loadoutWeapon = w; loadoutSlot = FirstSlot(w); FillLoadout(true); });
            chips.Add(chip);
            chipList.Add((chip, w));
        }
        refreshLoadoutTabs = () =>
        {
            foreach (var (chip, id) in chipList)
            {
                bool on = id == loadoutWeapon;
                chip.style.backgroundColor = on ? new Color(.3f, .04f, .07f, .9f) : new Color(.03f, .03f, .035f, .7f);
                SetBorder(chip, 1, on ? Accent : new Color(1, 1, 1, .12f));
            }
        };
        loadoutName = ElderboomUi.Text(left, "", "eb-bold");
        loadoutName.style.fontSize = 88; loadoutName.style.letterSpacing = 4; loadoutName.style.marginTop = 6; loadoutName.style.marginBottom = -6;
        loadoutBadge = ElderboomUi.Text(left, "", "eb-bold");
        loadoutBadge.style.alignSelf = Align.FlexStart;
        loadoutBadge.style.fontSize = 18; loadoutBadge.style.letterSpacing = 3;
        loadoutBadge.style.paddingLeft = 14; loadoutBadge.style.paddingRight = 14; loadoutBadge.style.paddingTop = 5; loadoutBadge.style.paddingBottom = 5;
        SetBorder(loadoutBadge, 2, new Color(1, 1, 1, .3f));

        // Choices for the selected slot.
        loadoutOptions = Absolute(loadoutPage, Margin, 420, width: 470);
        loadoutOptionsTitle = ElderboomUi.Text(loadoutOptions, "", "eb-h3");
        loadoutOptionsTitle.style.marginBottom = 8;
        loadoutList = new ScrollView(ScrollViewMode.Vertical);
        loadoutList.style.maxHeight = 340;
        loadoutList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        loadoutOptions.Add(loadoutList);
        loadoutDescription = ElderboomUi.Text(loadoutOptions, "", "eb-body", "eb-muted");
        loadoutDescription.style.marginTop = 12; loadoutDescription.style.whiteSpace = WhiteSpace.Normal; loadoutDescription.style.fontSize = 15;

        // Slot boxes.
        foreach (var slot in ElderboomWeaponMods.Slots)
        {
            string s = slot;
            var pos = SlotPositions[slot];
            var box = new VisualElement { focusable = true, pickingMode = PickingMode.Position };
            box.style.position = Position.Absolute;
            box.style.left = pos.x; box.style.top = pos.y; box.style.width = BoxW; box.style.height = BoxH;
            box.style.backgroundColor = new Color(.02f, .022f, .026f, .82f);
            SetBorder(box, 1, new Color(1, 1, 1, .16f));
            box.style.paddingLeft = 12; box.style.paddingRight = 10; box.style.paddingTop = 8; box.style.paddingBottom = 8;
            ElderboomUi.Interactive(box, () => { loadoutSlot = s; FillLoadout(false); }, () => box.style.backgroundColor = new Color(.06f, .05f, .055f, .9f));
            box.RegisterCallback<PointerLeaveEvent>(_ => box.style.backgroundColor = new Color(.02f, .022f, .026f, .82f));
            loadoutPage.Add(box);
            slotBoxes[slot] = box;
        }

        // Stats, bottom right.
        loadoutStats = Absolute(loadoutPage, right: 96, bottom: 132, width: 640);
        loadoutStats.AddToClassList("eb-row");

        Footer(loadoutPage, ("RESET", () => { ElderboomUi.Confirm(); ElderboomWeaponMods.Reset(loadoutWeapon); FillLoadout(true); }, false),
            ("DONE", CloseLoadout, true));
    }

    static void SetBorder(VisualElement e, float width, Color color)
    {
        e.style.borderLeftWidth = width; e.style.borderRightWidth = width; e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
        e.style.borderLeftColor = color; e.style.borderRightColor = color; e.style.borderTopColor = color; e.style.borderBottomColor = color;
    }

    void ShowLoadoutPage(bool on)
    {
        if (!on)
        {
            if (loadoutStage != null) loadoutStage.SetVisible(false);
            return;
        }
        if (loadoutStage == null) loadoutStage = ElderboomLoadoutStage.Create();
        loadoutStage.SetVisible(true);
        // The studio set replaces the menu backdrop.
        if (backdropBase != null) backdropBase.style.display = DisplayStyle.None;
        if (frameLayer != null) frameLayer.style.display = DisplayStyle.None;
        if (backdropTint != null) backdropTint.style.display = DisplayStyle.None;
        FillLoadout(true);
    }

    // rebuild: the weapon or the whole loadout changed (not just the selected slot).
    void FillLoadout(bool rebuild)
    {
        if (loadoutPage == null || loadoutWeapon == null) return;
        var def = ElderboomWeapons.Find(loadoutWeapon);
        if (rebuild)
        {
            loadoutSet = ElderboomWeaponMods.Load(loadoutWeapon);
            loadoutStage?.ResetView();
        }
        refreshLoadoutTabs?.Invoke();
        string shortName = def != null ? def.name.Split(' ')[0] : loadoutWeapon.ToUpperInvariant();
        loadoutTitle.text = "CUSTOMIZE  " + shortName;
        loadoutName.text = def != null ? def.name : loadoutWeapon.ToUpperInvariant();
        bool modified = ElderboomWeaponMods.IsModified(loadoutWeapon);
        loadoutBadge.text = modified ? "CUSTOM LOADOUT EQUIPPED" : "STANDARD ISSUE";
        loadoutBadge.style.color = modified ? Positive : new Color(.75f, .75f, .75f);
        SetBorder(loadoutBadge, 2, modified ? new Color(Positive.r, Positive.g, Positive.b, .8f) : new Color(1, 1, 1, .25f));
        loadoutBadge.style.backgroundColor = modified ? new Color(.1f, .3f, .12f, .45f) : new Color(0, 0, 0, .3f);

        if (loadoutStage != null) loadoutStage.Show(loadoutWeapon, loadoutSet);
        FillSlotBoxes();
        FillOptions();
        FillStats();
        loadoutCallouts.MarkDirtyRepaint();
    }

    void FillSlotBoxes()
    {
        foreach (var slot in ElderboomWeaponMods.Slots)
        {
            var box = slotBoxes[slot];
            box.Clear();
            bool available = ElderboomWeaponMods.HasSlot(loadoutWeapon, slot);
            box.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (!available) continue;
            bool selected = slot == loadoutSlot;
            SetBorder(box, selected ? 2 : 1, selected ? new Color(1, 1, 1, .9f) : new Color(1, 1, 1, .16f));
            var mod = ElderboomWeaponMods.FindMod(ElderboomWeaponMods.Get(loadoutSet, slot));
            int choices = ElderboomWeaponMods.Options(loadoutWeapon, slot).Count - 1;
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("eb-row"); head.style.justifyContent = Justify.SpaceBetween;
            var label = ElderboomUi.Text(head, ElderboomWeaponMods.SlotName(slot), "eb-bold");
            label.style.fontSize = 12; label.style.letterSpacing = 2;
            label.style.color = mod == null ? new Color(1f, .45f, .35f) : new Color(.8f, .82f, .84f);
            var count = ElderboomUi.Text(head, choices.ToString(), "eb-caption"); count.style.fontSize = 12;
            box.Add(head);
            if (mod == null)
            {
                var plus = ElderboomUi.Text(box, "+", "eb-bold");
                plus.style.fontSize = 46; plus.style.unityTextAlign = TextAnchor.MiddleCenter; plus.style.marginTop = -2;
                plus.style.color = new Color(1, 1, 1, .85f);
            }
            else
            {
                string[] words = mod.name.Split(' ');
                var code = ElderboomUi.Text(box, words[0], "eb-bold");
                code.style.fontSize = 32; code.style.marginTop = 6; code.style.letterSpacing = 2;
                var rest = ElderboomUi.Text(box, words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : "", "eb-caption");
                rest.style.fontSize = 12; rest.style.whiteSpace = WhiteSpace.Normal;
            }
        }
    }

    void FillOptions()
    {
        loadoutList.Clear();
        loadoutOptionsTitle.text = ElderboomWeaponMods.SlotName(loadoutSlot);
        string current = ElderboomWeaponMods.Get(loadoutSet, loadoutSlot);
        var currentMod = ElderboomWeaponMods.FindMod(current);
        loadoutDescription.text = currentMod != null ? currentMod.description : "Nothing mounted.";
        foreach (var mod in ElderboomWeaponMods.Options(loadoutWeapon, loadoutSlot))
        {
            var m = mod;
            bool on = (m == null && current == null) || (m != null && m.id == current);
            var row = new VisualElement { focusable = true, pickingMode = PickingMode.Position };
            row.style.marginBottom = 6; row.style.paddingLeft = 16; row.style.paddingRight = 14; row.style.paddingTop = 9; row.style.paddingBottom = 9;
            row.style.backgroundColor = on ? new Color(.3f, .04f, .07f, .88f) : new Color(.03f, .03f, .035f, .78f);
            row.style.borderLeftWidth = 3; row.style.borderLeftColor = on ? Accent : new Color(1, 1, 1, .08f);
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("eb-row"); line.style.justifyContent = Justify.SpaceBetween; line.style.alignItems = Align.Center;
            var name = ElderboomUi.Text(line, m != null ? m.name : "NONE", "eb-bold");
            name.style.fontSize = 19; name.style.letterSpacing = 2;
            if (on) ElderboomUi.Text(line, "EQUIPPED", "eb-caption").style.color = Positive;
            row.Add(line);
            var deltas = Deltas(m);
            if (deltas.Count > 0)
            {
                var chips = new VisualElement { pickingMode = PickingMode.Ignore };
                chips.AddToClassList("eb-row"); chips.style.flexWrap = Wrap.Wrap; chips.style.marginTop = 3;
                foreach (var (text, good) in deltas)
                {
                    var c = ElderboomUi.Text(chips, text, "eb-caption");
                    c.style.fontSize = 12; c.style.marginRight = 12; c.style.color = good ? Positive : Negative;
                }
                row.Add(chips);
            }
            ElderboomUi.Interactive(row, () =>
            {
                if (m == null) loadoutSet.Remove(loadoutSlot); else loadoutSet[loadoutSlot] = m.id;
                ElderboomWeaponMods.Save(loadoutWeapon, loadoutSet);
                FillLoadout(false);
            }, () => loadoutDescription.text = m != null ? m.description : "Nothing mounted.");
            loadoutList.Add(row);
        }
    }

    static List<(string, bool)> Deltas(ElderboomWeaponMods.Mod m)
    {
        var list = new List<(string, bool)>();
        if (m == null) return list;
        if (m.slot == ElderboomWeaponMods.Optic) list.Add((m.zoom.ToString(m.zoom % 1f == 0f ? "0" : "0.0#", System.Globalization.CultureInfo.InvariantCulture) + "X ZOOM", true));
        void Add(int v, string n) { if (v != 0) list.Add(((v > 0 ? "+" : "−") + Mathf.Abs(v) + " " + n, v > 0)); }
        Add(m.hipfire, "HIPFIRE"); Add(m.precision, "PRECISION"); Add(m.control, "CONTROL"); Add(m.mobility, "MOBILITY");
        if (m.mobility != 0) list.Add(((m.mobility < 0 ? "+" : "−") + Mathf.RoundToInt(Mathf.Abs(m.mobility) * 1.5f) + "% ADS TIME", m.mobility > 0));
        if (m.suppressor || m.id == "SC01" || m.id == "SL01") list.Add(("MUFFLED SHOT", true));
        if (m.capacity > 0) list.Add((m.capacity + " ROUNDS", true));
        if (m.light == "flash") list.Add(("WEAPON LIGHT", true));
        else if (m.light != null) list.Add(("VISIBLE LASER", true));
        if (m.slot == ElderboomWeaponMods.Charm) list.Add(("COSMETIC", true));
        return list;
    }

    void FillStats()
    {
        loadoutStats.Clear();
        var def = ElderboomWeapons.Find(loadoutWeapon);
        var now = ElderboomWeaponMods.StatsOf(loadoutWeapon, loadoutSet);
        var stock = ElderboomWeaponMods.StatsOf(loadoutWeapon, ElderboomWeaponMods.Defaults(loadoutWeapon));

        var numbers = new VisualElement { pickingMode = PickingMode.Ignore };
        numbers.style.width = 170;
        loadoutStats.Add(numbers);
        void Number(string label, string value)
        {
            ElderboomUi.Text(numbers, label, "eb-caption").style.fontSize = 15;
            var v = ElderboomUi.Text(numbers, value, "eb-bold");
            v.style.fontSize = 38; v.style.marginTop = -6; v.style.marginBottom = 2;
        }
        Number("DMG", def != null ? Mathf.RoundToInt(def.damage).ToString() : "-");
        Number("ROF", def != null && def.fireInterval > 0f ? Mathf.RoundToInt(60f / def.fireInterval).ToString() : "-");
        int cap = ElderboomWeaponMods.CapacityOf(loadoutSet);
        Number("MAG", def != null ? (cap > 0 ? cap : def.capacity).ToString() : "-");
        // Aim time with this loadout (slower with heavy optics and long cans).
        float ads = ElderboomWeaponMods.AimTimeFor(loadoutWeapon, ElderboomWeaponMods.MobilityDelta(loadoutSet));
        float adsStock = ElderboomWeaponMods.AimTimeFor(loadoutWeapon, ElderboomWeaponMods.MobilityDelta(ElderboomWeaponMods.Defaults(loadoutWeapon)));
        Number("ADS", Mathf.RoundToInt(ads * 1000f) + " MS");
        var adsLabel = numbers[numbers.childCount - 1];
        if (Mathf.Abs(ads - adsStock) > .0005f) adsLabel.style.color = ads < adsStock ? Positive : Negative;

        var bars = new VisualElement { pickingMode = PickingMode.Ignore };
        bars.style.flexGrow = 1; bars.style.marginTop = 6;
        loadoutStats.Add(bars);
        Bar(bars, "HIPFIRE", now.hipfire, stock.hipfire);
        Bar(bars, "PRECISION", now.precision, stock.precision);
        Bar(bars, "CONTROL", now.control, stock.control);
        Bar(bars, "MOBILITY", now.mobility, stock.mobility);
    }

    static void Bar(VisualElement parent, string label, int value, int stock)
    {
        var head = new VisualElement { pickingMode = PickingMode.Ignore };
        head.AddToClassList("eb-row"); head.style.justifyContent = Justify.SpaceBetween; head.style.marginTop = 10;
        var l = ElderboomUi.Text(head, label, "eb-bold"); l.style.fontSize = 19; l.style.letterSpacing = 2;
        var v = ElderboomUi.Text(head, value.ToString("000"), "eb-bold"); v.style.fontSize = 19;
        if (value != stock) v.style.color = value > stock ? Positive : Negative;
        parent.Add(head);
        var track = new VisualElement { pickingMode = PickingMode.Ignore };
        track.style.height = 6; track.style.marginTop = 6; track.style.backgroundColor = new Color(1, 1, 1, .14f);
        track.style.flexDirection = FlexDirection.Row;
        float a = Mathf.Min(value, stock), b = Mathf.Max(value, stock);
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.style.width = Length.Percent(a); fill.style.backgroundColor = new Color(1, 1, 1, .9f);
        track.Add(fill);
        if (b > a)
        {
            var delta = new VisualElement { pickingMode = PickingMode.Ignore };
            delta.style.width = Length.Percent(b - a); delta.style.backgroundColor = value > stock ? Positive : Negative;
            track.Add(delta);
        }
        parent.Add(track);
        // Marker under the bar at the current value (like the reference's triangle).
        var marker = new VisualElement { pickingMode = PickingMode.Ignore };
        marker.style.width = 7; marker.style.height = 7; marker.style.marginTop = 4; marker.style.marginLeft = -3.5f;
        marker.style.left = Length.Percent(value); marker.style.backgroundColor = new Color(1, 1, 1, .8f);
        marker.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
        parent.Add(marker);
    }

    void DrawCallouts(MeshGenerationContext ctx)
    {
        if (loadoutStage == null || page != Page.Loadout) return;
        var p = ctx.painter2D;
        p.lineWidth = 1.4f;
        foreach (var slot in ElderboomWeaponMods.Slots)
        {
            if (!loadoutStage.Sockets.TryGetValue(slot, out var socket) || socket == null) continue;
            if (!loadoutStage.ToPanel(socket.position, out var target)) continue;
            if (!slotBoxes.TryGetValue(slot, out var box)) continue;
            var r = box.layout;
            if (float.IsNaN(r.width) || r.width <= 0f) continue;
            // From the box edge nearest the socket.
            var from = new Vector2(Mathf.Clamp(target.x, r.xMin, r.xMax), Mathf.Clamp(target.y, r.yMin, r.yMax));
            if (r.Contains(target)) continue;
            bool selected = slot == loadoutSlot;
            p.strokeColor = selected ? new Color(1, 1, 1, .85f) : SlotLine;
            p.BeginPath(); p.MoveTo(from); p.LineTo(target); p.Stroke();
            p.fillColor = selected ? Color.white : new Color(1, 1, 1, .7f);
            p.BeginPath(); p.Arc(target, selected ? 4.5f : 3.5f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree)); p.Fill();
        }
    }

    void UpdateLoadout()
    {
        if (page == Page.Loadout && loadoutCallouts != null) loadoutCallouts.MarkDirtyRepaint();
    }
}
