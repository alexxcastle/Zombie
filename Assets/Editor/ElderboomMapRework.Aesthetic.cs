using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Aesthetic pass: church precinct (front square, stall wings, rear courtyard with fountain), grey stone bridge,
// taller creek bank walls, lamps along the streets, discontinuous low walls / fences, pebble streets, filled empty lots.
static partial class ElderboomMapRework
{
    const string AesMarker = "Rework_Aesthetic_v1";
    const string AesB = "Logs/backup_aesthetic/";
    const string BridgeMatPath = "Assets/GameAssets/Environment/BridgeCemetery/wide_stone_bridge_metric/WideBridge_Stone_HDRP.mat";

    [MenuItem("Elderboom HDRP/Map Rework/31u Undo Aesthetic Pass")]
    static void AestheticUndo()
    {
        File.Copy(AesB + "Elderboom_Unity_Port.unity", "Assets/Scenes/Elderboom_Unity_Port.unity", true);
        File.Copy(AesB + "Elderboom_Valley.asset", "Assets/Terrain/Elderboom_Valley.asset", true);
        File.Copy(AesB + "Navigation.asset", "Assets/Resources/Elderboom/Navigation.asset", true);
        File.Copy(AesB + "WideBridge_Stone_HDRP.mat", BridgeMatPath, true);
        foreach (var p in new[] { BridgeMatPath, "Assets/Terrain/Elderboom_Valley.asset", "Assets/Resources/Elderboom/Navigation.asset", "Assets/Scenes/Elderboom_Unity_Port.unity" }) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
    }

    [MenuItem("Elderboom HDRP/Map Rework/31 Aesthetic Pass Apply")]
    static void AestheticApply()
    {
        var sb = new StringBuilder(); var scene = EditorSceneManager.GetActiveScene();
        void Done() => File.WriteAllText("Logs/aesthetic.txt", sb.ToString());
        if (GameObject.Find(AesMarker) != null) { sb.AppendLine("already applied"); Done(); return; }
        try { AestheticRun(sb, scene); }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        Done();
    }

    [MenuItem("Elderboom HDRP/Map Rework/31b Redo Street Borders + Lot Fill")]
    static void AestheticRedo()
    {
        var sb = new StringBuilder(); var scene = EditorSceneManager.GetActiveScene();
        try { AestheticRun(sb, scene, true); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText("Logs/aesthetic_redo.txt", sb.ToString());
    }

    static void AestheticRun(StringBuilder sb, UnityEngine.SceneManagement.Scene scene, bool redo = false)
    {
        var terrain = MainTerrain(); var td = terrain.terrainData; var tpos = terrain.transform.position;
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + tpos.y;
        if (!redo) { Directory.CreateDirectory(AesB);
        File.Copy(scene.path, AesB + System.IO.Path.GetFileName(scene.path), true);
        File.Copy(AssetDatabase.GetAssetPath(td), AesB + System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(td)), true);
        File.Copy("Assets/Resources/Elderboom/Navigation.asset", AesB + "Navigation.asset", true);
        File.Copy(BridgeMatPath, AesB + "WideBridge_Stone_HDRP.mat", true); }
        Directory.CreateDirectory("Assets/Resources/Elderboom/Aesthetic");
        var rnd = new System.Random(20261111);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Physics.SyncTransforms();

        var rootGo = GameObject.Find(AesMarker); if (redo && rootGo == null) { sb.AppendLine("not applied yet"); return; }
        var root = redo ? rootGo.transform : new GameObject(AesMarker).transform;
        Transform Group(string n) { var ex = root.Find(n); if (ex) return ex; var g = new GameObject(n).transform; g.SetParent(root, false); return g; }
        var gPrecinct = Group("Church_Precinct"); var gLamps = Group("Street_Lamps_Added"); var gBorders = Group("Street_Borders"); var gPebble = Group("Pebble_Paving"); var gFill = Group("Open_Lot_Fill");
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GameObject Named(string n, string under = null) => all.Where(t => t.name == n && (under == null || Path(t).Contains(under))).Select(t => t.gameObject).FirstOrDefault();

        if (redo)
        {
            foreach (var g in new[] { gBorders, gFill }) for (int i = g.childCount - 1; i >= 0; i--) Object.DestroyImmediate(g.GetChild(i).gameObject);
            if (File.Exists("Logs/aesthetic_fill_trees.txt"))
            {
                var tl = td.treeInstances.ToList(); int before = tl.Count;
                foreach (var line in File.ReadAllLines("Logs/aesthetic_fill_trees.txt")) { var f = line.Split(';'); if (f.Length < 2) continue; var q = new Vector2(float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)); int k = tl.FindIndex(ti => (new Vector2(ti.position.x * td.size.x + tpos.x, ti.position.z * td.size.z + tpos.z) - q).sqrMagnitude < .01f); if (k >= 0) tl.RemoveAt(k); }
                td.treeInstances = tl.ToArray(); sb.AppendLine("previous fill trees removed " + (before - tl.Count));
            }
            Physics.SyncTransforms();
            all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
        // ================= 1. bridge: grey stone
        if (!redo) { {
            var bridge = GameObject.Find("SouthCreek_WideStoneBridge");
            var mats = new HashSet<Material>();
            if (bridge) foreach (var r in bridge.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
            foreach (var m in mats)
            {
                var tex = m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") as Texture2D : null;
                string mp = AssetDatabase.GetAssetPath(m);
                if (tex == null || !mp.EndsWith(".mat")) { sb.AppendLine("bridge mat skipped " + m.name); continue; }
                string tp = AssetDatabase.GetAssetPath(tex); if (tp.Contains("_grey")) continue;
                string gp = System.IO.Path.ChangeExtension(tp, null) + "_grey.png";
                var img = new Texture2D(2, 2, TextureFormat.RGBA32, false); img.LoadImage(File.ReadAllBytes(tp));
                var px = img.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i]; float l = (.30f * c.r + .59f * c.g + .11f * c.b) / 255f;
                    l = Mathf.Clamp01(.08f + l * 1.02f);
                    float g0 = l * .975f, g1 = l * .985f, g2 = l * 1.0f; // cool grey limestone
                    px[i] = new Color32((byte)(255 * Mathf.Lerp(g0, c.r / 255f, .1f)), (byte)(255 * Mathf.Lerp(g1, c.g / 255f, .1f)), (byte)(255 * Mathf.Lerp(g2, c.b / 255f, .1f)), c.a);
                }
                img.SetPixels32(px); File.WriteAllBytes(gp, img.EncodeToPNG()); Object.DestroyImmediate(img);
                AssetDatabase.ImportAsset(gp, ImportAssetOptions.ForceUpdate);
                var si = AssetImporter.GetAtPath(tp) as TextureImporter; var gi = AssetImporter.GetAtPath(gp) as TextureImporter;
                if (si && gi) { gi.sRGBTexture = true; gi.maxTextureSize = si.maxTextureSize; gi.textureCompression = si.textureCompression; gi.mipmapEnabled = true; gi.SaveAndReimport(); }
                var grey = AssetDatabase.LoadAssetAtPath<Texture2D>(gp);
                m.SetTexture("_BaseColorMap", grey); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", grey);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(.86f, .86f, .87f, 1f));
                HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m);
                sb.AppendLine("bridge grey: " + mp + " <- " + gp);
            }
        }

