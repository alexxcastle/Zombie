using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// The ground of the whole map brought to the autumn reference: wet photoscanned cobbles on every paved strip, terrain
// layers with height-based blending (grass, gravelly dirt along the paving, leaf litter under the trees, dark soil
// patches), soft warped borders between the layers, dirt creeping over the paving edges, fallen leaves (scattered,
// in drifts against walls, under trees) and puddles on the streets.
// Batch: -executeMethod ElderboomDusk.GroundBatch     (safe to run again: it rebuilds its own objects and layers)
static partial class ElderboomDusk
{
    const string GT = D + "/Downloaded/Textures/";
    const int GN = 2048;                       // alphamap / mask resolution of the valley (0.34 m per texel)
    const int LEAF = 3, SOIL = 4, PATH = 6, SNOW = 2;   // terrain layer slots (3, 4, 6 were unused; 2 is the snow)

    public static void GroundBatch() { Open(); Ground(); Save("ground"); }

    static Texture2D MaskTex(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) { log.AppendLine("  missing texture " + path); return null; }
        if (ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Texture2D CutoutTex(string name)
    {
        string path = D + "/Ground/" + name + ".png";
        var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) { log.AppendLine("  missing texture " + path); return null; }
        if (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp) { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static TerrainLayer GLayer(string name, string id, float tile, float normalScale, Color tint)
    {
        string lp = D + "/" + name + ".terrainlayer"; var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
        if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, lp); }
        l.diffuseTexture = Tex(GT + id + "/" + id + "_diff_2k.jpg"); l.normalMapTexture = Tex(GT + id + "/" + id + "_nor_gl_2k.jpg", true); l.maskMapTexture = MaskTex(GT + id + "/" + id + "_mask.png");
        l.tileSize = new Vector2(tile, tile); l.normalScale = normalScale; l.metallic = 0; l.smoothness = .2f;
        l.diffuseRemapMin = Vector4.zero; l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1);
        l.maskMapRemapMin = Vector4.zero; l.maskMapRemapMax = Vector4.one;
        EditorUtility.SetDirty(l); return l;
    }
    static Material CobbleMat(string name, float tile, Color tint)
    {
        string cb = GT + "cobblestone_floor_08/cobblestone_floor_08"; var m = Mat(name, "HDRP/Lit");
        m.SetTexture("_BaseColorMap", Tex(cb + "_diff_2k.jpg")); m.SetTexture("_NormalMap", Tex(cb + "_nor_gl_2k.jpg", true)); m.SetTexture("_MaskMap", MaskTex(cb + "_mask.png"));
        m.SetColor("_BaseColor", tint); m.SetFloat("_NormalScale", 1.35f); m.SetFloat("_Metallic", 0); m.SetFloat("_Smoothness", .6f);
        m.SetFloat("_SmoothnessRemapMin", .05f); m.SetFloat("_SmoothnessRemapMax", 1f); m.SetFloat("_AORemapMin", .25f); m.SetFloat("_AORemapMax", 1f);
        m.SetTextureScale("_BaseColorMap", new Vector2(tile, tile));
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }
    // distance (in cells) from every cell to the nearest cell where src is true
    static float[,] Chamfer(bool[,] src, int n)
    {
        var d = new float[n, n]; const float Big = 1e6f, Dg = 1.41421f;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) d[y, x] = src[y, x] ? 0 : Big;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float v = d[y, x]; if (v == 0) continue;
                if (x > 0) v = Mathf.Min(v, d[y, x - 1] + 1);
                if (y > 0) { v = Mathf.Min(v, d[y - 1, x] + 1); if (x > 0) v = Mathf.Min(v, d[y - 1, x - 1] + Dg); if (x < n - 1) v = Mathf.Min(v, d[y - 1, x + 1] + Dg); }
                d[y, x] = v;
            }
        for (int y = n - 1; y >= 0; y--) for (int x = n - 1; x >= 0; x--)
            {
                float v = d[y, x]; if (v == 0) continue;
                if (x < n - 1) v = Mathf.Min(v, d[y, x + 1] + 1);
                if (y < n - 1) { v = Mathf.Min(v, d[y + 1, x] + 1); if (x < n - 1) v = Mathf.Min(v, d[y + 1, x + 1] + Dg); if (x > 0) v = Mathf.Min(v, d[y + 1, x - 1] + Dg); }
                d[y, x] = v;
            }
        return d;
    }
    static float SStep(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));

    static void Ground()
    {
        var gr = new System.Random(4242); float R(float a, float b) => a + (float)gr.NextDouble() * (b - a);
        var t = Valley(); var td = t.terrainData; var pos = t.transform.position; var size = td.size;
        const int N = GN; float cell = size.x / (N - 1f);
        log.AppendLine("ground: valley " + size + " alphamap " + td.alphamapResolution + " layers " + td.alphamapLayers);

        // ---------------------------------------------------------- 1. paving: wet cobbles on every strip and patch
        var roadMat = CobbleMat("Dusk_Road_Cobble", 1.35f, new Color(.70f, .67f, .63f));
        var pebbleMat = CobbleMat("Dusk_Pebble_Cobble", 1.8f, new Color(.74f, .70f, .65f));
        var pav = new float[N, N]; for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) pav[y, x] = -999f;
        int strips = 0, patches = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled || !mf.sharedMesh) continue;
            bool road = mf.name.StartsWith("Road_"), pebble = mf.name.StartsWith("Pebble_") || (mf.transform.parent && mf.transform.parent.name == "Pebble_Paving");
            if (!road && !pebble) continue;
            if (mr.bounds.size.y > 4f) { log.AppendLine("  skipped (not flat) " + mf.name); continue; }
            mr.sharedMaterial = road ? roadMat : pebbleMat; EditorUtility.SetDirty(mr); if (road) strips++; else patches++;
            var m = mf.sharedMesh; if (!m.isReadable) { log.AppendLine("  mesh not readable (no edge blending there): " + mf.name); continue; }
            var vs = m.vertices; var tr = m.triangles; var l2w = mf.transform.localToWorldMatrix;
            var gx = new float[vs.Length]; var gz = new float[vs.Length]; var gy = new float[vs.Length];
            for (int i = 0; i < vs.Length; i++) { var w = l2w.MultiplyPoint3x4(vs[i]); gx[i] = (w.x - pos.x) / cell; gz[i] = (w.z - pos.z) / cell; gy[i] = w.y; }
            for (int i = 0; i + 2 < tr.Length; i += 3)
            {
                float ax = gx[tr[i]], az = gz[tr[i]], bx = gx[tr[i + 1]], bz = gz[tr[i + 1]], cx = gx[tr[i + 2]], cz = gz[tr[i + 2]];
                float area = (bx - ax) * (cz - az) - (cx - ax) * (bz - az); if (Mathf.Abs(area) < 1e-6f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)))), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(az, Mathf.Min(bz, cz)))), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(az, Mathf.Max(bz, cz))));
                for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                    {
                        float w0 = ((bx - xx) * (cz - yy) - (cx - xx) * (bz - yy)) / area, w1 = ((cx - xx) * (az - yy) - (ax - xx) * (cz - yy)) / area, w2 = 1 - w0 - w1;
                        if (w0 < -.03f || w1 < -.03f || w2 < -.03f) continue;
                        pav[yy, xx] = Mathf.Max(pav[yy, xx], w0 * gy[tr[i]] + w1 * gy[tr[i + 1]] + w2 * gy[tr[i + 2]]);
                    }
            }
        }
        var isPav = new bool[N, N]; var notPav = new bool[N, N]; int pavCells = 0;
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { isPav[y, x] = pav[y, x] > -900f; notPav[y, x] = !isPav[y, x]; if (isPav[y, x]) pavCells++; }
        var dOut = Chamfer(isPav, N); var dIn = Chamfer(notPav, N);     // cells to the paving / cells to the paving's border
        log.AppendLine("  paving: " + strips + " road strips, " + patches + " pebble meshes, " + (pavCells * cell * cell).ToString("F0") + " m2");

        // ---------------------------------------------------------- 2. where the trees stand (leaf litter collects there)
        var tf = new float[N, N]; int treeCount = 0;
        void Tree(float wx, float wz, float rad)
        {
            float cxg = (wx - pos.x) / cell, czg = (wz - pos.z) / cell; int rr = Mathf.CeilToInt(rad / cell); treeCount++;
            for (int yy = Mathf.Max(0, (int)czg - rr); yy <= Mathf.Min(N - 1, (int)czg + rr); yy++) for (int xx = Mathf.Max(0, (int)cxg - rr); xx <= Mathf.Min(N - 1, (int)cxg + rr); xx++)
                { float dd = Mathf.Sqrt((xx - cxg) * (xx - cxg) + (yy - czg) * (yy - czg)) * cell / rad; if (dd < 1) tf[yy, xx] = Mathf.Min(1.6f, tf[yy, xx] + (1 - dd) * (1 - dd)); }
        }
        foreach (var ti in td.treeInstances) Tree(pos.x + ti.position.x * size.x, pos.z + ti.position.z * size.z, 3.6f + 2.2f * ti.widthScale);
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if ((tr.name.StartsWith("Cypress") || tr.name.StartsWith("SM_BlackAlder")) && (tr.parent == null || !(tr.parent.name.StartsWith("Cypress") || tr.parent.name.StartsWith("SM_BlackAlder")))) Tree(tr.position.x, tr.position.z, 5f);
        log.AppendLine("  trees: " + treeCount);

        // ---------------------------------------------------------- 3. terrain layers and the splat map
        var layers = td.terrainLayers;
        if (layers.Length < 8) { log.AppendLine("  valley has " + layers.Length + " layers, expected 8: terrain painting skipped"); }
        else
        {
            layers[0] = GLayer("Valley_Grass", "forrest_ground_01", 4.2f, 1f, new Color(.80f, .78f, .70f));
            layers[LEAF] = GLayer("Valley_Leaves", "leaves_forest_ground", 2.6f, 1f, new Color(.92f, .86f, .78f));
            layers[SOIL] = GLayer("Valley_Soil", "brown_mud_leaves_01", 3.8f, 1f, new Color(.74f, .70f, .64f));
            layers[PATH] = GLayer("Valley_Path", "grass_path_2", 3.2f, 1.1f, new Color(.78f, .73f, .66f));
            td.terrainLayers = layers;
            int ow = td.alphamapWidth, L = td.alphamapLayers; var old = td.GetAlphamaps(0, 0, ow, ow); bool first = ow != N;
            if (first) td.alphamapResolution = N;
            var a = new float[N, N, L]; var s = new float[L]; var villageC = new Vector2(8, -12);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (N - 1f), v = y / (N - 1f), wx = pos.x + u * size.x, wz = pos.z + v * size.z;
                    if (first)
                    {   // old 1.4 m blocks -> soft, irregular borders: bilinear + a small warp of where we read
                        float sx = wx + (Mathf.PerlinNoise(wx * .19f + 7, wz * .19f + 3) - .5f) * 3.4f + (Mathf.PerlinNoise(wx * .9f + 1, wz * .9f + 9) - .5f) * .7f;
                        float sz = wz + (Mathf.PerlinNoise(wx * .19f + 31, wz * .19f + 17) - .5f) * 3.4f + (Mathf.PerlinNoise(wx * .9f + 5, wz * .9f + 2) - .5f) * .7f;
                        float fx = Mathf.Clamp((sx - pos.x) / size.x * (ow - 1), 0, ow - 1.001f), fy = Mathf.Clamp((sz - pos.z) / size.z * (ow - 1), 0, ow - 1.001f);
                        int ix = (int)fx, iy = (int)fy; float tx = fx - ix, ty = fy - iy;
                        for (int l = 0; l < L; l++) s[l] = Mathf.Lerp(Mathf.Lerp(old[iy, ix, l], old[iy, ix + 1, l], tx), Mathf.Lerp(old[iy + 1, ix, l], old[iy + 1, ix + 1, l], tx), ty);
                    }
                    else for (int l = 0; l < L; l++) s[l] = old[y, x, l];
                    s[0] += s[LEAF] + s[SOIL] + s[PATH]; s[LEAF] = s[SOIL] = s[PATH] = 0;      // (re-run) back to the base

                    float n1 = Mathf.PerlinNoise(wx * .035f + 11, wz * .035f + 23), n2 = Mathf.PerlinNoise(wx * .13f + 41, wz * .13f + 7), n3 = Mathf.PerlinNoise(wx * .55f + 3, wz * .55f + 61);
                    float steep = td.GetSteepness(u, v);
                    // no snow lying in the village (as in the reference): it starts on the meadows around it
                    float dv = Vector2.Distance(new Vector2(wx, wz), villageC); float keepSnow = SStep(60f, 125f, dv + (n2 - .5f) * 30f); s[0] += s[SNOW] * (1 - keepSnow); s[SNOW] *= keepSnow;

                    float dm = dOut[y, x] * cell;                                                // metres to the nearest paving
                    float path = dm > 9f ? 0 : 1 - SStep(.15f, 1.3f + 3.0f * n2 + 1.2f * n3, dm);
                    float leaf = SStep(.3f, .72f, tf[y, x] * .85f + (n3 - .5f) * .55f + (n1 - .5f) * .3f) * .95f;
                    leaf = Mathf.Max(leaf, SStep(.60f, .78f, n2 * .55f + n3 * .45f) * .55f);     // loose litter everywhere
                    leaf *= 1 - SStep(26f, 40f, steep);
                    float soil = SStep(.50f, .70f, n1 * .65f + n2 * .35f) * .8f;

                    float leafW = s[0] * leaf, rem = s[0] - leafW, pathW = rem * path; rem -= pathW; float soilW = rem * soil; rem -= soilW;
                    for (int l = 1; l < L; l++)
                    {   // the moss and rock layers also give way to the dirt band and to the litter
                        if (l == SNOW || l == LEAF || l == SOIL || l == PATH || l == 5) continue;
                        float tp = s[l] * path * .75f; s[l] -= tp; pathW += tp; float tl = s[l] * leaf * .6f; s[l] -= tl; leafW += tl;
                    }
                    s[0] = rem; s[LEAF] = leafW; s[SOIL] = soilW; s[PATH] = pathW;
                    float sum = 0; for (int l = 0; l < L; l++) sum += s[l]; if (sum < 1e-4f) { s[0] = 1; sum = 1; }
                    for (int l = 0; l < L; l++) a[y, x, l] = s[l] / sum;
                }
            td.SetAlphamaps(0, 0, a); EditorUtility.SetDirty(td);
            var cov = new double[L]; for (int y = 0; y < N; y += 2) for (int x = 0; x < N; x += 2) for (int l = 0; l < L; l++) cov[l] += a[y, x, l];
            log.AppendLine("  splat " + (first ? "resampled " + ow + " -> " + N : "repainted at " + N) + ", coverage " + string.Join(" ", cov.Select((c, i) => i + ":" + (c / (N * N / 4)).ToString("F3"))));

            // height-based blending between the layers (stones and leaves poke through the dirt instead of fading)
            var tm = t.materialTemplate; string mp = D + "/Valley_Ground.mat";
            if (tm != null)
            {
                if (AssetDatabase.GetAssetPath(tm) != mp)
                {
                    if (AssetDatabase.LoadAssetAtPath<Material>(mp) == null) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(tm), mp);
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(mp); if (copy != null) { tm = copy; t.materialTemplate = tm; EditorUtility.SetDirty(t); }
                }
                if (tm.HasProperty("_EnableHeightBlend")) { tm.SetFloat("_EnableHeightBlend", 1f); tm.EnableKeyword("_TERRAIN_BLEND_HEIGHT"); }
                if (tm.HasProperty("_HeightTransition")) tm.SetFloat("_HeightTransition", .14f);
                HDMaterial.ValidateMaterial(tm); EditorUtility.SetDirty(tm);
                log.AppendLine("  terrain material " + AssetDatabase.GetAssetPath(tm) + " shader " + tm.shader.name + " heightBlend " + tm.HasProperty("_EnableHeightBlend"));
            }
        }

        // ---------------------------------------------------------- 4. decals: dirt over the paving edges, leaves, puddles
        var oldRoot = GameObject.Find("Dusk_Ground"); if (oldRoot) Object.DestroyImmediate(oldRoot);
        var root = new GameObject("Dusk_Ground").transform;
        var gEdges = Group("Edge_Dirt", Vector3.zero, 0, root); var gLeaves = Group("Leaves", Vector3.zero, 0, root); var gPuddles = Group("Puddles", Vector3.zero, 0, root);
        var flatN = Tex(D + "/flat_normal.png", true);
        var dirt = Enumerable.Range(0, 4).Select(i => Decal("Dusk_Decal_EdgeDirt" + i, CutoutTex("edge_dirt" + i), flatN, .16f, 1f)).ToArray();
        var scatter = Enumerable.Range(0, 4).Select(i => Decal("Dusk_Decal_Leaves" + i, CutoutTex("leaves_scatter" + i), flatN, .24f, 1f)).ToArray();
        var pile = Enumerable.Range(0, 3).Select(i => Decal("Dusk_Decal_LeafPile" + i, CutoutTex("leaves_pile" + i), flatN, .2f, 1f)).ToArray();
        var puddle = Enumerable.Range(0, 3).Select(i => AssetDatabase.LoadAssetAtPath<Material>(D + "/Dusk_Decal_Puddle" + i + ".mat")).Where(m => m != null).ToArray();

        float WX(int x) => pos.x + x * cell; float WZ(int y) => pos.z + y * cell;
        float SurfY(float wx, float wz)
        {
            float g = GroundY(wx, wz); int x = Mathf.Clamp(Mathf.RoundToInt((wx - pos.x) / cell), 0, N - 1), y = Mathf.Clamp(Mathf.RoundToInt((wz - pos.z) / cell), 0, N - 1);
            return pav[y, x] > -900f ? Mathf.Max(g, pav[y, x]) : g;
        }
        void Put(Material m, float wx, float wz, float w, float l, Transform parent, float draw)
        {
            if (m == null) return;
            var go = new GameObject("D_" + m.name); go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(wx, SurfY(wx, wz) + .12f, wz); go.transform.rotation = Quaternion.Euler(90, R(0, 360), 0);
            var d = go.AddComponent<DecalProjector>(); d.material = m; d.size = new Vector3(w, l, .8f); d.pivot = new Vector3(0, 0, .4f); d.drawDistance = draw; d.fadeScale = .75f;
        }
        // candidates inside the played area, shuffled, then thinned to one per grid cell of "spacing" metres
        int bx0 = Mathf.Clamp(Mathf.RoundToInt((-125f - pos.x) / cell), 1, N - 2), bx1 = Mathf.Clamp(Mathf.RoundToInt((135f - pos.x) / cell), 1, N - 2);
        int bz0 = Mathf.Clamp(Mathf.RoundToInt((-135f - pos.z) / cell), 1, N - 2), bz1 = Mathf.Clamp(Mathf.RoundToInt((125f - pos.z) / cell), 1, N - 2);
        List<Vector2Int> Pick(System.Func<int, int, bool> ok, float spacing, int cap, int stride = 1)
        {
            var c = new List<Vector2Int>();
            for (int y = bz0; y <= bz1; y += stride) for (int x = bx0; x <= bx1; x += stride) if (ok(x, y)) c.Add(new Vector2Int(x, y));
            for (int i = c.Count - 1; i > 0; i--) { int j = gr.Next(i + 1); var tmp = c[i]; c[i] = c[j]; c[j] = tmp; }
            var used = new HashSet<long>(); var res = new List<Vector2Int>();
            foreach (var p in c)
            {
                long key = (long)Mathf.FloorToInt(WX(p.x) / spacing + 4096) * 100003L + Mathf.FloorToInt(WZ(p.y) / spacing + 4096);
                if (!used.Add(key)) continue; res.Add(p); if (res.Count >= cap) break;
            }
            return res;
        }
        bool Edge(int x, int y) => isPav[y, x] && (!isPav[y, x - 1] || !isPav[y, x + 1] || !isPav[y - 1, x] || !isPav[y + 1, x]);

        // dirt and gravel lapping over the border of every paved strip: no more straight cut between stones and soil
        var edges = Pick(Edge, 2.7f, 620);
        foreach (var p in edges) Put(dirt[gr.Next(dirt.Length)], WX(p.x) + R(-.5f, .5f), WZ(p.y) + R(-.5f, .5f), R(2.8f, 4.8f), R(2.8f, 4.8f), gEdges, 60);
        // leaves blown to the sides of the streets
        var sideLeaves = Pick(Edge, 4.6f, 300);
        foreach (var p in sideLeaves) Put(scatter[gr.Next(3)], WX(p.x) + R(-.7f, .7f), WZ(p.y) + R(-.7f, .7f), R(2.4f, 3.8f), R(2.4f, 3.8f), gLeaves, 45);
        // thicker under the trees that overhang the paving
        var underTrees = Pick((x, y) => tf[y, x] > .45f && dOut[y, x] * cell < 2.5f, 3.2f, 260, 2);
        foreach (var p in underTrees) Put(scatter[gr.Next(2)], WX(p.x) + R(-.5f, .5f), WZ(p.y) + R(-.5f, .5f), R(2.8f, 4.4f), R(2.8f, 4.4f), gLeaves, 45);
        // a few single leaves in the middle of the streets
        var loose = Pick((x, y) => isPav[y, x] && dIn[y, x] * cell > .7f, 5.5f, 220, 2);
        foreach (var p in loose) Put(scatter[2 + gr.Next(2)], WX(p.x), WZ(p.y), R(3f, 4.6f), R(3f, 4.6f), gLeaves, 40);
        // drifts where the wind stops: against walls, fences and steps near the streets
        Physics.SyncTransforms(); int drifts = 0;
        var nearPaving = Pick((x, y) => dOut[y, x] * cell < 4.5f, 3.4f, 1400, 2);
        var driftCells = new HashSet<long>();
        foreach (var p in nearPaving)
        {
            if (drifts >= 190) break;
            float wx = WX(p.x), wz = WZ(p.y); var o = new Vector3(wx, SurfY(wx, wz) + .4f, wz); RaycastHit best = default; float bd = 99;
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0, k * 45f, 0) * Vector3.forward;
                if (Physics.Raycast(o, dir, out var hit, 2.6f, ~0, QueryTriggerInteraction.Ignore) && !(hit.collider is TerrainCollider) && Mathf.Abs(hit.normal.y) < .35f && hit.distance < bd) { bd = hit.distance; best = hit; }
            }
            if (bd > 90) continue;
            var q = best.point + new Vector3(best.normal.x, 0, best.normal.z).normalized * R(.35f, .6f);
            long key = (long)Mathf.FloorToInt(q.x / 2.6f + 4096) * 100003L + Mathf.FloorToInt(q.z / 2.6f + 4096); if (!driftCells.Add(key)) continue;
            Put(pile[gr.Next(pile.Length)], q.x, q.z, R(1.4f, 2.7f), R(1.4f, 2.7f), gLeaves, 45);
            if (gr.NextDouble() < .6) Put(scatter[gr.Next(2)], q.x + best.normal.x * .8f, q.z + best.normal.z * .8f, R(2.6f, 3.8f), R(2.6f, 3.8f), gLeaves, 40);
            drifts++;
        }
        // puddles in the hollows of the streets
        var pools = puddle.Length == 0 ? new List<Vector2Int>() : Pick((x, y) => isPav[y, x] && dIn[y, x] * cell > .9f, 8.5f, 110, 2);
        foreach (var p in pools) Put(puddle[gr.Next(puddle.Length)], WX(p.x), WZ(p.y), R(1.2f, 3.0f), R(.9f, 2.1f), gPuddles, 70);

        log.AppendLine("  decals: edge dirt " + edges.Count + ", side leaves " + sideLeaves.Count + ", under trees " + underTrees.Count + ", loose " + loose.Count + ", drifts " + drifts + ", puddles " + pools.Count);
    }
}
