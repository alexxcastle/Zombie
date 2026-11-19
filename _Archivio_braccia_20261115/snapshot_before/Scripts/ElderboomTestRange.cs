using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Test range (Assets/Scenes/Elderboom_TestRange.unity): no waves, ammo never runs out.
// 7 / 8 / 9 (or F1 / F2 / F3) switch operator: Tactical, Eli, Soph.
// Weapon dispenser by the firing line: interact near it (or B anywhere) to pick any weapon of the catalog.
// +: hold editor (ElderboomGripEditor, available in matches too).
public class ElderboomTestRange : MonoBehaviour
{
    public static bool Active;
    public static bool StandaloneOnly => Active && !Application.isEditor &&
        !Application.CanStreamedLevelBeLoaded("ZombieTest_Menu");
    public string weapon = "shotgun";
    public Vector3 dispenserPosition = new Vector3(3.2f, 0f, 0.6f);
    // Reflection bay: dark booth with coloured lights, left of the firing line, to judge weapon materials.
    public Vector3 lightBayPosition = new Vector3(-5.2f, 0f, 1.2f);
    Transform lightBay, bayOrbit;
    string previousStart;
    Transform dispenser;
    bool menuOpen;
    Vector2 scroll;

    static readonly (KeyCode a, KeyCode b, string id)[] Switch =
    {
        (KeyCode.Alpha7, KeyCode.None, ElderboomIds.Tactical), // F1 opens the cheat menu
        (KeyCode.Alpha8, KeyCode.F2, ElderboomIds.Eli),
        (KeyCode.Alpha9, KeyCode.F3, ElderboomIds.Soph),
    };

    void Awake()
    {
        Active = true;
        previousStart = ElderboomArsenal.StartingWeapon;
        ElderboomArsenal.StartingWeapon = weapon;
    }

    void OnDestroy()
    {
        Active = false;
        ElderboomArsenal.StartingWeapon = previousStart;
    }

    void Start() { BuildDispenser(); BuildLightBay(); }

    void BuildDispenser()
    {
        dispenser = new GameObject("Weapon_Dispenser").transform;
        dispenser.SetParent(transform, false);
        dispenser.localPosition = dispenserPosition;
        dispenser.localRotation = Quaternion.Euler(0f, 62f, 0f); // screen side (-Z) towards the firing line
        var shader = Shader.Find("HDRP/Lit");
        Material Mat(Color c, float smooth, Color emit)
        {
            var m = new Material(shader); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            if (emit != Color.black) { m.EnableKeyword("_EMISSIVE_COLOR_MAP"); m.SetColor("_EmissiveColor", emit); }
            return m;
        }
        var body = Mat(new Color(.12f, .13f, .14f), .45f, Color.black);
        var screen = Mat(new Color(.05f, .02f, .02f), .8f, new Color(6f, .35f, .2f));
        void Part(string n, Vector3 p, Vector3 s, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(dispenser, false);
            g.transform.localPosition = p; g.transform.localScale = s; g.GetComponent<Renderer>().sharedMaterial = m;
        }
        Part("Cabinet", new Vector3(0f, .9f, 0f), new Vector3(.9f, 1.8f, .55f), body);
        Part("Screen", new Vector3(0f, 1.25f, -.28f), new Vector3(.62f, .42f, .02f), screen);
        Part("Tray", new Vector3(0f, .55f, -.32f), new Vector3(.7f, .08f, .18f), body);
        var label = new GameObject("Label").AddComponent<TextMesh>();
        label.transform.SetParent(dispenser, false); label.transform.localPosition = new Vector3(0f, 1.25f, -.30f);
        label.transform.localRotation = Quaternion.identity; label.text = "ARMI"; label.anchor = TextAnchor.MiddleCenter;
        label.characterSize = .05f; label.fontSize = 64; label.color = new Color(1f, .85f, .8f);
        var lamp = new GameObject("Light").AddComponent<Light>(); lamp.transform.SetParent(dispenser, false);
        lamp.type = LightType.Point; lamp.transform.localPosition = new Vector3(0f, 1.4f, -.8f); lamp.range = 3.5f; lamp.intensity = 600f; lamp.color = new Color(1f, .45f, .35f);
    }

