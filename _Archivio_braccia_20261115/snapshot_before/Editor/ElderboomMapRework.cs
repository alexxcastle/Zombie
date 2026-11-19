using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Map rework tooling (edit mode, village scene).
//  Inspect: what sits at the points marked on the critical-points map, plus the gameplay skeleton
//           (areas, gates, stations, spawns, objectives) that the rework must keep working.
static partial class ElderboomMapRework
{
    // Image (1600 px preview of the 190 m top view) -> world XZ.
    public static Vector3 W(float px, float py) => new Vector3(5.7f - 95f + px * 190f / 1600f, 0f, -0.7f + 95f - py * 190f / 1600f);

    static readonly (string tag, float px, float py, float radius)[] Points =
    {
        ("1 house broken texture", 600, 1450, 9), ("2 broken bush", 582, 582, 4),
        ("3a black tri", 391, 585, 2.5f), ("3b", 297, 1051, 2.5f), ("3c", 403, 1279, 2.5f), ("3d", 788, 1580, 2.5f), ("3e", 983, 1440, 2.5f),
        ("3f", 1003, 1471, 2.5f), ("3g", 1188, 1286, 2.5f), ("3h", 1320, 1203, 2.5f), ("3i", 1525, 885, 2.5f),
        ("5 green slab", 1065, 900, 6), ("8 pond", 490, 550, 9), ("12 rock in fence", 1400, 1238, 6), ("13 ruin tower", 1420, 150, 9),
        ("6 creek footbridge", 810, 1192, 6), ("9 repeated house", 727, 596, 5),
    };

