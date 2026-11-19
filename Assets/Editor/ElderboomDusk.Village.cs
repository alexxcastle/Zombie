using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// The rest of the village in winter: the ten identical "perimeter" boxes become alpine houses, barns, a tavern and a mill
// (Hyper3D models in Assets/GameAssets/Dusk/Rodin), street lamps become lantern posts, snow lies on the open ground,
// firewood, barrels, boulders and spruces fill the gaps. Batch: -executeMethod ElderboomDusk.VillageBatch
static partial class ElderboomDusk
{
    public static void VillageBatch() { Open(); Village(); Save("village"); }

    static Dictionary<string, float> ReadTable(string path)
    {
        var d = new Dictionary<string, float>(); if (!File.Exists(path)) return d;
        foreach (var line in File.ReadAllLines(path)) { var a = line.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries); if (a.Length >= 2 && !a[0].StartsWith("#")) d[a[0]] = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture); }
        return d;
    }
    static bool Has(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(D + "/Rodin/" + name + ".glb") != null;
    static void Solid(GameObject go, float shrink = .85f)
    {
        if (!go) return; var b = BoundsOf(go); var c = new GameObject(go.name + "_Collider"); c.transform.SetParent(go.transform.parent, false); c.transform.position = b.center;
        c.AddComponent<BoxCollider>().size = new Vector3(b.size.x * shrink, b.size.y, b.size.z * shrink); c.isStatic = true;
    }

    static void Village()
    {
        var old = GameObject.Find("Dusk_Village"); if (old) Object.DestroyImmediate(old);
        var root = new GameObject("Dusk_Village").transform; cur = root;
        var hero = GameObject.Find("Dusk_Hero_Forge"); heroRoot = hero ? hero.transform : root;
        var front = ReadTable("Logs/dusk/model_front.txt");       // yaw of each model's door side, in model space
        float Front(string n) => front.TryGetValue(n, out var f) ? f : 0f;
        var centre = new Vector3(8, 0, -12);

        // ---------------------------------------------------------- buildings in place of the perimeter boxes
        // name -> height in metres
        var plan = new (string kind, float h)[] {
            ("stone_cottage", 7.2f), ("timber_chalet", 10f), ("stone_barn", 8.6f), ("stone_cottage", 7.2f), ("timber_chalet", 10f),
            ("village_tavern", 13f), ("stone_barn", 8.6f), ("watermill", 10f), ("alpine_stone_house", 10.5f) };
        var houses = new List<GameObject>();
        for (int i = 0; i < plan.Length; i++)
        {
            var pb = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(t => t.parent == null && t.name == "Perimeter_Building_" + i);
            if (!pb) { log.AppendLine("  no Perimeter_Building_" + i); continue; }
            if (!Has(plan[i].kind)) { log.AppendLine("  model not ready for Perimeter_Building_" + i + ": " + plan[i].kind); pb.gameObject.SetActive(true); continue; }
            pb.gameObject.SetActive(true); var b = BoundsOf(pb.gameObject);
            var to = centre - b.center; float face = Mathf.Abs(to.x) > Mathf.Abs(to.z) ? (to.x > 0 ? 90 : 270) : (to.z > 0 ? 0 : 180);
            var go = Rodin(plan[i].kind, new Vector3(b.center.x, 0, b.center.z), face - Front(plan[i].kind), plan[i].h);
            if (!go) continue;
            go.name = "Dusk_House_" + i + "_" + plan[i].kind; Solid(go, .88f); houses.Add(go);
            pb.gameObject.SetActive(false); EditorUtility.SetDirty(pb.gameObject);
            log.AppendLine("  Perimeter_Building_" + i + " " + b.size.ToString("F0") + " -> " + plan[i].kind + " facing " + face);
        }

        // ---------------------------------------------------------- dressing around every new house
        var rr = new System.Random(77); float R2(float a, float b2) => a + (float)rr.NextDouble() * (b2 - a);
        foreach (var h in houses)
        {
            var b = BoundsOf(h); var away = (b.center - centre); away.y = 0; away.Normalize(); var side = new Vector3(-away.z, 0, away.x);
            var p = b.center + side * (Mathf.Max(b.extents.x, b.extents.z) * .55f + 1.6f); Solid(Rodin("firewood_stack", new Vector3(p.x, 0, p.z), R2(0, 360), 1.5f), .8f);
            p = b.center - side * (Mathf.Max(b.extents.x, b.extents.z) * .55f + 1.8f) - away * 1.5f; Solid(Rodin("barrel_cluster", new Vector3(p.x, 0, p.z), R2(0, 360), 1.45f), .8f);
            p = b.center + away * (Mathf.Max(b.extents.x, b.extents.z) * .5f + 2.6f) + side * R2(-2f, 2f); Solid(Rodin("woodshed", new Vector3(p.x, 0, p.z), Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg, 3.1f), .85f);
        }

        // ---------------------------------------------------------- lantern posts instead of the modern street lamps
        if (Has("lantern_post"))
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude).Where(t => t.name == "Elderboom_Streetlamp").ToArray())
            {
                var mr = t.GetComponent<MeshRenderer>(); if (!mr) continue; mr.enabled = false; EditorUtility.SetDirty(mr);
                var p = t.position; float gy = GroundY(p.x, p.z);
                Rodin("lantern_post", new Vector3(p.x, 0, p.z), t.eulerAngles.y - Front("lantern_post"), 2.8f);
                foreach (Transform c in t) { c.position = new Vector3(p.x, gy + 2.0f, p.z) + t.forward * .0f; EditorUtility.SetDirty(c); foreach (var r in c.GetComponentsInChildren<MeshRenderer>()) { r.enabled = false; EditorUtility.SetDirty(r); } }
                n++;
            }
            log.AppendLine("  lantern posts " + n);
        }

        // ---------------------------------------------------------- hero area extras
        var house9 = GameObject.Find("Dusk_alpine_stone_house");
        Solid(Rodin("covered_well", new Vector3(83.5f, 0, -27.5f), 20, 3.4f), .7f);
        Solid(Rodin("stone_trough", new Vector3(66.2f, 0, -35.2f), 70, .95f), .8f);
        Rodin("wooden_sled", new Vector3(71.8f, 0, -40.6f), 130, .65f);
        Solid(Rodin("market_stall", new Vector3(81.5f, 0, -36.8f), 200 - Front("market_stall"), 2.8f), .8f);
        Solid(Rodin("snow_boulders", new Vector3(63.5f, 0, -45.0f), 40, 1.5f), .8f); Solid(Rodin("snow_boulders", new Vector3(86.5f, 0, -43f), 200, 1.9f), .8f);
        Solid(Rodin("snow_boulders", new Vector3(41f, 0, -41.2f), 100, 1.2f), .8f); Solid(Rodin("snow_boulders", new Vector3(18f, 0, -41.5f), 300, 1.3f), .8f);
        if (Has("hay_cart")) { var c = heroRoot.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Dusk_Abandoned_Farm_Cart"); Vector3 cp = c ? BoundsOf(c.gameObject).center : new Vector3(71, 0, -28); if (c) Object.DestroyImmediate(c.gameObject); Solid(Rodin("hay_cart", new Vector3(cp.x, 0, cp.z), 100 - Front("hay_cart"), 2.1f), .8f); }
        if (Has("rustic_fence"))
        {   // along the track from the start towards the forge, on the creek side
            var a = new Vector2(88, -34.5f); var bb = new Vector2(76.5f, -38.2f); float len = Vector2.Distance(a, bb); int cnt = Mathf.FloorToInt(len / 1.8f); var dir = (bb - a).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            for (int i = 0; i < cnt; i++) { var q = a + dir * (i * 1.8f + .9f); Rodin("rustic_fence", new Vector3(q.x, 0, q.y), yaw - Front("rustic_fence"), 1.25f); }
        }
        ClearGrowth(houses.Concat(heroRoot.GetComponentsInChildren<Transform>().Where(t => t.name == "Dusk_alpine_stone_house" || t.name == "Dusk_blacksmith_forge").Select(t => t.gameObject)).ToList());

        TerrainSnow();
        try { log.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation()); } catch (System.Exception e) { log.AppendLine("nav bake failed " + e.Message); }
    }

    // Shrubs, alders and terrain trees standing inside (or right against) the new buildings go.
    static void ClearGrowth(List<GameObject> buildings)
    {
        var boxes = buildings.Where(b => b).Select(b => { var x = BoundsOf(b); x.Expand(new Vector3(3f, 40f, 3f)); return x; }).ToList(); int hidden = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!(t.name.StartsWith("Cypress") || t.name.StartsWith("Red_Bush") || t.name.StartsWith("SM_BlackAlder") || t.name.StartsWith("Fern_"))) continue;
            if (boxes.Any(x => x.Contains(new Vector3(t.position.x, x.center.y, t.position.z)))) { t.gameObject.SetActive(false); hidden++; }
        }
        var ter = Valley(); var td = ter.terrainData; var pos = ter.transform.position; var size = td.size;
        var keep = td.treeInstances.Where(ti => { var w = pos + Vector3.Scale(ti.position, size); return !boxes.Any(x => x.Contains(new Vector3(w.x, x.center.y, w.z))); }).ToArray();
        int cut = td.treeInstanceCount - keep.Length; if (cut > 0) td.SetTreeInstances(keep, true);
        log.AppendLine("  cleared growth: " + hidden + " shrubs, " + cut + " terrain trees");
    }

    // Snow on the open ground of the whole valley: the unused third terrain layer becomes snow, painted in soft patches
    // away from roads, slopes and the creek, thicker away from the village.
    static void TerrainSnow()
    {
        var t = Valley(); var td = t.terrainData; var layers = td.terrainLayers; const int SI = 2;
        string lp = D + "/Valley_Snow.terrainlayer"; var snow = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
        if (snow == null) { snow = new TerrainLayer(); AssetDatabase.CreateAsset(snow, lp); }
        snow.diffuseTexture = Tex(D + "/Downloaded/Textures/snow_02/snow_02_diff_2k.jpg"); snow.normalMapTexture = Tex(D + "/Downloaded/Textures/snow_02/snow_02_nor_gl_2k.jpg", true);
        snow.tileSize = new Vector2(5, 5); snow.smoothness = .22f; snow.metallic = 0; snow.normalScale = 1; EditorUtility.SetDirty(snow);
        layers[SI] = snow; td.terrainLayers = layers;
        int w = td.alphamapWidth, h = td.alphamapHeight, L = td.alphamapLayers; var a = td.GetAlphamaps(0, 0, w, h); var pos = t.transform.position; var size = td.size;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f), v = y / (h - 1f), wx = pos.x + u * size.x, wz = pos.z + v * size.z;
                float n = Mathf.PerlinNoise(wx * .045f + 11, wz * .045f + 5) * .6f + Mathf.PerlinNoise(wx * .16f + 3, wz * .16f + 9) * .4f;
                float dist = Vector2.Distance(new Vector2(wx, wz), new Vector2(8, -12));
                float s = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .66f, n + Mathf.InverseLerp(70, 190, dist) * .22f));
                s *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(16, 32, td.GetSteepness(u, v)));
                s *= 1 - Mathf.Clamp01((a[y, x, 5] + a[y, x, 7]) * 3f);
                if (td.GetInterpolatedHeight(u, v) + pos.y < .35f) s = 0;
                float others = 0; for (int l = 0; l < L; l++) if (l != SI) others += a[y, x, l];
                if (others < 1e-4f) { a[y, x, 0] = 1; others = 1; }
                for (int l = 0; l < L; l++) if (l != SI) a[y, x, l] *= (1 - s) / others;
                a[y, x, SI] = s;
            }
        td.SetAlphamaps(0, 0, a); EditorUtility.SetDirty(td);
        // mountains: matte snow, sharper silhouette
        var ms = AssetDatabase.LoadAssetAtPath<TerrainLayer>(D + "/Mountain_Snow.terrainlayer"); if (ms) { ms.smoothness = .06f; ms.normalScale = .35f; ms.diffuseTexture = snow.diffuseTexture; ms.tileSize = new Vector2(90, 90); EditorUtility.SetDirty(ms); }
        var mt = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).FirstOrDefault(x => x.name == "Elderboom_Mountains"); if (mt) { mt.heightmapPixelError = 5; EditorUtility.SetDirty(mt); }
        log.AppendLine("  terrain snow painted");
    }
}