    // ---- reflection bay ----
    void BuildLightBay()
    {
        lightBay = new GameObject("Reflection_Light_Bay").transform;
        lightBay.SetParent(transform, false);
        lightBay.localPosition = lightBayPosition;
        lightBay.localRotation = Quaternion.Euler(0f, -90f, 0f); // open side (local -Z) faces the firing line (+X)
        var shader = Shader.Find("HDRP/Lit");
        Material Mat(Color c, float smooth, Color emit)
        {
            var m = new Material(shader); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            if (emit != Color.black) { m.EnableKeyword("_EMISSIVE_COLOR_MAP"); m.SetColor("_EmissiveColor", emit); }
            return m;
        }
        var dark = Mat(new Color(.035f, .035f, .04f), .18f, Color.black);
        var floor = Mat(new Color(.05f, .05f, .055f), .35f, Color.black);
        void Part(string n, Vector3 p, Vector3 sc, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(lightBay, false);
            g.transform.localPosition = p; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = m;
        }
        Part("Floor", new Vector3(0f, .01f, 0f), new Vector3(4.6f, .02f, 3.6f), floor);
        Part("BackWall", new Vector3(0f, 1.6f, 1.8f), new Vector3(4.6f, 3.2f, .12f), dark);
        Part("WallL", new Vector3(-2.3f, 1.6f, 0f), new Vector3(.12f, 3.2f, 3.6f), dark);
        Part("WallR", new Vector3(2.3f, 1.6f, 0f), new Vector3(.12f, 3.2f, 3.6f), dark);
        Part("Roof", new Vector3(0f, 3.2f, 0f), new Vector3(4.6f, .12f, 3.6f), dark);
        // Emissive strips: long, sharp reflections on flat metal and polymer.
        Part("Strip_White", new Vector3(0f, 3.12f, .2f), new Vector3(3.8f, .03f, .12f), Mat(Color.black, .5f, new Color(9f, 9.5f, 10f)));
        Part("Strip_Orange", new Vector3(-2.22f, 1.5f, .6f), new Vector3(.03f, 1.8f, .1f), Mat(Color.black, .5f, new Color(10f, 4.5f, 1.2f)));
        Part("Strip_Cyan", new Vector3(2.22f, 1.5f, .6f), new Vector3(.03f, 1.8f, .1f), Mat(Color.black, .5f, new Color(1f, 7f, 10f)));
        // Coloured lights along the back wall and in the corners.
        var lights = new (string n, Vector3 p, Color c, float lm)[]
        {
            ("Red", new Vector3(-1.7f, 2.4f, 1.5f), new Color(1f, .12f, .08f), 900f),
            ("Green", new Vector3(-.6f, 2.6f, 1.5f), new Color(.2f, 1f, .3f), 800f),
            ("Blue", new Vector3(.6f, 2.6f, 1.5f), new Color(.15f, .35f, 1f), 1100f),
            ("Magenta", new Vector3(1.7f, 2.4f, 1.5f), new Color(1f, .2f, .85f), 900f),
            ("Sodium", new Vector3(-2.0f, .5f, -.9f), new Color(1f, .55f, .15f), 700f),
            ("Cyan", new Vector3(2.0f, .5f, -.9f), new Color(.1f, .95f, 1f), 700f),
            ("CoolWhite", new Vector3(0f, 2.9f, -1.4f), new Color(.82f, .9f, 1f), 1300f),
        };
        foreach (var l in lights) BayLight("Bay_" + l.n, LightType.Point, lightBay.TransformPoint(l.p), l.c, l.lm, 6f);
        // A warm spot slowly orbiting the centre: highlights travel across the weapon while you look at it.
        bayOrbit = new GameObject("Bay_Orbit").transform; bayOrbit.SetParent(lightBay, false); bayOrbit.localPosition = new Vector3(0f, 1.5f, 0f);
        var spot = BayLight("Bay_OrbitSpot", LightType.Spot, bayOrbit.TransformPoint(new Vector3(0f, .9f, -1.4f)), new Color(1f, .93f, .82f), 1600f, 8f);
        spot.transform.SetParent(bayOrbit, true); spot.spotAngle = 55f; spot.transform.LookAt(bayOrbit.position);
        var label = new GameObject("Label").AddComponent<TextMesh>();
        label.transform.SetParent(lightBay, false); label.transform.localPosition = new Vector3(0f, 2.75f, 1.73f);
        label.transform.localRotation = Quaternion.identity; label.text = "TEST RIFLESSI"; label.anchor = TextAnchor.MiddleCenter;
        label.characterSize = .06f; label.fontSize = 64; label.color = new Color(.9f, .9f, .95f);
    }

