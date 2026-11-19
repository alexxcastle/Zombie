using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

// "build": the block map. Six areas separated by natural borders (rocks, banks, a creek, brush) and a few built
// ones (fence, collapsed stone wall), seven passages bought with credits, rolling ground with level pads under
// everything that stands on it, a ring of hills and forest around the playable land, new spawn points per block,
// stations moved to their blocks, navigation rebaked. Re-runnable: the terrain starts again from the copy made on
// the first run, and the previous build root is replaced.
// "shots": pictures for checking (top view + one at each passage) in Logs/blocks_*.jpg.
static partial class ElderboomClaudeBlocks
{
    static readonly Vector2 C = new Vector2(5.7f, -0.7f);
    static Vector2 W(float dx, float dz) => new Vector2(C.x + dx, C.y + dz);
    static Vector2[] Ws(params float[] xz) { var r = new Vector2[xz.Length / 2]; for (int i = 0; i < r.Length; i++) r[i] = W(xz[2 * i], xz[2 * i + 1]); return r; }

    const string TerrainCopy = "Assets/Terrain/Elderboom_Valley_preBlocks.asset";
    const string NavPath = "Assets/Resources/Elderboom/Navigation.asset";
    const string RootName = "Elderboom_Blocks";

    // ---------- Layout (dx, dz from the village centre; +z north) ----------
    static readonly Vector2[] Outer = Ws(-101,16, -110,-22, -96,-58, -64,-72, -40,-104, 0,-116, 42,-108, 72,-80, 92,-46, 113,-14, 108,22,
                                         119,56, 96,93, 56,101, 33,70, 14,93, -22,99, -52,86, -64,56, -76,31, -101,16);
    enum Kind { Rocks, Fence, StoneWall, Creek, Brush }
    sealed class Border { public Kind kind; public Vector2[] pts; public Border(Kind k, Vector2[] p) { kind = k; pts = p; } }
    static readonly Border[] Borders =
    {
        new Border(Kind.Rocks, Ws(-45,20, -45,12)),                        // square | farm
        new Border(Kind.Fence, Ws(-45,12, -45,-12)),
        new Border(Kind.Rocks, Ws(-45,-12, -45,-47)),
        new Border(Kind.Rocks, Ws(-45,20, -40,32, -28,36, -20,32, -3,31, 1,28.5f, 5,26, 28.5f,25)), // square | timber yard
        new Border(Kind.StoneWall, Ws(28.5f,25, 28.5f,-24)),                // square | cemetery
        new Border(Kind.Rocks, Ws(28.5f,-24, 40,-26, 50,-40, 50,-47)),
        new Border(Kind.Creek, Ws(-64,-72, -62,-55, -45,-47, -25,-46, 1,-47, 25,-49, 50,-47)), // the creek
        new Border(Kind.Rocks, Ws(50,-47, 62,-60, 72,-80)),                 // cemetery | creek
        new Border(Kind.Rocks, Ws(-45,20, -62,27, -76,31)),                 // farm | timber yard
        new Border(Kind.Brush, Ws(28.5f,25, 33,40, 33,70)),                 // timber yard | woods
        new Border(Kind.Rocks, Ws(28.5f,25, 55,26, 80,24, 108,22)),         // cemetery | woods
    };
    sealed class Block { public string name; public bool start; public Vector2[] poly; public int spawns; public Block(string n, bool s, int k, Vector2[] p) { name = n; start = s; spawns = k; poly = p; } }
    static readonly Block[] Blocks =
    {
        new Block("Village Square", true, 10, Ws(-45,20, -40,32, -28,36, -20,32, -3,31, 1,28.5f, 5,26, 28.5f,25, 28.5f,-24, 40,-26, 50,-40, 50,-47, 25,-49, 1,-47, -25,-46, -45,-47, -45,0)),
        new Block("West Farm", false, 7, Ws(-45,20, -62,27, -76,31, -101,16, -110,-22, -96,-58, -64,-72, -62,-55, -45,-47, -45,0)),
        new Block("North Timber Yard", false, 7, Ws(-45,20, -40,32, -28,36, -20,32, -3,31, 1,28.5f, 5,26, 28.5f,25, 33,40, 33,70, 14,93, -22,99, -52,86, -64,56, -76,31, -62,27)),
        new Block("East Cemetery", false, 7, Ws(28.5f,25, 55,26, 80,24, 108,22, 113,-14, 92,-46, 72,-80, 62,-60, 50,-47, 50,-40, 40,-26, 28.5f,-24)),
        new Block("South Creek", false, 7, Ws(-45,-47, -25,-46, 1,-47, 25,-49, 50,-47, 62,-60, 72,-80, 42,-108, 0,-116, -40,-104, -64,-72, -62,-55)),
        new Block("Outer Woods", false, 6, Ws(28.5f,25, 33,40, 33,70, 56,101, 96,93, 119,56, 108,22, 80,24, 55,26)),
    };
    enum GateKind { Wood, Logs, Iron, Bridge, Brush }
    sealed class Pass { public string area, otherArea; public int cost; public GateKind kind; public Vector2 at; public Vector2 along; public Pass(string a, int c, GateKind k, Vector2 p, Vector2 dir, string other = "") { area = a; cost = c; kind = k; at = p; along = dir.normalized; otherArea=other; } }
    static readonly Pass[] Passes =
    {
        new Pass("West Farm", 750, GateKind.Wood, W(-45,0), new Vector2(0,1)),
        new Pass("North Timber Yard", 1000, GateKind.Logs, W(1,28.5f), new Vector2(1,-.6f)),
        new Pass("East Cemetery", 1000, GateKind.Iron, W(28.5f,-15), new Vector2(0,1)),
        new Pass("South Creek", 1250, GateKind.Bridge, W(1,-47), new Vector2(1,0)),
        new Pass("Outer Woods", 1500, GateKind.Brush, W(33,52), new Vector2(0,1)),
        new Pass("North Timber Yard", 650, GateKind.Wood, W(-56,24.53f), new Vector2(-17,7), "West Farm"),
        new Pass("Outer Woods", 850, GateKind.Iron, W(70,24.8f), new Vector2(25,-2), "East Cemetery"),
    };
    const float GapHalf = 2.9f;