    [MenuItem("Elderboom HDRP/Map Rework/1 Inspect")]
    static void Inspect()
    {
        var sb = new StringBuilder();
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        foreach (var (tag, px, py, radius) in Points)
        {
            var p = W(px, py);
            sb.AppendLine("== " + tag + "  world (" + p.x.ToString("F1") + ", " + p.z.ToString("F1") + ")");
            var near = renderers.Where(r => { var b = r.bounds; var c = new Vector2(Mathf.Clamp(p.x, b.min.x, b.max.x), Mathf.Clamp(p.z, b.min.z, b.max.z)); return (c - new Vector2(p.x, p.z)).magnitude <= radius && b.size.x < 60f && b.size.z < 60f; })
                .OrderBy(r => r.bounds.size.sqrMagnitude).Take(14);
            foreach (var r in near)
            {
                var mats = string.Join(",", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + "[" + (m.shader != null ? m.shader.name : "noshader") + "]").Take(3));
                sb.AppendLine("   " + Path(r.transform) + " | " + r.GetType().Name + " | size " + r.bounds.size.ToString("F1") + " | y " + r.bounds.center.y.ToString("F1") + " | " + mats);
            }
        }
        sb.AppendLine(); sb.AppendLine("== ROOTS");
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            int rc = root.GetComponentsInChildren<Renderer>(true).Length;
            sb.AppendLine("   " + root.name + (root.activeSelf ? "" : " (off)") + " children=" + root.transform.childCount + " renderers=" + rc);
        }
        sb.AppendLine(); sb.AppendLine("== GAMEPLAY SKELETON");
        foreach (var a in Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Include))
            sb.AppendLine("   AREA " + a.areaName + " at " + a.transform.position.ToString("F0") + " size " + a.size.ToString("F0") + " unlocked=" + a.unlockedAtStart + " poly=" + (a.polygon != null ? a.polygon.Length : 0));
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include))
            sb.AppendLine("   GATE " + Path(g.transform) + " at " + g.transform.position.ToString("F0"));
        foreach (var s in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Include))
            sb.AppendLine("   STATION " + s.label + " at " + s.transform.position.ToString("F0"));
        var spawns = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Include);
        sb.AppendLine("   SPAWNS " + spawns.Length + ": " + string.Join(" ", spawns.Select(s => "(" + s.transform.position.x.ToString("F0") + "," + s.transform.position.z.ToString("F0") + ")" + s.area)));
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            var n = t.name;
            if (n.Contains("Altar") || n.Contains("Generator") || n.Contains("PowerGrid") || n.Contains("HorsePart") || n.Contains("Horse_Salvage") || n.Contains("PlayerStart") || n.Contains("Start_Spawn") || n.Contains("SouthCreek") || n.Contains("Creek_Continuous") || n.Contains("Bridge"))
                if (t.parent == null || !t.parent.name.Contains(n)) sb.AppendLine("   OBJ " + Path(t) + " at " + t.position.ToString("F0"));
        }
        var terrain = Object.FindObjectsByType<Terrain>().Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();
        var td = terrain.terrainData;
        sb.AppendLine(); sb.AppendLine("== TERRAIN " + terrain.name + " pos " + terrain.transform.position + " size " + td.size + " alphamap " + td.alphamapResolution + " layers " + string.Join(",", td.terrainLayers.Select(l => l != null ? l.name : "null")));
        sb.AppendLine("   detail protos " + td.detailPrototypes.Length + " tree protos " + string.Join(",", td.treePrototypes.Select(t => t.prefab != null ? t.prefab.name : "null")) + " trees " + td.treeInstanceCount);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/map_inspect.txt", sb.ToString());
        Debug.Log("[MapRework] Logs/map_inspect.txt");
    }

    public static string Path(Transform t)
    {
        var parts = new List<string>();
        for (var p = t; p != null && parts.Count < 5; p = p.parent) parts.Add(p.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}

// Ground-level shots in edit mode (daylight, no fog), to judge what the top view cannot show.
static partial class ElderboomMapRework
{
    public static float Ground(float x, float z)
    {
        if (Physics.Raycast(new Vector3(x, 120f, z), Vector3.down, out var hit, 300f, ~((1 << 2) | (1 << 19)), QueryTriggerInteraction.Ignore)) return hit.point.y;
        var t = Terrain.activeTerrain; return t != null ? t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y : 0f;
    }

    public static string Shot(string file, Vector3 eyeXZ, Vector3 targetXZ, float fov = 70f, float eyeHeight = 1.7f, int w = 1280, int h = 720)
    {
        var eye = new Vector3(eyeXZ.x, Ground(eyeXZ.x, eyeXZ.z) + eyeHeight, eyeXZ.z);
        var target = new Vector3(targetXZ.x, Ground(targetXZ.x, targetXZ.z) + (targetXZ.y != 0 ? targetXZ.y : 1.5f), targetXZ.z);
        var go = new GameObject("ReworkShotCam") { hideFlags = HideFlags.DontSave };
        var cam = go.AddComponent<Camera>(); go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        cam.fieldOfView = fov; cam.nearClipPlane = .1f; cam.farClipPlane = 1500f;
        go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        string r = ElderboomAbilityShots.Capture(cam, file, w, h);
        Object.DestroyImmediate(go);
        return file + " " + r;
    }

    // Temporary daylight for review shots; restored afterwards (nothing saved).
    public static System.Action Daylight()
    {
        var vol = new GameObject("ReworkDaylight") { hideFlags = HideFlags.DontSave }.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true; vol.priority = 1000;
        vol.sharedProfile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        var ex = vol.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true); ex.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed); ex.fixedExposure.Override(12.2f);
        vol.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.Fog>(true).enabled.Override(false);
        var saved = new List<(Light, Color, float, Quaternion)>();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) { saved.Add((l, l.color, l.intensity, l.transform.rotation)); l.color = new Color(1, .96f, .9f); l.intensity = 80000; l.transform.rotation = Quaternion.Euler(50, 35, 0); }
        return () =>
        {
            foreach (var (l, c, i, q) in saved) if (l != null) { l.color = c; l.intensity = i; l.transform.rotation = q; }
            Object.DestroyImmediate(vol.gameObject);
        };
    }

    static readonly (string name, Vector3 eye, Vector3 target)[] Views =
    {
        ("01_timber_inn", new Vector3(-18, 0, -62), new Vector3(-18, 4, -79)),
        ("02_pond_bush", new Vector3(-12, 0, 16), new Vector3(-28, 0, 28)),
        ("03_church_from_square", new Vector3(-34, 0, -6), new Vector3(14, 8, -12)),
        ("04_church_east_slab", new Vector3(54, 0, -14), new Vector3(36, 2, -12)),
        ("05_amphitheatre", new Vector3(79, 0, 60), new Vector3(79, 2, 76)),
        ("06_repeated_houses", new Vector3(-3, 0, 8), new Vector3(-3, 3, 24)),
        ("07_creek_bridge", new Vector3(7, 0, -36), new Vector3(7, 0, -52)),
        ("08_rock_fence", new Vector3(77, 0, -40), new Vector3(77, 1, -53)),
        ("09_black_tri_west", new Vector3(-43, 0, 14), new Vector3(-43, 1, 25)),
        ("10_black_tri_south", new Vector3(27, 0, -64), new Vector3(27, 1, -77)),
        ("11_west_field", new Vector3(-38, 0, 38), new Vector3(-62, 2, 58)),
        ("12_north_street", new Vector3(30, 0, 30), new Vector3(-10, 3, 45)),
    };

    [MenuItem("Elderboom HDRP/Map Rework/2 Ground Shots")]
    static void GroundShots() => GroundShots("Logs/rework_shots/before_");

    public static void GroundShots(string prefix)
    {
        var restore = Daylight();
        var sb = new StringBuilder();
        try { foreach (var (name, eye, target) in Views) sb.AppendLine(Shot(prefix + name + ".png", eye, target)); }
        finally { restore(); }
        Directory.CreateDirectory("Logs/rework_shots");
        File.WriteAllText("Logs/rework_shots/shots.txt", sb.ToString());
        Debug.Log("[MapRework] shots done");
    }
}