        // ================= 2. creek bank walls with railing: bigger and taller
        {
            int n = 0;
            foreach (var t in all.Where(t => t.parent != null && t.parent.name == "Riverbank_Stone_And_Spearhead_Iron" && (t.name.StartsWith("Bank_North_") || t.name.StartsWith("Bank_South_"))))
            {
                var b0 = Bounds(t.gameObject);
                t.localScale = Vector3.Scale(t.localScale, new Vector3(1.10f, 1.5f, 1.15f));
                var b1 = Bounds(t.gameObject); t.position += new Vector3(b0.center.x - b1.center.x, b0.min.y - b1.min.y, b0.center.z - b1.center.z);
                EditorUtility.SetDirty(t); n++;
            }
            sb.AppendLine("bank pieces scaled: " + n);
        }
        }
        Physics.SyncTransforms();

        // ================= shared data
        var streets = new List<(string area, string name, float half, Vector2[] pts)>();
        foreach (var s in Streets) streets.Add(("Village Square", s.name, s.half, s.pts));
        foreach (var p in AreaPlans) foreach (var s in p.streets) streets.Add((p.area, s.name, s.half, s.pts));
        float StreetGap(Vector2 q, int skip = -1)
        {
            float d = 1e9f;
            for (int k = 0; k < streets.Count; k++) { if (k == skip) continue; var pts = streets[k].pts; for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, SegDist(q, pts[i], pts[i + 1]) - streets[k].half); }
            return d;
        }
        var church = GameObject.Find("FallenCity_Church_HumanScale"); var cb = Bounds(church);
        sb.AppendLine("church bounds " + cb.min.ToString("F1") + " .. " + cb.max.ToString("F1"));
        var houses = new List<Transform>();
        foreach (var t in all)
        {
            if (!IsHouseName(t.name) || t.GetComponentInChildren<Renderer>() == null || t.IsChildOf(church.transform)) continue;
            var b = Bounds(t.gameObject);
            if (b.size.y < 2.6f || b.size.y > 30f || Mathf.Min(b.size.x, b.size.z) < 2.5f || Mathf.Max(b.size.x, b.size.z) > 45f) continue;
            houses.Add(t);
        }
        houses = houses.Where(h => !houses.Any(o => o != h && h.IsChildOf(o))).ToList();
        var hb = houses.Select(h => Bounds(h.gameObject)).ToList(); hb.Add(cb);
        bool NearHouse(Vector2 q, float m) => hb.Any(b => q.x > b.min.x - m && q.x < b.max.x + m && q.y > b.min.z - m && q.y < b.max.z + m);
        var avoid = new List<(Vector2 p, float r)>();
        foreach (var s in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) avoid.Add((XZ(s.transform.position), 5f));
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) avoid.Add((XZ(g.transform.position), 7f));
        foreach (var sp in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) avoid.Add((XZ(sp.transform.position), 3.5f));
        foreach (var n in new[] { "Horse_Salvage_Objective", "Church_Altar_Weapon_Upgrader" }) { var go = GameObject.Find(n); if (go) avoid.Add((XZ(Bounds(go).center), 7f)); }
        { var go = GameObject.Find("SouthCreek_WideStoneBridge"); if (go) { var b = Bounds(go); avoid.Add((new Vector2(b.center.x, b.max.z), 6f)); avoid.Add((new Vector2(b.center.x, b.min.z), 6f)); } }
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>(); if (player) avoid.Add((XZ(player.transform.position), 8f));
        bool Avoided(Vector2 q, float m = 0f) => avoid.Any(a => (a.p - q).magnitude < a.r + m);
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(a => a.polygon != null && a.polygon.Length > 2).ToArray();
        ElderboomArea AreaOf(Vector2 p) => areas.FirstOrDefault(a => a.Contains(new Vector3(p.x, 0, p.y)));
        float EdgeD(ElderboomArea a, Vector2 p) { float d = 1e9f; for (int i = 0, j = a.polygon.Length - 1; i < a.polygon.Length; j = i++) d = Mathf.Min(d, SegDist(p, a.polygon[j], a.polygon[i])); return d; }
        int mask = ~((1 << 2) | (1 << 30));
        bool Blocked(Vector2 c, Vector2 halfXZ, float yaw, float y0, float hgt, Transform ignore = null)
        {
            foreach (var col in Physics.OverlapBox(new Vector3(c.x, y0 + .3f + hgt * .5f, c.y), new Vector3(halfXZ.x, hgt * .5f, halfXZ.y), Quaternion.Euler(0, yaw, 0), mask, QueryTriggerInteraction.Ignore))
            { if (col is TerrainCollider) continue; if (ignore && col.transform.IsChildOf(ignore)) continue; return true; }
            return false;
        }
        bool BlockedBy(Vector2 c, Vector2 halfXZ, float yaw, float y0, float hgt, out string who)
        {
            who = null;
            foreach (var col in Physics.OverlapBox(new Vector3(c.x, y0 + .3f + hgt * .5f, c.y), new Vector3(halfXZ.x, hgt * .5f, halfXZ.y), Quaternion.Euler(0, yaw, 0), mask, QueryTriggerInteraction.Ignore))
            { if (col is TerrainCollider) continue; var t = col.transform; while (t.parent != null && t.parent.parent != null) t = t.parent; who = t.name; return true; }
            return false;
        }
        // places a copy (or the object itself) with its footprint centred on 'at', its long local x axis at 'yaw', resting on the lowest ground
        GameObject Put(GameObject src, bool clone, Vector2 at, float yaw, Transform parent, Vector3 scaleMul, string nm = null)
        {
            var go = clone ? Object.Instantiate(src) : src; go.SetActive(true);
            if (nm != null) go.name = nm; else if (clone) go.name = src.name;
            var t = go.transform; var srcRot = src.transform.rotation; var srcScale = src.transform.lossyScale;
            if (clone) { t.SetParent(parent, true); t.localScale = Vector3.Scale(srcScale, scaleMul); }
            else t.localScale = Vector3.Scale(t.localScale, scaleMul);
            float sy = srcRot.eulerAngles.y; t.rotation = Quaternion.Euler(0, yaw - sy, 0) * srcRot;
            var b = Bounds(go);
            float lo = Mathf.Min(Mathf.Min(H(at.x - b.extents.x, at.y - b.extents.z), H(at.x + b.extents.x, at.y - b.extents.z)), Mathf.Min(H(at.x - b.extents.x, at.y + b.extents.z), Mathf.Min(H(at.x + b.extents.x, at.y + b.extents.z), H(at.x, at.y))));
            t.position += new Vector3(at.x - b.center.x, lo - .04f - b.min.y, at.y - b.center.z);
            EditorUtility.SetDirty(go); return go;
        }
        float YawOf(Vector2 dir) => Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg; // local +x along dir
        var trees = td.treeInstances.ToList();
        Vector2 TreeXZ(TreeInstance ti) => new Vector2(ti.position.x * td.size.x + tpos.x, ti.position.z * td.size.z + tpos.z);
        int treesRemoved = 0;
        void ClearTrees(System.Func<Vector2, bool> inside) { int n0 = trees.Count; trees.RemoveAll(ti => inside(TreeXZ(ti))); treesRemoved += n0 - trees.Count; }

        // ================= 3. church precinct
        float west = cb.min.x, east = cb.max.x, north = cb.max.z, south = cb.min.z;
        var innGo = all.FirstOrDefault(t => t.name.Contains("inn") && houses.Contains(t));
        float innEast = innGo ? Bounds(innGo.gameObject).max.x : -19f;
        var front = Rect.MinMaxRect(Mathf.Max(innEast + 2f, west - 13f), south + .4f, west - .3f, north - .4f);
        var court = Rect.MinMaxRect(east + .4f, south + 1.2f, 51.2f, north - 1.0f);
        var nWing = Rect.MinMaxRect(west + .5f, north + .1f, east - 1.5f, 6.9f);
        var sWing = Rect.MinMaxRect(west + .5f, -31.1f, east - .5f, south - .1f);
        var precinct = new[] { front, court, nWing, sWing };
        bool InPrec(Vector2 q, float m = 0f) => precinct.Any(r => q.x > r.xMin - m && q.x < r.xMax + m && q.y > r.yMin - m && q.y < r.yMax + m);
        sb.AppendLine("front " + front + " court " + court + " nWing " + nWing + " sWing " + sWing + " innEast " + innEast);

        // kit sources (before anything is hidden)
        var srcBench = Named("S_Old_Wooden_Bench_udlufetqx_lod3", "Cemetery_Consecrated_Ground");
        var srcFountain = Named("village_fountain_0");
        var srcStallA = Named("North_forecourt_produce_stall"); var srcStallB = Named("North_forecourt_linen_stall");
        var srcCrate = Named("CC0_wooden_crate_01", "05_Creek_Fishing"); var srcBarrel = Named("CC0_wine_barrel_01", "01_Village_Market"); var srcBasket = Named("CC0_wicker_basket_01");
        var srcWall = all.Where(t => t.name == "Elderboom_MossyStoneWall" && t.parent && t.parent.name == "graveyard_rear_wall").Select(t => t.gameObject).FirstOrDefault();
        var srcFence = all.Where(t => t.name == "Elderboom_ModularFence" && t.parent && t.parent.name == "sawmill_enclosure").Select(t => t.gameObject).FirstOrDefault();
        var srcLamp = all.Where(t => t.name == "Elderboom_Streetlamp" && t.parent == null).Select(t => t.gameObject).FirstOrDefault();
        var srcHay = Named("S_Round_Hay_Bale_rlCay_lod3", "Farm_Working_Yard");
        sb.AppendLine("kit: bench " + (srcBench != null) + " fountain " + (srcFountain != null) + " stalls " + (srcStallA != null) + "/" + (srcStallB != null) + " crate " + (srcCrate != null) + " barrel " + (srcBarrel != null) + " basket " + (srcBasket != null) + " wall " + (srcWall != null) + " fence " + (srcFence != null) + " lamp " + (srcLamp != null) + " hay " + (srcHay != null));

        if (!redo) {
        // hide small props in the precinct (and the old forecourt market that now stands on Via Maestra)
        {
            var keep = new HashSet<Transform>();
            foreach (var s in new[] { srcBench, srcFountain, srcCrate, srcBarrel, srcBasket, srcWall, srcFence, srcLamp, srcHay }) if (s) keep.Add(s.transform);
            int hidden = 0; var seen = new HashSet<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                var go = r.gameObject; if (go.transform.IsChildOf(church.transform) || go.transform.IsChildOf(root)) continue;
                var b = r.bounds; if (Mathf.Max(b.size.x, b.size.z) > 4.5f) continue;
                while (go.transform.parent != null && go.transform.parent.parent != null) { var pb = Bounds(go.transform.parent.gameObject); if (Mathf.Max(pb.size.x, pb.size.z) <= 4.5f && pb.size.y < 4.5f) go = go.transform.parent.gameObject; else break; }
                if (!seen.Add(go)) continue;
                var gb = Bounds(go); var c = XZ(gb.center);
                bool onMaestra = Path(go.transform).Contains("Village_Market") || Path(go.transform).Contains("Final_Props");
                bool hit = InPrec(c, .2f) || (onMaestra && c.x > 6f && c.x < 44f && c.y > 6f && c.y < 15f && StreetGap(c) < .8f);
                if (!hit) continue;
                string p = Path(go.transform); string ln = p.ToLowerInvariant();
                if (keep.Contains(go.transform) || ln.Contains("streetlamp") || ln.Contains("station_") || ln.Contains("church_") || ln.Contains("riverbank") || ln.Contains("gate") || ln.Contains("blocker") || ln.Contains("spawn") || go.GetComponentInChildren<ElderboomStation>() || go.GetComponentInChildren<ElderboomSpawnPoint>()) continue;
                if (go == srcStallA || go == srcStallB) continue;
                go.SetActive(false); EditorUtility.SetDirty(go); hidden++; sb.AppendLine("  hide " + p + " @" + c.ToString("F1"));
            }
            sb.AppendLine("precinct props hidden: " + hidden);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var c = XZ(r.bounds.center); if (!InPrec(c) || r.transform.IsChildOf(church.transform)) continue;
                if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) > 4.5f && !r.name.ToLowerInvariant().Contains("grass")) sb.AppendLine("  big in precinct: " + Path(r.transform) + " " + r.bounds.size.ToString("F1"));
            }
            ClearTrees(q => InPrec(q, 1.5f));
        }

        // paving mesh (pebbles) over a set of rects, terrain-following
        var pebbleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Elderboom_Authored_Roads.mat");
        GameObject MeshObj(string nm, List<Vector3> v, List<int> tri, Transform parent)
        {
            var m = new Mesh { name = nm, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetTriangles(tri, 0); m.SetUVs(0, v.Select(p => new Vector2(p.x * .35f, p.z * .35f)).ToList()); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            string ap = "Assets/Resources/Elderboom/Aesthetic/" + nm + ".asset"; AssetDatabase.DeleteAsset(ap); AssetDatabase.CreateAsset(m, ap);
            var go = new GameObject(nm); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = pebbleMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic); return go;
        }
        void Patch(string nm, Rect r, float chamfer)
        {
            var v = new List<Vector3>(); var tri = new List<int>(); float step = 1f;
            int nx = Mathf.CeilToInt(r.width / step), nz = Mathf.CeilToInt(r.height / step);
            var idx = new int[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) { float x = Mathf.Min(r.xMin + i * step, r.xMax), z = Mathf.Min(r.yMin + j * step, r.yMax); idx[i, j] = v.Count; v.Add(new Vector3(x, H(x, z) + .05f, z)); }
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
                {
                    float cx = r.xMin + (i + .5f) * step, cz = r.yMin + (j + .5f) * step;
                    float dx = Mathf.Min(cx - r.xMin, r.xMax - cx), dz = Mathf.Min(cz - r.yMin, r.yMax - cz); if (dx + dz < chamfer) continue;
                    tri.AddRange(new[] { idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i, j], idx[i + 1, j + 1], idx[i + 1, j] });
                }
            MeshObj(nm, v, tri, gPebble);
        }
        Patch("Pebble_Church_FrontSquare", front, 1.5f);
        Patch("Pebble_Church_Courtyard", court, 0f);
        Patch("Pebble_Church_NorthWing", nWing, 0f);
        Patch("Pebble_Church_SouthWing", sWing, 0f);

        // church door openings on the long faces: keep the stalls off them
        List<float> Openings(float faceZ, float dirZ)
        {
            var res = new List<float>();
            for (float x = west + 1f; x < east - 1f; x += .5f)
            {
                var o = new Vector3(x, H(x, faceZ + dirZ * 2.5f) + 1.3f, faceZ + dirZ * 2.5f); bool hitChurch = false;
                foreach (var h in Physics.RaycastAll(o, new Vector3(0, 0, -dirZ), 5f, mask, QueryTriggerInteraction.Ignore)) if (h.collider.transform.IsChildOf(church.transform)) { hitChurch = true; break; }
                if (!hitChurch) res.Add(x);
            }
            return res;
        }
        var openN = Openings(north, 1f); var openS = Openings(south, -1f);
        sb.AppendLine("church openings north: " + string.Join(",", openN.Select(x => x.ToString("F1"))) + " | south: " + string.Join(",", openS.Select(x => x.ToString("F1"))));

        // fountain + benches in the rear courtyard
        if (srcFountain)
        {
            var f = Put(srcFountain, true, court.center, 0f, gPrecinct, Vector3.one, "Courtyard_Fountain");
            var fb = Bounds(f); float fit = Mathf.Min(1f, (court.width - 3.2f) / Mathf.Max(fb.size.x, fb.size.z));
            if (fit < 1f) { Object.DestroyImmediate(f); f = Put(srcFountain, true, court.center, 0f, gPrecinct, Vector3.one * fit, "Courtyard_Fountain"); }
            sb.AppendLine("courtyard fountain at " + court.center.ToString("F1") + " scale " + fit.ToString("F2"));
        }
        if (srcBench)
        {
            foreach (var (p, yaw) in new[] { (new Vector2(court.xMin + .6f, court.center.y + 5.5f), 90f), (new Vector2(court.xMin + .6f, court.center.y - 5.5f), 90f), (new Vector2(court.xMax - .6f, court.center.y + 5.5f), 270f), (new Vector2(court.xMax - .6f, court.center.y - 5.5f), 270f),
                                             (new Vector2(front.xMin + .7f, front.center.y + 6f), 90f), (new Vector2(front.xMin + .7f, front.center.y - 6f), 90f) })
                Put(srcBench, true, p, yaw, gPrecinct, Vector3.one, "Precinct_Bench");
        }
        if (srcBarrel) { Put(srcBarrel, true, new Vector2(front.xMin + .8f, front.yMax - 1.2f), 20f, gPrecinct, Vector3.one); Put(srcBarrel, true, new Vector2(front.xMin + 1.9f, front.yMax - .9f), 70f, gPrecinct, Vector3.one); }

        // stall wings
        {
            var stalls = new[] { srcStallA, srcStallB }.Where(s => s).ToArray(); var props = new[] { srcCrate, srcBarrel, srcBasket }.Where(s => s).ToArray();
            int placed = 0, si = 0;
            void Wing(Rect r, float faceZ, float dirZ, List<float> openings, float yaw)
            {
                for (float x = r.xMin + 3.5f; x < r.xMax - 2.5f; x += 6.6f)
                {
                    if (openings.Any(o => Mathf.Abs(o - x) < 3f)) { sb.AppendLine("  stall slot skipped (door) x " + x.ToString("F1")); continue; }
                    var src = stalls[si++ % stalls.Length];
                    var at = new Vector2(x, faceZ + dirZ * (1.0f + 1.5f));
                    bool firstUse = (src == srcStallA && si <= 2) || (src == srcStallB && si <= 2);
                    Put(src, !firstUse, at, yaw, gPrecinct, Vector3.one, "Market_Stall");
                    if (firstUse) src.transform.SetParent(gPrecinct, true);
                    placed++;
                    if (props.Length > 0)
                    {
                        var pr = props[(placed + 1) % props.Length]; Put(pr, true, new Vector2(x + 2.6f, faceZ + dirZ * R(1.4f, 2.4f)), R(0, 360), gPrecinct, Vector3.one);
                        if (placed % 2 == 0) Put(props[placed % props.Length], true, new Vector2(x - 2.4f, faceZ + dirZ * R(1.2f, 2f)), R(0, 360), gPrecinct, Vector3.one);
                    }
                }
            }
            if (stalls.Length > 0) { Wing(nWing, north, 1f, openN, 0f); Wing(sWing, south, -1f, openS, 180f); }
            sb.AppendLine("stalls placed " + placed);
        }
        Physics.SyncTransforms();

        // ================= 6. pebble streets
        {
            foreach (var st in streets.Where(s => s.name == "Via Maestra" || s.name == "Strada del Mulino" || s.name == "Rampa del Ponte" || s.name == "Via del Mercato" || s.name == "Via del Ponte"))
            {
                var dense = Dense(st.pts, .8f); var v = new List<Vector3>(); var tri = new List<int>(); int across = 5; float w = st.half * .92f;
                for (int i = 0; i < dense.Count; i++)
                {
                    var tg = (dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)]).normalized; var nrm = new Vector2(-tg.y, tg.x);
                    for (int j = 0; j < across; j++)
                    {
                        float f = j / (float)(across - 1) * 2f - 1f; float edge = (j == 0 || j == across - 1) ? (Mathf.PerlinNoise(dense[i].x * .37f, dense[i].y * .37f + j) - .5f) * .5f : 0f;
                        var q = dense[i] + nrm * f * (w + edge * Mathf.Sign(f)); v.Add(new Vector3(q.x, H(q.x, q.y) + .055f, q.y));
                    }
                    if (i == 0) continue; int b0 = (i - 1) * across, b1 = i * across;
                    for (int j = 0; j + 1 < across; j++) tri.AddRange(new[] { b0 + j, b1 + j, b1 + j + 1, b0 + j, b1 + j + 1, b0 + j + 1 });
                }
                // drop quads over the paved precinct
                var keepTri = new List<int>();
                for (int i = 0; i < tri.Count; i += 3) { var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f; if (InPrec(new Vector2(c.x, c.z), -.2f)) continue; keepTri.AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] }); }
                // winding: make sure normals face up
                var m0 = keepTri.Count >= 3 ? Vector3.Cross(v[keepTri[1]] - v[keepTri[0]], v[keepTri[2]] - v[keepTri[0]]) : Vector3.up;
                if (m0.y < 0) for (int i = 0; i < keepTri.Count; i += 3) { int t2 = keepTri[i + 1]; keepTri[i + 1] = keepTri[i + 2]; keepTri[i + 2] = t2; }
                MeshObj("Pebble_" + st.name.Replace(" ", "_"), v, keepTri, gPebble);
                sb.AppendLine("pebble street " + st.name + " " + dense.Count + " stations");
            }
        }
        // ================= 4. lamps along the streets
        {
            var lamps = all.Where(t => t.name == "Elderboom_Streetlamp" && t.parent == null).Select(t => t.gameObject).ToList();
            var spots = new List<(Vector2 p, float yaw, string street)>();
            for (int k = 0; k < streets.Count; k++)
            {
                var st = streets[k]; if (st.name == "Rampa del Ponte" || st.name == "Sagrato") continue;
                float spacing = st.area == "Village Square" ? 17f : 23f;
                var dense = Dense(st.pts, .5f); float acc = spacing * .5f; int side = k % 2 == 0 ? 1 : -1;
                for (int i = 1; i < dense.Count; i++)
                {
                    acc += (dense[i] - dense[i - 1]).magnitude; if (acc < spacing) continue;
                    var tg = (dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[i - 1]).normalized; var nrm = new Vector2(-tg.y, tg.x);
                    bool ok = false;
                    foreach (int sd in new[] { side, -side })
                    {
                        var p = dense[i] + nrm * sd * (st.half + .9f);
                        if (StreetGap(p) < .5f || NearHouse(p, 1.2f) || Avoided(p, 1f) || H(p.x, p.y) < .25f || spots.Any(s => (s.p - p).magnitude < 11f)) continue;
                        var a = AreaOf(p); if (a == null || EdgeD(a, p) < 1.5f) continue;
                        if (Blocked(p, new Vector2(.45f, .45f), 0, H(p.x, p.y), 3f)) continue;
                        spots.Add((p, YawOf(-nrm * sd) + 90f, st.name)); ok = true; side = -sd; break;
                    }
                    acc = ok ? 0f : spacing - 3f;
                }
            }
            // front square corners
            foreach (var p in new[] { new Vector2(front.xMin + .5f, front.yMin + .5f), new Vector2(front.xMin + .5f, front.yMax - .5f), new Vector2(court.center.x, court.yMin + .5f) })
                if (!spots.Any(s => (s.p - p).magnitude < 6f)) spots.Add((p, 0f, "precinct"));
            int maxLamps = 24; spots = spots.Take(maxLamps).ToList();
            int moved = 0, added = 0;
            for (int i = 0; i < spots.Count; i++)
            {
                GameObject l;
                if (i < lamps.Count) { l = Put(lamps[i], false, spots[i].p, spots[i].yaw, null, Vector3.one); moved++; }
                else
                {
                    l = Put(srcLamp, true, spots[i].p, spots[i].yaw, gLamps, Vector3.one, "Elderboom_Streetlamp"); added++;
                    foreach (var lt in l.GetComponentsInChildren<Light>(true)) { lt.shadows = LightShadows.None; var hd = lt.GetComponent<HDAdditionalLightData>(); if (hd) EditorUtility.SetDirty(hd); }
                }
                sb.AppendLine("  lamp " + spots[i].street + " " + spots[i].p.ToString("F1"));
                Physics.SyncTransforms();
            }
            for (int i = spots.Count; i < lamps.Count; i++) { lamps[i].SetActive(false); sb.AppendLine("  spare lamp hidden"); }
            sb.AppendLine("lamps moved " + moved + ", added " + added);
        }
        }

        // ================= 5. discontinuous low walls and fences along the streets
        {
            int walls = 0, fences = 0; var why = new Dictionary<string, int>(); void Why(string k) { why.TryGetValue(k, out int v); why[k] = v + 1; }
            float modW = 3.3f, modF = 3.2f;
            if (srcWall) { var tmp = Put(srcWall, true, new Vector2(0, -200), 0, gBorders, new Vector3(1, .55f, 1)); var b = Bounds(tmp); modW = b.size.x * .96f; Object.DestroyImmediate(tmp); }
            if (srcFence) { var tmp = Put(srcFence, true, new Vector2(0, -200), 0, gBorders, new Vector3(1, .82f, 1)); var b = Bounds(tmp); modF = b.size.x * .97f; Object.DestroyImmediate(tmp); }
            sb.AppendLine("module lengths wall " + modW.ToString("F2") + " fence " + modF.ToString("F2"));
            for (int k = 0; k < streets.Count; k++)
            {
                var st = streets[k]; if (st.name == "Rampa del Ponte" || st.name == "Sagrato") continue;
                var dense = Dense(st.pts, .25f); var arc = new float[dense.Count]; for (int i = 1; i < dense.Count; i++) arc[i] = arc[i - 1] + (dense[i] - dense[i - 1]).magnitude;
                float total = arc[arc.Length - 1];
                (Vector2 p, Vector2 t) At(float s) { int i = System.Array.FindIndex(arc, a => a >= s); if (i <= 0) i = 1; if (i < 0) i = arc.Length - 1; var t = (dense[i] - dense[i - 1]).normalized; float f = Mathf.InverseLerp(arc[i - 1], arc[i], s); return (Vector2.Lerp(dense[i - 1], dense[i], f), t); }
                float s0 = R(3f, 7f);
                while (s0 < total - 4f)
                {
                    bool useWall = st.area == "Village Square" ? rnd.NextDouble() < .75 : st.area == "Outer Woods" ? rnd.NextDouble() < .2 : rnd.NextDouble() < .5;
                    var src = useWall ? srcWall : srcFence; if (src == null) src = srcWall ?? srcFence; if (src == null) break;
                    float mod = src == srcWall ? modW : modF; int nMods = rnd.Next(2, 5);
                    int side = rnd.NextDouble() < .5 ? 1 : -1; bool both = rnd.NextDouble() < .25;
                    foreach (int sd in both ? new[] { side, -side } : new[] { side })
                        for (int m = 0; m < nMods; m++)
                        {
                            float sc = s0 + (m + .5f) * mod; if (sc > total - 2f) break;
                            var (p, t) = At(sc); var nrm = new Vector2(-t.y, t.x);
                            var c = p + nrm * sd * (st.half + 1.15f);
                            var e1 = c + t * mod * .5f; var e2 = c - t * mod * .5f;
                            var pts3 = new[] { c, e1, e2 };
                            if (pts3.Any(q => StreetGap(q) < .45f)) { Why("street"); continue; }
                            if (pts3.Any(q => NearHouse(q, 1.5f))) { Why("house"); continue; }
                            if (pts3.Any(q => InPrec(q, 1f))) { Why("precinct"); continue; }
                            if (pts3.Any(q => Avoided(q, 0f))) { Why("avoid"); continue; }
                            if (pts3.Any(q => H(q.x, q.y) < .3f)) { Why("creek"); continue; }
                            if (Mathf.Abs(H(e1.x, e1.y) - H(e2.x, e2.y)) > .9f) { Why("slope"); continue; }
                            var a = AreaOf(c); if (a == null || a.areaName != st.area || EdgeD(a, c) < 2.5f) { Why("area"); continue; }
                            float yaw = YawOf(t);
                            if (BlockedBy(c, new Vector2(mod * .5f, .4f), yaw, H(c.x, c.y), 1.3f, out string bn)) { Why("collider " + bn); continue; }
                            Put(src, true, c, yaw, gBorders, src == srcWall ? new Vector3(1, .55f, 1) : new Vector3(1, .82f, 1), src == srcWall ? "Street_LowWall" : "Street_Fence");
                            Physics.SyncTransforms(); if (src == srcWall) walls++; else fences++;
                        }
                    s0 += nMods * mod + R(5f, 11f);
                }
            }
            sb.AppendLine("street borders: walls " + walls + ", fences " + fences);
            foreach (var kv in why.OrderByDescending(kv => kv.Value).Take(25)) sb.AppendLine("   reject " + kv.Key + " x" + kv.Value);
        }

        // patches winding check is done the same way below
        if (!redo) foreach (var mf in gPebble.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh; var tv = m.vertices; var tt = m.triangles; if (tt.Length < 3) continue;
            if (Vector3.Cross(tv[tt[1]] - tv[tt[0]], tv[tt[2]] - tv[tt[0]]).y < 0) { for (int i = 0; i < tt.Length; i += 3) { int x = tt[i + 1]; tt[i + 1] = tt[i + 2]; tt[i + 2] = x; } m.triangles = tt; m.RecalculateNormals(); m.RecalculateTangents(); EditorUtility.SetDirty(m); }
        }
        Physics.SyncTransforms();

        // ================= 7. fill the wide empty lots
        {
            float cell = 2f; var fillAreas = new[] { "Village Square", "South Creek", "Outer Woods" };
            var cand = new List<Vector2>();
            for (float x = -110; x <= 120; x += cell) for (float z = -110; z <= 110; z += cell)
                {
                    var q = new Vector2(x, z); var a = AreaOf(q); if (a == null || !fillAreas.Contains(a.areaName) || EdgeD(a, q) < 5f) continue;
                    cand.Add(q);
                }
            var occ = new List<Vector2>();
            foreach (var q in cand)
            {
                bool o = StreetGap(q) < 1.5f || NearHouse(q, 1.5f) || InPrec(q, 2f) || Avoided(q, 3f) || H(q.x, q.y) < .35f;
                if (!o && Blocked(q, new Vector2(cell * .5f, cell * .5f), 0, H(q.x, q.y), 2.5f)) o = true;
                if (o) occ.Add(q);
            }
            foreach (var ti in trees) occ.Add(TreeXZ(ti));
            foreach (var a in areas) for (int i = 0, j = a.polygon.Length - 1; i < a.polygon.Length; j = i++) for (float f = 0; f <= 1f; f += .05f) occ.Add(Vector2.Lerp(a.polygon[j], a.polygon[i], f));
            var occGrid = new Dictionary<Vector2Int, List<Vector2>>();
            void AddOcc(Vector2 q) { var k = new Vector2Int(Mathf.FloorToInt(q.x / 8f), Mathf.FloorToInt(q.y / 8f)); if (!occGrid.TryGetValue(k, out var l)) occGrid[k] = l = new List<Vector2>(); l.Add(q); }
            foreach (var q in occ) AddOcc(q);
            var occSet = new HashSet<Vector2>(occ);
            float Clear(Vector2 q, float max)
            {
                float d = max; int n = Mathf.CeilToInt(max / 8f); var k0 = new Vector2Int(Mathf.FloorToInt(q.x / 8f), Mathf.FloorToInt(q.y / 8f));
                for (int dx = -n; dx <= n; dx++) for (int dz = -n; dz <= n; dz++) if (occGrid.TryGetValue(new Vector2Int(k0.x + dx, k0.y + dz), out var l)) foreach (var o in l) d = Mathf.Min(d, (o - q).magnitude);
                return d;
            }
            var free = cand.Where(q => !occSet.Contains(q)).ToList();
            { var hist = new int[12]; foreach (var q in free) hist[Mathf.Min(11, (int)(Clear(q, 22f) / 2f))]++; sb.AppendLine("fill: cand " + cand.Count + " occ " + occ.Count + " free " + free.Count + " clearance hist(2m bins) " + string.Join(",", hist)); }
            int clusters = 0, tAdded = 0; var fillTrees = new StringBuilder();
            for (int iter = 0; iter < 34; iter++)
            {
                Vector2 best = default; float bd = 0f;
                foreach (var q in free) { float d = Clear(q, 22f); if (d > bd) { bd = d; best = q; } }
                if (bd < 6.5f) break;
                string area = AreaOf(best).areaName;
                // tree prototype of the nearest existing tree
                var near = trees.OrderBy(ti => (TreeXZ(ti) - best).sqrMagnitude).Take(6).ToList(); if (near.Count == 0) break;
                int nT = area == "Outer Woods" ? rnd.Next(4, 7) : area == "South Creek" ? rnd.Next(3, 5) : rnd.Next(2, 4);
                float rad = Mathf.Min(bd - 2.5f, area == "Outer Woods" ? 6f : 4.5f);
                for (int i = 0; i < nT; i++)
                {
                    float ang = i / (float)nT * Mathf.PI * 2f + R(-.4f, .4f); var p = best + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(rad * .4f, rad);
                    if (H(p.x, p.y) < .35f) continue;
                    var proto = near[rnd.Next(near.Count)]; float s = R(.85f, 1.15f);
                    var ti = new TreeInstance { prototypeIndex = proto.prototypeIndex, position = new Vector3((p.x - tpos.x) / td.size.x, 0f, (p.y - tpos.z) / td.size.z), widthScale = proto.widthScale * s, heightScale = proto.heightScale * s, rotation = R(0, Mathf.PI * 2f), color = proto.color, lightmapColor = proto.lightmapColor };
                    trees.Add(ti); tAdded++; fillTrees.AppendLine(p.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + p.y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                // a little set piece in the clearing
                var dir = new Vector2(Mathf.Cos(R(0, 6.28f)), Mathf.Sin(R(0, 6.28f))); var perp = new Vector2(-dir.y, dir.x);
                var wallSrc = area == "Village Square" ? srcWall : srcFence ?? srcWall;
                if (wallSrc && bd > 8f)
                {
                    var c0 = best + dir * (rad + 1.2f);
                    for (int m = -1; m <= 0; m++) { var c = c0 + perp * (m + .5f) * 3.1f; if (!Blocked(c, new Vector2(1.6f, .4f), YawOf(perp), H(c.x, c.y), 1.3f) && H(c.x, c.y) > .35f) { Put(wallSrc, true, c, YawOf(perp), gFill, wallSrc == srcWall ? new Vector3(1, .55f, 1) : new Vector3(1, .82f, 1), "Lot_Border"); Physics.SyncTransforms(); } }
                }
                var deco = area == "Village Square" ? srcBench : area == "Outer Woods" ? (srcHay ?? srcCrate) : (srcCrate ?? srcBarrel);
                if (deco) { var c = best - dir * (rad * .3f); if (!Blocked(c, new Vector2(.8f, .8f), 0, H(c.x, c.y), 1.2f)) { Put(deco, true, c, YawOf(perp), gFill, Vector3.one, "Lot_" + deco.name); Physics.SyncTransforms(); } }
                if (area != "Outer Woods" && srcBarrel && rnd.NextDouble() < .5) { var c = best - dir * (rad * .3f) + perp * 1.8f; Put(srcBarrel, true, c, R(0, 360), gFill, Vector3.one, "Lot_Barrel"); }
                clusters++; sb.AppendLine("  lot fill " + area + " " + best.ToString("F0") + " clearance " + bd.ToString("F1") + " trees " + nT);
                for (float a2 = 0; a2 < 6.28f; a2 += .4f) for (float rr = 0; rr <= rad + 3f; rr += 2f) AddOcc(best + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * rr);
            }
            sb.AppendLine("lots filled " + clusters + ", trees added " + tAdded); File.WriteAllText("Logs/aesthetic_fill_trees.txt", fillTrees.ToString());
        }
        td.treeInstances = trees.ToArray(); EditorUtility.SetDirty(td);
        sb.AppendLine("trees removed in precinct " + treesRemoved);

        Physics.SyncTransforms();
        sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        sb.AppendLine("SAVED");
    }
}