    // ---------- Geometry helpers ----------
    static float SegDist(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f)); return Vector2.Distance(p, a + ab * t); }
    static float LineDist(Vector2 p, Vector2[] pts) { float d = float.MaxValue; for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, SegDist(p, pts[i], pts[i + 1])); return d; }
    static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }
    static float PassDist(Vector2 p) { float d = float.MaxValue; foreach (var g in Passes) d = Mathf.Min(d, Vector2.Distance(p, g.at)); return d; }
    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

    static Terrain terrain; static TerrainData data;
    static float Ground(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0, p.y)) + terrain.transform.position.y;

    static string RunBuild(string mode)
    {
        terrain = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();
        data = terrain.terrainData;
        if (mode.StartsWith("shots")) return Shots();
        if (mode.StartsWith("playtest")) return StartPlaytest();
        if (!mode.StartsWith("build")) return "FAIL unknown mode " + mode;
        var sb = new StringBuilder();
        Random.InitState(4242);

        // Original terrain kept once; every run starts from it.
        string dataPath = AssetDatabase.GetAssetPath(data);
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainCopy) == null) { AssetDatabase.CopyAsset(dataPath, TerrainCopy); sb.AppendLine("terrain copy saved " + TerrainCopy); }
        var original = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainCopy);

        // Old layout off: yard gates and fences, ring spawns, square boundary walls; previous build removed.
        var old = GameObject.Find(RootName); if (old != null) Object.DestroyImmediate(old);
        int off = 0;
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "West_Farm_Gate" || go.name == "North_Timber_Yard_Gate" || go.name == "Yard_Fence" || go.name.StartsWith("Zombie_Spawn_") || (go.name.StartsWith("Elderboom_") && go.name.EndsWith("_Boundary")))
            { if (go.activeSelf) { go.SetActive(false); off++; } }
        sb.AppendLine("old layout objects switched off " + off);
        var valley = GameObject.Find("Elderboom_Valley");
        if (valley != null)
            foreach (Transform ch in valley.transform)
            {
                var r = ch.GetComponentInChildren<Renderer>();
                var at = r != null ? r.bounds.center : ch.position;
                sb.AppendLine("  valley child " + ch.name + " at " + at.ToString("F0") + (r != null ? " size " + r.bounds.size.ToString("F0") : "") + (ch.gameObject.activeSelf ? "" : " (off)"));
                if (ch.name.Contains("Water") && Vector2.Distance(new Vector2(at.x, at.z), C) < 150 && ch.gameObject.activeSelf) { ch.gameObject.SetActive(false); sb.AppendLine("    -> switched off (ground reshaped under it)"); }
            }
        var root = new GameObject(RootName).transform;
        // Small legacy sections cross the new timber and creek approaches. Keep their
        // source objects in the scene, disabled, instead of leaving invisible collisions.
        var village=GameObject.Find("Elderboom_Village_Port");
        if(village!=null)foreach(Transform piece in village.transform)
            if(piece.name=="S_Mossy_Stone_Wall_ukhgdfyga_lod3_Var159" || piece.name=="S_Modular_Fence_ufflaitdw_lod3_Var55")
                piece.gameObject.SetActive(false);
        Physics.SyncTransforms();

        FitCemeteryApproach(sb);

        // ---------- Ground ----------
        int res = data.heightmapResolution; var size = data.size; var origin = terrain.transform.position;
        var heights = original.GetHeights(0, 0, res, res);
        float cell = size.x / (res - 1);
        var pad = new float[res, res];
        void PadRect(float x0, float z0, float x1, float z1, float core, float fall)
        {
            int i0 = Mathf.Max(0, Mathf.FloorToInt((x0 - fall - origin.x) / cell)), i1 = Mathf.Min(res - 1, Mathf.CeilToInt((x1 + fall - origin.x) / cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((z0 - fall - origin.z) / cell)), j1 = Mathf.Min(res - 1, Mathf.CeilToInt((z1 + fall - origin.z) / cell));
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
            {
                float x = origin.x + i * cell, z = origin.z + j * cell;
                float dx = Mathf.Max(x0 - x, 0, x - x1), dz = Mathf.Max(z0 - z, 0, z - z1);
                float w = 1f - Smooth(core, core + fall, Mathf.Sqrt(dx * dx + dz * dz));
                if (w > pad[j, i]) pad[j, i] = w;
            }
        }
        int padded = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r.GetComponentInParent<Terrain>() != null || r is ParticleSystemRenderer) continue;
            string n = r.name; var b = r.bounds;
            if (n.Contains("SkySphere") || n.Contains("Rock") || b.size.x > 60 || b.size.z > 60) continue;
            if (b.min.y > 6f || b.max.y < -2f) continue;                 // roofs, sky decorations, underground
            if (Vector2.Distance(new Vector2(b.center.x, b.center.z), C) > 140) continue;
            if (n.StartsWith("Road_"))
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var v = mf.sharedMesh.vertices;
                for (int k = 0; k < v.Length; k += 3) { var w = r.transform.TransformPoint(v[k]); PadRect(w.x, w.z, w.x, w.z, .6f, 3f); }
                padded++; continue;
            }
            if (n.Contains("BlackAlder")) { var p = r.transform.position; PadRect(p.x, p.z, p.x, p.z, 1.2f, 2.5f); padded++; continue; }
            PadRect(b.min.x, b.min.z, b.max.x, b.max.z, .7f, 3.6f); padded++;
        }
        sb.AppendLine("level pads under " + padded + " objects");

        float rise = 0; int changed = 0;
        Vector2[][] rockLines = Borders.Where(x => x.kind == Kind.Rocks || x.kind == Kind.Brush).Select(x => x.pts).ToArray();
        Vector2[] creek = Borders.First(x => x.kind == Kind.Creek).pts;
        for (int j = 0; j < res; j++) for (int i = 0; i < res; i++)
        {
            var p = new Vector2(origin.x + i * cell, origin.z + j * cell);
            if (Vector2.Distance(p, C) > 175) continue;
            float orig = heights[j, i] * size.y + origin.y;
            bool inside = Inside(p, Outer);
            float dOut = LineDist(p, Outer);
            float u = (Mathf.PerlinNoise(p.x * .028f + 31.7f, p.y * .028f + 17.3f) - .5f) * 1.7f + (Mathf.PerlinNoise(p.x * .075f + 5.1f, p.y * .075f + 9.4f) - .5f) * .55f;
            float h = 1f + u + (p.y - C.y) * .004f;
            float passFade = Smooth(3.5f, 9f, PassDist(p));
            float berm = 0; foreach (var line in rockLines) berm = Mathf.Max(berm, 1.7f * Mathf.Exp(-Mathf.Pow(LineDist(p, line) / 3.3f, 2)));
            h += berm * passFade;
            float ring = 5.5f * Mathf.Exp(-Mathf.Pow(dOut / 11f, 2)) + (inside ? 0 : 2.5f * Smooth(0, 14, dOut));
            h += ring;
            float dCreek = LineDist(p, creek);
            float w = pad[j, i] * Smooth(2.5f, 6.5f, dCreek) * (inside || dOut < 4 ? 1 : 0);
            h = Mathf.Lerp(h, 1f, w);
            h -= 1.55f * Mathf.Exp(-Mathf.Pow(dCreek / 2.7f, 2)) * (inside || dOut < 6 ? 1 : 0);
            if (!inside) h = Mathf.Lerp(h, orig, Smooth(16, 46, dOut));
            heights[j, i] = Mathf.Clamp01((h - origin.y) / size.y);
            rise = Mathf.Max(rise, h); changed++;
        }
        data.SetHeights(0, 0, heights);
        if (valley != null)
            foreach (Transform ch in valley.transform)
            {
                if (!ch.name.Contains("Rock") || !ch.gameObject.activeSelf) continue;
                var rr = ch.GetComponentsInChildren<Renderer>(); if (rr.Length == 0) continue;
                var rb = RB(ch.gameObject); var at = new Vector2(rb.center.x, rb.center.z);
                if (Vector2.Distance(at, C) > 150) continue;
                ch.position += Vector3.up * (Ground(at) - rb.size.y * .25f - rb.min.y);
            }
        sb.AppendLine("ground samples reshaped " + changed + ", highest " + rise.ToString("F1") + " m");

        // ---------- Terrain trees: original set, minus those in the way, plus forest around the ring ----------
        var trees = new List<TreeInstance>();
        foreach (var ti in original.treeInstances)
        {
            var wp = new Vector2(origin.x + ti.position.x * size.x, origin.z + ti.position.z * size.z);
            if (PassDist(wp) < 7 || Borders.Any(bd => LineDist(wp, bd.pts) < 3.5f) || LineDist(wp, Outer) < 3) continue;
            int pi = Mathf.Clamp(Mathf.RoundToInt((wp.x - origin.x) / cell), 0, res - 1), pj = Mathf.Clamp(Mathf.RoundToInt((wp.y - origin.z) / cell), 0, res - 1);
            if (pad[pj, pi] > .25f && Inside(wp, Outer)) continue;
            var t = ti; t.position = new Vector3(t.position.x, (Ground(wp) - origin.y) / size.y, t.position.z); trees.Add(t);
        }
        int kept = trees.Count, added = 0;
        for (int k = 0; k < 4000 && added < 260; k++)
        {
            int s = Random.Range(0, Outer.Length - 1);
            var a = Outer[s]; var b2 = Outer[s + 1];
            var along = Vector2.Lerp(a, b2, Random.value);
            var normal = new Vector2(-(b2 - a).y, (b2 - a).x).normalized;
            var p = along + normal * Random.Range(-34f, 34f);
            if (Inside(p, Outer) || LineDist(p, Outer) < 5) continue;
            if (trees.Any(t => (new Vector2(origin.x + t.position.x * size.x, origin.z + t.position.z * size.z) - p).sqrMagnitude < 4.5f * 4.5f)) continue;
            var ti = new TreeInstance
            {
                prototypeIndex = Random.Range(0, data.treePrototypes.Length),
                position = new Vector3((p.x - origin.x) / size.x, (Ground(p) - origin.y) / size.y, (p.y - origin.z) / size.z),
                widthScale = Random.Range(.85f, 1.25f), rotation = Random.Range(0, Mathf.PI * 2),
                color = Color.white, lightmapColor = Color.white
            };
            ti.heightScale = ti.widthScale * Random.Range(.9f, 1.15f);
            trees.Add(ti); added++;
        }
        data.SetTreeInstances(trees.ToArray(), true);
        sb.AppendLine("terrain trees: " + original.treeInstanceCount + " -> kept " + kept + " + forest ring " + added);
        Physics.SyncTransforms();

        // ---------- Borders ----------
        var rockPrefabs = new[] { "Rock_A_01", "Rock_A_02", "Rock_B_01", "Rock_B_02", "Rock_C_01", "Rock_C_02", "Rock_D" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Rocks/" + n + ".prefab")).Where(g => g != null).ToArray();
        var bigRocks = new[] { "Rock_Overgrown_A", "Rock_Overgrown_B", "Rock_Overgrown_C", "Rock_Overgrown_D" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Rocks/" + n + ".prefab")).Where(g => g != null).ToArray();
        var bushes = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TerrainDemoScene_HDRP/Prefabs/Foliage" }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null).ToArray();
        var waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Water/WaterPlane.prefab");
        var fenceSrc = FindPiece("S_Modular_Fence_ufflaitdw"); var wallSrc = FindPiece("S_Mossy_Stone_Wall_ukhgdfyga");
        var beamSrc = FindPiece("S_Worn_Wooden_Beam"); var woodPile = FindPiece("S_Chopped_Wood_Pile");
        var graveSrc = FindPiece("S_Old_Gravestone");
        sb.AppendLine("pieces: rocks " + rockPrefabs.Length + " big " + bigRocks.Length + " bushes " + bushes.Length + " water " + (waterPrefab != null) + " fence " + (fenceSrc != null) + " wall " + (wallSrc != null) + " beam " + (beamSrc != null) + " pile " + (woodPile != null) + " grave " + (graveSrc != null));
        var borderRoot = new GameObject("Borders").transform; borderRoot.SetParent(root);
        var wallRoot = new GameObject("Invisible_Walls").transform; wallRoot.SetParent(root);
        int rocks = 0, skipped = 0, pieces = 0;

        foreach (var bd in Borders)
        {
            var parent = new GameObject(bd.kind + "_Border").transform; parent.SetParent(borderRoot);
            if (bd.kind == Kind.Rocks || bd.kind == Kind.Brush)
                foreach (var p in Walk(bd.pts, bd.kind == Kind.Brush ? 2.4f : 2.7f))
                {
                    if (PassDist(p) < GapHalf + 1.2f) continue;
                    bool bush = bushes.Length > 0 && Random.value < (bd.kind == Kind.Brush ? .6f : .2f);
                    var prefab = bush ? bushes[Random.Range(0, bushes.Length)] : rockPrefabs[Random.Range(0, rockPrefabs.Length)];
                    if (Place(prefab, parent, p, bush ? Random.Range(2.2f, 3.2f) : Random.Range(2.3f, 3.4f), bush ? .1f : .32f)) rocks++; else skipped++;
                }
            else if (bd.kind == Kind.Fence || bd.kind == Kind.StoneWall)
            {
                var src = bd.kind == Kind.Fence ? fenceSrc : wallSrc;
                if (src != null) pieces += Tile(src, parent, bd.pts, bd.kind == Kind.StoneWall);
                if (bd.kind == Kind.StoneWall) foreach (var p in Walk(bd.pts, 7f)) if (PassDist(p) > GapHalf + 1.5f && Random.value < .5f) Place(rockPrefabs[Random.Range(0, rockPrefabs.Length)], parent, p + Random.insideUnitCircle * 1.2f, Random.Range(1.0f, 1.7f), .3f);
            }
            else if (bd.kind == Kind.Creek && waterPrefab != null)
                for (int i = 0; i + 1 < bd.pts.Length; i++)
                {
                    var a = bd.pts[i]; var b = bd.pts[i + 1]; var mid = (a + b) * .5f;
                    float bed = Mathf.Min(Ground(a), Ground(mid), Ground(b));
                    var water = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab); water.name = "Creek_Water";
                    water.transform.SetParent(parent, true);
                    water.transform.position = new Vector3(mid.x, bed + .55f, mid.y);
                    water.transform.rotation = Quaternion.identity; water.transform.localScale = Vector3.one;
                    var wb = RB(water); // size of the prefab as it comes: scale it to the creek, not by guess
                    water.transform.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.y - a.y)) * Quaternion.Euler(0, 90, 0);
                    water.transform.localScale = new Vector3((Vector2.Distance(a, b) + 2.5f) / Mathf.Max(wb.size.x, .01f), 1f, 3.4f / Mathf.Max(wb.size.z, .01f));
                    var wb2 = RB(water); water.transform.position += new Vector3(mid.x - wb2.center.x, 0, mid.y - wb2.center.z);
                    foreach (var col in water.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                }
            Walls(wallRoot, bd.pts, bd.kind == Kind.Creek ? 3.5f : 4.5f);
        }
        // Ring: big boulders + rocks + walls.
        var ringRoot = new GameObject("Ring_Border").transform; ringRoot.SetParent(borderRoot);
        int k2 = 0;
        foreach (var p in Walk(Outer, 4.2f))
        {
            var dirOut = OutwardNormal(p);
            if (k2++ % 4 == 0 && bigRocks.Length > 0) { if (Place(bigRocks[Random.Range(0, bigRocks.Length)], ringRoot, p + dirOut * Random.Range(3f, 6f), Random.Range(4.5f, 7f), .3f)) rocks++; }
            else if (Random.value < .55f && Place(rockPrefabs[Random.Range(0, rockPrefabs.Length)], ringRoot, p + dirOut * Random.Range(-.5f, 2f), Random.Range(2.4f, 3.6f), .3f)) rocks++;
        }
        Walls(wallRoot, Outer, 6f);
        sb.AppendLine("borders: rocks/bushes " + rocks + " (skipped on buildings " + skipped + "), fence/wall pieces " + pieces + ", wall boxes " + wallRoot.childCount);

        // ---------- Passages ----------
        var gateRoot = new GameObject("Passages").transform; gateRoot.SetParent(root);
        var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Elderboom_Gate_Metal.mat");
        var oldGate = GameObject.Find("West_Farm_Gate"); if (metal == null) { var og = Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include).FirstOrDefault(); if (og != null && og.GetComponent<Renderer>() != null) metal = og.GetComponent<Renderer>().sharedMaterial; }
        foreach (var ps in Passes)
        {
            float y = Ground(ps.at);
            var gate = new GameObject("Passage_" + ps.area.Replace(" ", "_") + "_" + ps.cost);
            gate.transform.SetParent(gateRoot);
            gate.transform.position = new Vector3(ps.at.x, y + 1.4f, ps.at.y);
            gate.transform.rotation = Quaternion.LookRotation(new Vector3(ps.along.x, 0, ps.along.y)) * Quaternion.Euler(0, -90, 0); // local x along the border
            var box = gate.AddComponent<BoxCollider>(); box.size = new Vector3(GapHalf * 2 + .6f, 3.2f, 1.1f);
            var logic = gate.AddComponent<ElderboomGate>(); logic.areaName = ps.area; logic.cost = ps.cost;
            logic.otherAreaName=ps.otherArea;
            BuildGateLook(gate.transform, ps.kind, fenceSrc, beamSrc, woodPile, metal, bushes, rockPrefabs);
            if (ps.kind == GateKind.Bridge && beamSrc != null)
            {
                var deck = new GameObject("Bridge_Deck"); deck.transform.SetParent(gateRoot);
                var normal = new Vector3(-ps.along.y, 0, ps.along.x); // across the creek
                deck.transform.position = new Vector3(ps.at.x, Mathf.Max(Ground(ps.at + new Vector2(normal.x, normal.z) * 5), Ground(ps.at - new Vector2(normal.x, normal.z) * 5)) + .05f, ps.at.y);
                deck.transform.rotation = Quaternion.LookRotation(normal);
                for (int i = -11; i <= 11; i++)
                {
                    var plank = Clone(beamSrc, deck.transform);
                    AlignLong(plank, new Vector3(ps.along.x, 0, ps.along.y));
                    SetCenter(plank, deck.transform.position + normal * (i * .42f) + Vector3.up * .05f);
                }
                var walk = deck.AddComponent<BoxCollider>(); walk.center = Vector3.zero; walk.size = new Vector3(3.6f, .25f, 9.5f);
                deck.SetActive(false); logic.reveal = deck;
            }
        }
        sb.AppendLine("passages " + gateRoot.childCount);
        AddCombatDressing(root, rockPrefabs, woodPile, wallSrc, sb);

        // ---------- Cemetery dressing ----------
        if (graveSrc != null)
        {
            var cem = new GameObject("Cemetery").transform; cem.SetParent(root);
            int g = 0;
            for (int row = 0; row < 4; row++) for (int col = 0; col < 6; col++)
            {
                var p = W(58 + col * 3.2f + Random.Range(-.4f, .4f), -4 - row * 3.6f + Random.Range(-.3f, .3f));
                var stone = Clone(graveSrc, cem);
                stone.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f), 90 + Random.Range(-12f, 12f), Random.Range(-5f, 5f)) * Quaternion.Inverse(Quaternion.Euler(0, 0, 0));
                SetCenter(stone, new Vector3(p.x, 0, p.y)); Seat(stone, p, .12f); g++;
            }
            sb.AppendLine("gravestones " + g);
        }

        // ---------- Areas ----------
        var areaRoot = new GameObject("Areas").transform; areaRoot.SetParent(root);
        foreach (var bl in Blocks)
        {
            var go = new GameObject("Area_" + bl.name.Replace(" ", "_")); go.transform.SetParent(areaRoot);
            var c = Centroid(bl.poly); go.transform.position = new Vector3(c.x, Ground(c), c.y);
            var area = go.AddComponent<ElderboomArea>(); area.areaName = bl.name; area.unlockedAtStart = bl.start; area.polygon = bl.poly; area.size = new Vector3(400, 80, 400);
        }

        // ---------- Stations ----------
        MoveStation("Station_SHOTGUNS", W(-58, -6), 1f, sb);
        MoveStation("Station_THIRD_WEAPON_SLOT", W(52, 6), 0f, sb);
        MoveStation("Station_MELEE", W(12, -71), 1f, sb);
        var melee = GameObject.Find("Station_MELEE");
        var reward = GameObject.Find("Station_REWARD");
        if (reward == null && melee != null) { reward = Object.Instantiate(melee); reward.name = "Station_REWARD"; }
        if (reward != null)
        {
            var st = reward.GetComponent<ElderboomStation>(); st.label = "REWARD · SPECIAL WEAPONS"; st.category = "weapons"; st.cost = 2500; st.weapons = ElderboomSpecialPickup.DropIds.ToArray();
            MoveStation("Station_REWARD", W(75, 62), 1f, sb); EditorUtility.SetDirty(st);
        }
        AddWayfinding(root, gateRoot);
        ClearGeneratedApproaches(root,sb);
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if (player != null) { var pp = new Vector2(player.transform.position.x, player.transform.position.z); player.transform.position = new Vector3(pp.x, Ground(pp) + .03f, pp.y); }

        // ---------- Spawn points ----------
        var spawnRoot = new GameObject("Spawns").transform; spawnRoot.SetParent(root);
        Physics.SyncTransforms();
        foreach (var bl in Blocks)
        {
            var made = new List<Vector2>();
            var mn = new Vector2(bl.poly.Min(v => v.x), bl.poly.Min(v => v.y)); var mx = new Vector2(bl.poly.Max(v => v.x), bl.poly.Max(v => v.y));
            for (int t = 0; t < 6000 && made.Count < bl.spawns; t++)
            {
                var p = new Vector2(Random.Range(mn.x, mx.x), Random.Range(mn.y, mx.y));
                if (!Inside(p, bl.poly) || !Inside(p, Outer) || LineDist(p, bl.poly.Append(bl.poly[0]).ToArray()) < 5f || LineDist(p, Outer) < 7) continue;
                int pi = Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 0, res - 1), pj = Mathf.Clamp(Mathf.RoundToInt((p.y - origin.z) / cell), 0, res - 1);
                if (pad[pj, pi] > .2f || made.Any(m => (m - p).sqrMagnitude < 12 * 12) || PassDist(p) < 8) continue;
                var at = new Vector3(p.x, Ground(p), p.y);
                if (Physics.CheckSphere(at + Vector3.up * 1.1f, .9f, ~0, QueryTriggerInteraction.Ignore)) continue;
                made.Add(p);
                var sp = new GameObject("Spawn_" + bl.name.Replace(" ", "_") + "_" + made.Count); sp.transform.SetParent(spawnRoot);
                sp.transform.position = at + Vector3.up * .05f;
                sp.AddComponent<ElderboomSpawnPoint>().area = bl.name;
            }
            sb.AppendLine("spawns " + bl.name + ": " + made.Count + "/" + bl.spawns);
        }

        // ---------- Navigation ----------
        Physics.SyncTransforms();
        sb.AppendLine(Bake());
        int dropped = 0;
        foreach (var sp in spawnRoot.GetComponentsInChildren<ElderboomSpawnPoint>().ToArray())
        {
            if (NavMesh.SamplePosition(sp.transform.position, out var hit, 1.5f, NavMesh.AllAreas)) sp.transform.position = hit.position + Vector3.up * .05f;
            else { Object.DestroyImmediate(sp.gameObject); dropped++; }
        }
        // Every spawn must reach the square (gates are open in the edit-time bake, closed by carving in game).
        int reach = 0, total = 0; var path = new NavMeshPath();
        var start = player != null ? player.transform.position : new Vector3(C.x, 1, C.y);
        if (NavMesh.SamplePosition(start, out var sh, 3, NavMesh.AllAreas)) start = sh.position;
        foreach (var sp in spawnRoot.GetComponentsInChildren<ElderboomSpawnPoint>()) { total++; if (NavMesh.CalculatePath(sp.transform.position, start, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) reach++; else { sb.AppendLine("  removed unreachable " + sp.name); Object.DestroyImmediate(sp.gameObject); } }
        if (reach < 20) throw new System.InvalidOperationException("Too few reachable spawn points: " + reach);
        sb.AppendLine("spawns off the walkable ground removed " + dropped + "; reach the square " + reach + "/" + total);

        EditorUtility.SetDirty(data); EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        sb.AppendLine("saved");
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    // ---------- Pieces ----------
    static GameObject FindPiece(string prefix)
    {
        var port = GameObject.Find("Elderboom_Village_Port"); if (port == null) return null;
        foreach (Transform c in port.transform) if (c.name.StartsWith(prefix) && c.GetComponentInChildren<Renderer>() != null) return c.gameObject;
        return null;
    }
    static GameObject Clone(GameObject src, Transform parent)
    {
        var go = Object.Instantiate(src); go.name = src.name;
        go.transform.SetParent(parent, true);
        go.transform.localScale = src.transform.lossyScale;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return go;
    }
    static Bounds RB(GameObject go) { var rs = go.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
    static void SetCenter(GameObject go, Vector3 target) { var b = RB(go); go.transform.position += new Vector3(target.x - b.center.x, target.y - b.center.y, target.z - b.center.z); }
    static void Seat(GameObject go, Vector2 p, float sink) { var b = RB(go); go.transform.position += Vector3.up * (Ground(p) - sink - b.min.y); }
    // Turn a piece so its longest horizontal side runs along dir.
    static void AlignLong(GameObject go, Vector3 dir)
    {
        go.transform.rotation = Quaternion.identity * Quaternion.Inverse(Quaternion.identity);
        var mf = go.GetComponentInChildren<MeshFilter>();
        var lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        var s = mf != null ? mf.transform.lossyScale : Vector3.one;
        bool alongX = Mathf.Abs(lb.size.x * s.x) >= Mathf.Abs(lb.size.z * s.z);
        float lenY = Mathf.Abs(lb.size.y * s.y);
        // A beam lying along its local y (tall in the mesh) is laid down.
        if (lenY > Mathf.Max(Mathf.Abs(lb.size.x * s.x), Mathf.Abs(lb.size.z * s.z)) * 1.5f)
            go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0) * Quaternion.Inverse(mf.transform.localRotation);
        else
            go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, alongX ? -90 : 0, 0) * Quaternion.Inverse(mf != null ? mf.transform.localRotation : Quaternion.identity);
    }
    static float Length(GameObject src)
    {
        var mf = src.GetComponentInChildren<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return 2f;
        var b = mf.sharedMesh.bounds; var s = mf.transform.lossyScale;
        return Mathf.Max(Mathf.Abs(b.size.x * s.x), Mathf.Abs(b.size.z * s.z), .5f);
    }
    static int Tile(GameObject src, Transform parent, Vector2[] pts, bool ruined)
    {
        int n = 0; float len = Length(src) * .97f;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; float L = Vector2.Distance(a, b); var dir = (b - a) / L;
            for (float t = len * .5f; t < L; t += len)
            {
                var p = a + dir * t;
                if (PassDist(p) < GapHalf + len * .5f) continue;
                if (ruined && Random.value < .18f) continue;
                var go = Clone(src, parent);
                AlignLong(go, new Vector3(dir.x, 0, dir.y));
                if (ruined) go.transform.rotation = Quaternion.Euler(Random.Range(-4f, 4f), 0, Random.Range(-5f, 5f)) * go.transform.rotation;
                SetCenter(go, new Vector3(p.x, 0, p.y));
                Seat(go, p, ruined ? Random.Range(.05f, .45f) : .05f);
                n++;
            }
        }
        return n;
    }
    static IEnumerable<Vector2> Walk(Vector2[] pts, float step)
    {
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; float L = Vector2.Distance(a, b);
            for (float t = 0; t < L; t += step) yield return Vector2.Lerp(a, b, t / L) + Random.insideUnitCircle * .45f;
        }
        yield return pts[pts.Length - 1];
    }
    static Vector2 OutwardNormal(Vector2 p) { var c = Centroid(Outer); return (p - c).normalized; }
    static Vector2 Centroid(Vector2[] poly) { var s = Vector2.zero; foreach (var v in poly) s += v; return s / poly.Length; }

    // A prefab of roughly the given height, half-sunk, randomly turned; refused where it would cut into a building.
    static bool Place(GameObject prefab, Transform parent, Vector2 p, float height, float sink)
    {
        if (prefab == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.transform.SetParent(parent, true);
        go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        go.transform.position = new Vector3(p.x, 0, p.y);
        var b = RB(go);
        float k = height / Mathf.Max(b.size.y, .1f) * Random.Range(.9f, 1.1f);
        go.transform.localScale = go.transform.localScale * k;
        b = RB(go);
        go.transform.position += new Vector3(p.x - b.center.x, Ground(p) - b.min.y - b.size.y * sink, p.y - b.center.z);
        b = RB(go);
        var shrink = new Vector3(b.extents.x * .7f, b.extents.y * .5f, b.extents.z * .7f);
        foreach (var col in Physics.OverlapBox(b.center + Vector3.up * b.extents.y * .3f, shrink, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col is TerrainCollider || col.transform.IsChildOf(parent.root)) continue;
            if (col.GetComponentInParent<ElderboomGate>() != null) continue;
            Object.DestroyImmediate(go); return false;
        }
        return true;
    }

    // Invisible blocking boxes along a line (raycasts pass through: Ignore Raycast layer), gaps at the passages.
    static void Walls(Transform parent, Vector2[] pts, float height)
    {
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var a = pts[i]; var b = pts[i + 1]; float L = Vector2.Distance(a, b); if (L < .01f) continue; var dir = (b - a) / L;
            var spans = new List<Vector2> { new Vector2(0, L) };
            foreach (var ps in Passes)
            {
                if (SegDist(ps.at, a, b) > .6f) continue;
                float tc = Vector2.Dot(ps.at - a, dir);
                var next = new List<Vector2>();
                foreach (var sp in spans)
                {
                    if (tc - GapHalf > sp.x) next.Add(new Vector2(sp.x, Mathf.Min(sp.y, tc - GapHalf)));
                    if (tc + GapHalf < sp.y) next.Add(new Vector2(Mathf.Max(sp.x, tc + GapHalf), sp.y));
                }
                spans = next;
            }
            foreach (var sp in spans)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((sp.y - sp.x) / 4f));
                for (int k = 0; k < n; k++) Box(parent, a + dir * Mathf.Lerp(sp.x, sp.y, k / (float)n), a + dir * Mathf.Lerp(sp.x, sp.y, (k + 1) / (float)n), height);
            }
        }
    }
    static void Box(Transform parent, Vector2 p0, Vector2 p1, float height)
    {
        float L = Vector2.Distance(p0, p1); if (L < .2f) return;
        var mid = (p0 + p1) * .5f;
        float g = Mathf.Min(Ground(p0), Ground(mid), Ground(p1));
        var go = new GameObject("Wall"); go.transform.SetParent(parent);
        go.layer = 2;
        go.transform.position = new Vector3(mid.x, g - 1f + (height + 1f) * .5f, mid.y);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(p1.x - p0.x, 0, p1.y - p0.y));
        var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(.9f, height + 1f + (Mathf.Max(Ground(p0), Ground(p1)) - g), L + .3f);
        box.center = new Vector3(0, (Mathf.Max(Ground(p0), Ground(p1)) - g) * .5f, 0);
    }

    static void BuildGateLook(Transform gate, GateKind kind, GameObject fence, GameObject beam, GameObject pile, Material metal, GameObject[] bushes, GameObject[] rocks)
    {
        var right = gate.right; var basePos = gate.position + Vector3.up * -1.4f;
        switch (kind)
        {
            case GateKind.Wood:
                {
                    var wood = WoodMaterial();
                    Post(gate, basePos + right * -GapHalf, wood, .24f, 2.5f); Post(gate, basePos + right * GapHalf, wood, .24f, 2.5f);
                    for (float x = -GapHalf + .35f; x < GapHalf - .2f; x += .42f) Post(gate, basePos + right * x + Vector3.up * .1f, wood, .13f, 1.85f + .1f * Mathf.Sin(x * 3f));
                    foreach (var h in new[] { .45f, 1.55f }) Rail(gate, basePos + Vector3.up * h - gate.forward * .1f, right, GapHalf * 2, wood, .14f);
                    var brace = GameObject.CreatePrimitive(PrimitiveType.Cube); brace.name = "Brace"; Object.DestroyImmediate(brace.GetComponent<Collider>());
                    brace.transform.SetParent(gate, true); brace.transform.position = basePos + Vector3.up * 1f - gate.forward * .12f;
                    brace.transform.rotation = Quaternion.LookRotation(right) * Quaternion.Euler(-19f, 0, 0); brace.transform.localScale = new Vector3(.1f, .14f, GapHalf * 2.05f);
                    if (wood != null) brace.GetComponent<Renderer>().sharedMaterial = wood;
                }
                break;
            case GateKind.Logs:
                if (beam != null) for (int i = 0; i < 6; i++)
                    {
                        var l = Clone(beam, gate); AlignLong(l, right);
                        l.transform.rotation = Quaternion.AngleAxis(Random.Range(-14f, 14f), gate.forward) * l.transform.rotation;
                        SetCenter(l, basePos + right * Random.Range(-.6f, .6f) + Vector3.up * (.35f + i * .38f) + gate.forward * Random.Range(-.25f, .25f));
                    }
                if (pile != null) { var p = Clone(pile, gate); SetCenter(p, basePos + right * (GapHalf - .6f) + gate.forward * .9f); var b = RB(p); p.transform.position += Vector3.up * (basePos.y - b.min.y); }
                break;
            case GateKind.Iron:
                for (float x = -GapHalf + .15f; x <= GapHalf - .1f; x += .24f) Post(gate, basePos + right * x, metal, .05f, 2.5f + .25f * Mathf.Sin(x * 1.3f));
                foreach (var h in new[] { .35f, 1.3f, 2.25f }) Rail(gate, basePos + Vector3.up * h, right, GapHalf * 2, metal);
                Post(gate, basePos + right * -GapHalf, metal, .3f, 2.9f); Post(gate, basePos + right * GapHalf, metal, .3f, 2.9f);
                break;
            case GateKind.Bridge:
                if (beam != null) for (int i = 0; i < 5; i++)
                    {
                        var l = Clone(beam, gate); AlignLong(l, gate.forward);
                        l.transform.rotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(-30f, 30f), Random.Range(-20f, 20f)) * l.transform.rotation;
                        SetCenter(l, basePos + right * Random.Range(-2f, 2f) + gate.forward * Random.Range(-2.5f, 2.5f) + Vector3.up * Random.Range(-.3f, .4f));
                    }
                Post(gate, basePos + right * -GapHalf + gate.forward * 3.2f, null, .25f, 1.4f); Post(gate, basePos + right * GapHalf + gate.forward * 3.2f, null, .25f, 1.4f);
                Post(gate, basePos + right * -GapHalf - gate.forward * 3.2f, null, .25f, 1.1f); Post(gate, basePos + right * GapHalf - gate.forward * 3.2f, null, .25f, .8f);
                break;
            case GateKind.Brush:
                for (int i = 0; i < 6 && bushes.Length > 0; i++) Place(bushes[Random.Range(0, bushes.Length)], gate, new Vector2(basePos.x, basePos.z) + new Vector2(right.x, right.z) * Random.Range(-GapHalf, GapHalf) + Random.insideUnitCircle * .5f, Random.Range(2.2f, 3f), .05f);
                if (beam != null) { var l = Clone(beam, gate); AlignLong(l, right); l.transform.rotation = Quaternion.AngleAxis(8, gate.forward) * l.transform.rotation; SetCenter(l, basePos + Vector3.up * .5f); }
                break;
        }
    }
    static void Post(Transform parent, Vector3 basePos, Material m, float w, float h)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "Post"; Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(parent, true); c.transform.position = basePos + Vector3.up * h * .5f; c.transform.rotation = parent.rotation; c.transform.localScale = new Vector3(w, h, w);
        if (m != null) c.GetComponent<Renderer>().sharedMaterial = m;
        else { var src = FindPiece("S_Worn_Wooden_Beam"); var r = src != null ? src.GetComponentInChildren<Renderer>() : null; if (r != null) c.GetComponent<Renderer>().sharedMaterial = r.sharedMaterial; }
    }
    static Material WoodMaterial() { var src = FindPiece("S_Worn_Wooden_Beam"); var r = src != null ? src.GetComponentInChildren<Renderer>() : null; return r != null ? r.sharedMaterial : null; }
    static void Rail(Transform parent, Vector3 center, Vector3 dir, float len, Material m, float thick = .07f)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "Rail"; Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(parent, true); c.transform.position = center; c.transform.rotation = Quaternion.LookRotation(dir); c.transform.localScale = new Vector3(thick * .85f, thick, len);
        if (m != null) c.GetComponent<Renderer>().sharedMaterial = m;
    }

    static void MoveStation(string name, Vector2 p, float lift, StringBuilder sb)
    {
        var go = GameObject.Find(name); if (go == null) { sb.AppendLine("station missing " + name); return; }
        go.transform.position = new Vector3(p.x, Ground(p) + lift, p.y);
        EditorUtility.SetDirty(go.transform);
        sb.AppendLine("station " + name + " -> " + go.transform.position.ToString("F1"));
    }

    internal static string BakeAccessNavigation()
    {
        terrain = Object.FindObjectsByType<Terrain>().Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();
        return Bake();
    }

    static string Bake()
    {
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = terrain.terrainData, transform = terrain.transform.localToWorldMatrix, area = 0 }
        };
        // The visible creek surface is water, not walkable ground. This replaces
        // the old tall invisible barrier; the raised bridge remains traversable.
        var creekSurface=GameObject.Find("Creek_Continuous_Surface");
        if(creekSurface && creekSurface.GetComponent<MeshFilter>()?.sharedMesh is Mesh creekMesh)
            sources.Add(new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Mesh, sourceObject=creekMesh, transform=creekSurface.transform.localToWorldMatrix, area=1 });
        // Bake the eventual bridge surface even while the purchase hides it.
        foreach (var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Exclude))
            if (gate.reveal != null && !gate.reveal.activeInHierarchy)
                foreach (var box in gate.reveal.GetComponentsInChildren<BoxCollider>(true))
                    sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), size = box.size, area = 0 });
        foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude))
        {
            if (collider.sharedMesh == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.GetComponent<Terrain>() != null || collider.GetComponentInParent<ElderboomGateLeaves>() != null) continue;
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = collider.sharedMesh, transform = collider.transform.localToWorldMatrix, area = 0 });
        }
        foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (!box.enabled || !box.gameObject.activeInHierarchy || box.GetComponent<ElderboomGate>() != null || box.GetComponentInParent<ElderboomGateLeaves>() != null || box.isTrigger) continue;
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), size = box.size, area = 0 });
        }
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = .4f; settings.agentHeight = 1.8f; settings.agentClimb = .35f; settings.agentSlope = 32f;
        var nav = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(new Vector3(C.x, 4f, C.y), new Vector3(300f, 40f, 300f)), Vector3.zero, Quaternion.identity);
        if (nav == null) return "FAIL navigation bake";
        AssetDatabase.DeleteAsset(NavPath); AssetDatabase.CreateAsset(nav, NavPath);
        var holder = Object.FindAnyObjectByType<ElderboomNavigation>();
        if (holder == null) holder = new GameObject("Elderboom_Navigation").AddComponent<ElderboomNavigation>();
        holder.data = nav; EditorUtility.SetDirty(holder);
        NavMesh.RemoveAllNavMeshData(); NavMesh.AddNavMeshData(nav);
        var tri = NavMesh.CalculateTriangulation();
        return "navigation baked: sources " + sources.Count + ", walkable triangles " + tri.indices.Length / 3;
    }

    // ---------- Pictures ----------
    static string Shots()
    {
        var sb = new StringBuilder();
        var light = new GameObject("ShotSun"); var l = light.AddComponent<Light>(); l.type = LightType.Directional; light.AddComponent<HDAdditionalLightData>();
        l.intensity = 8f; light.transform.rotation = Quaternion.Euler(55, 30, 0);
        void Render(string file, Vector3 pos, Quaternion rot, bool ortho, float sizeOrFov, int w, int h)
        {
            var go = new GameObject("BlocksShotCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.orthographic = ortho; if (ortho) cam.orthographicSize = sizeOrFov; else cam.fieldOfView = sizeOrFov;
            cam.nearClipPlane = .1f; cam.farClipPlane = 1200; go.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            for (int i = 0; i < 4; i++) cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToJPG(88)); RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            sb.AppendLine(file);
        }
        Render("Logs/blocks_top.jpg", new Vector3(C.x, 220, C.y), Quaternion.Euler(90, 0, 0), true, 130, 2048, 2048);
        int n = 0;
        foreach (var ps in Passes)
        {
            var across = new Vector2(-ps.along.y, ps.along.x);
            // stand on the side nearer the village centre, look across the passage
            var side = Vector2.Dot(C - ps.at, across) > 0 ? across : -across;
            var eye = ps.at + side * 8f;
            for (int t = 0; t < 12; t++)
            {
                var test = new Vector3(eye.x, Ground(eye) + 1.7f, eye.y);
                if (!Physics.CheckSphere(test, .6f, ~0 & ~(1 << 2), QueryTriggerInteraction.Ignore)) break;
                eye = ps.at + side * (8f - t * .5f) + ps.along * ((t % 2 == 0 ? 1 : -1) * t * .6f);
            }
            var pos = new Vector3(eye.x, Ground(eye) + 1.9f, eye.y);
            var target = new Vector3(ps.at.x, Ground(ps.at) + 1.2f, ps.at.y);
            Render("Logs/blocks_pass_" + (n++) + ".jpg", pos, Quaternion.LookRotation(target - pos), false, 65, 1280, 720);
        }
        foreach (var v in new[] { new Vector2(-20, -10), new Vector2(60, -20), new Vector2(-70, -30) })
        {
            var p = W(v.x, v.y); var pos = new Vector3(p.x, Ground(p) + 1.7f, p.y);
            var look = (new Vector3(C.x, 1, C.y) - pos); look.y = 0;
            Render("Logs/blocks_view_" + (n++) + ".jpg", pos, Quaternion.LookRotation(-look.normalized + Vector3.down * .05f), false, 70, 1280, 720);
        }
        Object.DestroyImmediate(light);
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