static partial class ElderboomMapRework
{
    static string Mats(Renderer r) => string.Join(", ", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + "[" + (m.shader != null ? m.shader.name : "-") + " tex=" + (m.HasProperty("_BaseColorMap") ? (m.GetTexture("_BaseColorMap") != null ? m.GetTexture("_BaseColorMap").name : "none") : m.mainTexture != null ? m.mainTexture.name : "none") + " col=" + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString("F2") : m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor").ToString("F2") : "-") + "]"));

    static Bounds Bounds(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) rs = g.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

    [MenuItem("Elderboom HDRP/Map Rework/3 Details")]
    static void Details()
    {
        var sb = new StringBuilder();
        void Dump(string label, IEnumerable<GameObject> gos, int maxRenderers = 6)
        {
            foreach (var g in gos)
            {
                if (g == null) continue;
                sb.AppendLine("== " + label + " " + Path(g.transform) + " pos " + g.transform.position.ToString("F1") + " rot " + g.transform.eulerAngles.ToString("F0") + " scale " + g.transform.lossyScale.ToString("F2") + " bounds " + Bounds(g).center.ToString("F1") + " / " + Bounds(g).size.ToString("F1"));
                foreach (var r in g.GetComponentsInChildren<Renderer>().Take(maxRenderers)) sb.AppendLine("     " + r.name + " : " + Mats(r));
            }
        }
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude).Select(t => t.gameObject).ToArray();
        Dump("INN", all.Where(g => g.name == "SouthCreek_Timber_Inn"));
        Dump("AMPHI", all.Where(g => g.name == "Woodland_Stone_Amphitheatre"));
        Dump("FOLIAGE", all.Where(g => g.name.StartsWith("FOLIAGE_TEMPLATE")), 2);
        Dump("POND", all.Where(g => g.name.StartsWith("SM_AssetPlatform")), 2);
        Dump("PERIM", all.Where(g => g.name.StartsWith("Perimeter_Building_")), 3);
        Dump("ANNEX", all.Where(g => g.name == "North_baker_back_kitchen" || g.name == "West_carpenter_workroom" || g.name == "East_wash_house"), 2);
        Dump("CHURCH", all.Where(g => g.name == "FallenCity_Church_HumanScale"), 2);
        Dump("ALTAR", all.Where(g => g.name == "Church_Altar_Weapon_Upgrader" || g.name == "Church_Tech_Setup" || g.name == "Road_ChapelEntrance" || g.name == "Access_churchyard_iron_gate"), 1);
        // White / pure-colour foliage anywhere: materials with no base texture on a leaf-like mesh.
        sb.AppendLine("== SUSPECT MATERIALS (no texture, glTF shader, or NULL)");
        var seen = new HashSet<string>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.sharedMaterials)
            {
                bool bad = m == null || m.shader == null || m.shader.name.Contains("InternalError") || (m.shader.name.Contains("glTF") && (m.name == "" || (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") == null)));
                if (!bad) continue;
                string key = (m == null ? "NULL" : m.name + m.shader?.name) + "|" + r.transform.root.name;
                if (!seen.Add(key)) continue;
                sb.AppendLine("   " + Path(r.transform) + " : " + (m == null ? "NULL" : "'" + m.name + "' " + m.shader?.name) + " at " + r.bounds.center.ToString("F0"));
            }
        }
        File.WriteAllText("Logs/map_details.txt", sb.ToString());
        Debug.Log("[MapRework] Logs/map_details.txt");
    }
}

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/4 Church Parts")]
    static void ChurchParts()
    {
        var sb = new StringBuilder();
        var church = GameObject.Find("FallenCity_Church_HumanScale");
        foreach (var r in church.GetComponentsInChildren<Renderer>())
            sb.AppendLine(r.name.PadRight(48) + " center " + r.bounds.center.ToString("F1") + " size " + r.bounds.size.ToString("F2"));
        foreach (Transform c in church.transform) if (c.GetComponent<Renderer>() == null) sb.AppendLine("NODE " + c.name + " " + c.position.ToString("F1") + " children " + c.childCount);
        // Copy the problem models' base textures out for a look.
        void Tex(string objectName, string file)
        {
            var g = GameObject.Find(objectName); if (g == null) return;
            var r = g.GetComponentInChildren<Renderer>(); var m = r.sharedMaterial;
            Texture t = null; foreach (var p in new[] { "baseColorTexture", "_BaseColorMap", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) != null) { t = m.GetTexture(p); break; }
            if (t == null) { sb.AppendLine("no texture on " + objectName); return; }
            string path = AssetDatabase.GetAssetPath(t);
            sb.AppendLine(objectName + " texture " + path + " " + t.width + "x" + t.height);
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Copy(path, "Logs/rework_shots/" + file + System.IO.Path.GetExtension(path), true);
        }
        Tex("SouthCreek_Timber_Inn", "tex_inn");
        Tex("Woodland_Stone_Amphitheatre", "tex_amphi");
        File.WriteAllText("Logs/map_church.txt", sb.ToString());
        Debug.Log("[MapRework] church parts");
    }
}