    Light BayLight(string n, LightType type, Vector3 position, Color color, float lumen, float range)
    {
        var go = new GameObject(n);
        go.transform.SetParent(lightBay, true);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = type; light.color = color; light.range = range;
        if (go.GetComponent<HDAdditionalLightData>() == null) go.AddComponent<HDAdditionalLightData>();
        var unitProperty = typeof(Light).GetProperty("lightUnit");
        if (unitProperty != null) { try { unitProperty.SetValue(light, System.Enum.Parse(unitProperty.PropertyType, "Lumen")); } catch { } }
        light.intensity = lumen;
        light.shadows = LightShadows.None;
        return light;
    }

    HDRPSlicePlayer Player => FindFirstObjectByType<HDRPSlicePlayer>();

    void Update()
    {
        if (bayOrbit != null) bayOrbit.Rotate(0f, 25f * Time.deltaTime, 0f, Space.Self);
        var game = ElderboomGame.Active;
        var player = game != null ? Player : null;
        if (player != null && player.ReserveAmmo < 40) player.SetReserveAmmo(99);
        if (game == null || player == null) return;
        if (ElderboomCheats.IsOpen || Input.GetKeyDown(KeyCode.F1)) return; // cheat menu has the keyboard

        if (menuOpen)
        {
            if (Input.GetKeyDown(KeyCode.B)) CloseMenu(player);
            return;
        }
        foreach (var (a, b, id) in Switch)
            if (Input.GetKeyDown(a) || Input.GetKeyDown(b)) { game.BeginSession(id, "NORMAL"); return; }

        bool near = dispenser != null && (player.transform.position - dispenser.position).sqrMagnitude < 2.6f * 2.6f;
        if (near && Time.time >= nextHint) { game.ShowNotice(player.ActionLabel("interact") + " · DISTRIBUTORE ARMI", 1.2f); nextHint = Time.time + 1f; }
        if ((near && player.ActionDown("interact")) || Input.GetKeyDown(KeyCode.B)) OpenMenu(player);
    }
    float nextHint;

    // ---- hold editor: moved to ElderboomGripEditor (key +, everywhere) ----

    void OpenMenu(HDRPSlicePlayer player)
    {
        menuOpen = true;
        player.enabled = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    void CloseMenu(HDRPSlicePlayer player)
    {
        menuOpen = false;
        if (player != null) player.enabled = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void Pick(ElderboomWeaponDefinition w)
    {
        var player = Player; var game = ElderboomGame.Active;
        ElderboomArsenal.StartingWeapon = w.id;
        menuOpen = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (game != null && player != null) game.BeginSession(player.OperatorDisplayName, "NORMAL");
    }

    void OnGUI()
    {
        if (!menuOpen) return;
        var all = ElderboomWeapons.All;
        float w = 520f, h = Mathf.Min(80f + all.Length * 46f, Screen.height - 80f);
        var box = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        GUI.color = new Color(1f, 1f, 1f, .96f);
        GUI.Box(box, "");
        var title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(box.x, box.y + 10f, w, 34f), "DISTRIBUTORE ARMI", title);
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 6, 6) };
        var view = new Rect(box.x + 14f, box.y + 52f, w - 28f, h - 96f);
        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, w - 48f, all.Length * 46f));
        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            string current = string.Equals(ElderboomArsenal.StartingWeapon, d.id, System.StringComparison.OrdinalIgnoreCase) ? "   ●" : "";
            string custom = ElderboomWeaponMods.IsModified(d.id) ? "   ·   PERSONALIZZATA" : "";
            if (GUI.Button(new Rect(0f, i * 46f, w - 48f, 40f), d.name + "   ·   " + d.category.Replace('_', ' ').ToUpperInvariant() + custom + current, btn)) Pick(d);
        }
        GUI.EndScrollView();
        var hint = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(box.x, box.yMax - 36f, w, 26f), "B per chiudere", hint);
    }
}