static partial class ElderboomMapRework
{
    static float SegDist(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude)); return (p - (a + ab * t)).magnitude; }

    [MenuItem("Elderboom HDRP/Map Rework/5 Probe")]
    static void Probe()
    {
        var sb = new StringBuilder();
        var pond = new Vector2(-30.4f, 29.4f);
        foreach (var a in Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Include))
        {
            if (a.polygon == null || a.polygon.Length < 2) continue;
            float best = 1e9f;
            for (int i = 0; i < a.polygon.Length; i++) best = Mathf.Min(best, SegDist(pond, a.polygon[i], a.polygon[(i + 1) % a.polygon.Length]));
            sb.AppendLine("AREA " + a.areaName + " polygon edge distance from pond centre " + best.ToString("F1") + "  poly=" + string.Join(" ", a.polygon.Select(v => "(" + v.x.ToString("F0") + "," + v.y.ToString("F0") + ")")));
        }
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r => !(r is ParticleSystemRenderer)).ToArray();
        sb.AppendLine("== near pond (r 9)");
        foreach (var r in rs.Where(r => (new Vector2(r.bounds.center.x, r.bounds.center.z) - pond).magnitude < 9f && r.bounds.size.magnitude < 20f).Take(30))
            sb.AppendLine("   " + Path(r.transform) + " c " + r.bounds.center.ToString("F1") + " s " + r.bounds.size.ToString("F1") + " col=" + (r.GetComponent<Collider>() != null));
        sb.AppendLine("== names with rail/railing/creek");
        foreach (var g in rs.Where(r => { var n = r.name.ToLowerInvariant(); return n.Contains("rail") || n.Contains("balust") || n.Contains("creek") || n.Contains("bank"); }).GroupBy(r => r.name).Take(30))
        {
            var r = g.First();
            sb.AppendLine("   " + Path(r.transform) + " x" + g.Count() + " : " + Mats(r));
        }
        sb.AppendLine("== rocks near (77,-52.7)");
        foreach (var r in rs.Where(r => (new Vector2(r.bounds.center.x, r.bounds.center.z) - new Vector2(77, -52.7f)).magnitude < 8f && r.bounds.size.y > 1.5f))
            sb.AppendLine("   " + Path(r.transform) + " c " + r.bounds.center.ToString("F1") + " s " + r.bounds.size.ToString("F1") + " : " + Mats(r));
        sb.AppendLine("== Red_Bush / church floor materials");
        foreach (var r in rs.Where(r => r.transform.parent != null && r.transform.parent.name.StartsWith("Red_Bush") || r.name.StartsWith("Red_Bush")).Take(2)) sb.AppendLine("   " + Path(r.transform) + " : " + Mats(r));
        var floor = GameObject.Find("FallenCity_Church_HumanScale").transform.Find("Circle.051");
        if (floor != null) sb.AppendLine("   church floor : " + Mats(floor.GetComponent<Renderer>()));
        // Clearance of each perimeter building to other structures.
        sb.AppendLine("== perimeter building clearance (to non-foliage renderers taller than 1 m)");
        var solids = rs.Where(r => r.bounds.size.y > 1f && r.bounds.size.x < 40 && r.bounds.size.z < 40 && !r.sharedMaterial?.name.Contains("Foliage") == true && !(r.name.Contains("Fern") || r.name.Contains("Grass"))).ToArray();
        foreach (var root in Enumerable.Range(0, 10).Select(i => GameObject.Find("Perimeter_Building_" + i)).Where(g => g != null))
        {
            var b = Bounds(root);
            float best = 99f; string who = "";
            foreach (var r in solids)
            {
                if (r.transform.IsChildOf(root.transform)) continue;
                var o = r.bounds;
                float dx = Mathf.Max(0, Mathf.Max(o.min.x - b.max.x, b.min.x - o.max.x)), dz = Mathf.Max(0, Mathf.Max(o.min.z - b.max.z, b.min.z - o.max.z));
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) { best = d; who = Path(r.transform); }
            }
            sb.AppendLine("   " + root.name + " size " + b.size.ToString("F1") + " nearest " + best.ToString("F1") + " m : " + who);
        }
        // Roads: names and bounds (painted strips).
        sb.AppendLine("== spawn points near church footprint");
        foreach (var s in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Include))
            if (s.transform.position.x > -22 && s.transform.position.x < 48 && s.transform.position.z > -34 && s.transform.position.z < 8) sb.AppendLine("   spawn " + s.transform.position.ToString("F1"));
        File.WriteAllText("Logs/map_probe.txt", sb.ToString());
        Debug.Log("[MapRework] probe");
    }
}
