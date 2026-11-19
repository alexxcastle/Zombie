using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

// Map rework, apply step (edit mode, village scene). Run once: a marker object prevents a second pass.
//  1. closed-gate connectivity of every area from the Village Square (baseline)
//  2. visual fixes: inn/amphitheatre/railing materials, broken foliage template, pond, red bushes, church parvis
//  3. layout: church at 0.65 (altar + tech kept at human scale, re-seated), perimeter houses scaled within
//     their clearance and alternately turned, boulder off the creek fence, vignettes in the empty fields
//  4. closed-gate connectivity again; any new leak is sealed with a churchyard wall, otherwise abort without saving
//  5. navigation rebake, spawns/stations/objective validated, scene saved, after shots
static partial class ElderboomMapRework
{
    const string MatDir = "Assets/GameAssets/Elderboom/ReworkMaterials";
    const string Marker = "Rework_Applied_v1";
    const string StartArea = "Village Square";
    static StringBuilder L;
    static void Flush() { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/map_apply.txt", L.ToString()); }

    [MenuItem("Elderboom HDRP/Map Rework/7 Apply")]
    static void Apply()
    {
        L = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (GameObject.Find(Marker) != null) { L.AppendLine("already applied (marker present)"); Flush(); return; }
        try
        {
            Directory.CreateDirectory(MatDir);
            Physics.SyncTransforms();
            var before = Connectivity("BEFORE (gates closed)"); Flush();

            Section("materials"); FixGltfMaterials(); FixFoliageTemplates(); Flush();
            Section("pond"); FixPond(); Flush();
            Section("red bushes"); FixRedBush(); Flush();
            Section("church"); ChurchRescale(); Flush();
            Section("houses"); Houses(); Flush();
            Section("boulder"); MoveBoulder(); Flush();
            Section("vignettes"); Vignettes(); Flush();
            Physics.SyncTransforms();

            var after = Connectivity("AFTER (gates closed)");
            for (int i = 0; i < 4; i++)
            {
                var leak = before.Keys.FirstOrDefault(k => !before[k].reach && after.ContainsKey(k) && after[k].reach);
                if (leak == null) break;
                L.AppendLine("LEAK into " + leak + " -> sealing"); SealLeak(after[leak].target); Physics.SyncTransforms();
                after = Connectivity("AFTER seal " + (i + 1));
            }
            var still = before.Keys.Where(k => !before[k].reach && after.ContainsKey(k) && after[k].reach).ToList();
            var lost = before.Keys.Where(k => before[k].reach && after.ContainsKey(k) && !after[k].reach).ToList();
            if (still.Count > 0 || lost.Count > 0)
            {
                L.AppendLine("ABORT: leaks " + string.Join(",", still) + " lost " + string.Join(",", lost) + " -> scene reloaded, nothing saved");
                Flush(); NavMesh.RemoveAllNavMeshData(); EditorSceneManager.OpenScene(scene.path); return;
            }

            Section("navigation");
            L.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
            ValidateGameplay();
            new GameObject(Marker);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            L.AppendLine("SAVED"); Flush();
        }
        catch (System.Exception e)
        {
            L.AppendLine("EXCEPTION " + e); Flush();
            NavMesh.RemoveAllNavMeshData(); EditorSceneManager.OpenScene(scene.path); return;
        }
        GroundShots("Logs/rework_shots/after_");
        L.AppendLine("after shots done"); Flush();
    }

    static void Section(string s) => L.AppendLine("== " + s);

    // ---------------------------------------------------------------- connectivity
    class Reach { public bool reach; public Vector3 target; }

    static Terrain MainTerrain() => Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => t.gameObject.layer != 30).OrderByDescending(t => t.terrainData.size.x).First();

    static List<NavMeshBuildSource> Sources(bool gatesClosed)
    {
        var terrain = MainTerrain();
        var s = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = terrain.terrainData, transform = terrain.transform.localToWorldMatrix, area = 0 } };
        var creek = GameObject.Find("Creek_Continuous_Surface");
        var cmf = creek != null ? creek.GetComponent<MeshFilter>() : null;
        if (cmf != null && cmf.sharedMesh != null) s.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = cmf.sharedMesh, transform = creek.transform.localToWorldMatrix, area = 1 });
        if (!gatesClosed)
            foreach (var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (gate.reveal != null && !gate.reveal.activeInHierarchy)
                    foreach (var box in gate.reveal.GetComponentsInChildren<BoxCollider>(true))
                        s.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = box.transform.localToWorldMatrix * Matrix4x4.Translate(box.center), size = box.size, area = 0 });
        foreach (var c in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c.sharedMesh == null || !c.enabled || !c.gameObject.activeInHierarchy || c.GetComponent<Terrain>() != null) continue;
            if (!gatesClosed && c.GetComponentInParent<ElderboomGateLeaves>() != null) continue;
            s.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = c.sharedMesh, transform = c.transform.localToWorldMatrix, area = 0 });
        }
        foreach (var b in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!b.enabled || !b.gameObject.activeInHierarchy || b.isTrigger) continue;
            bool gateish = b.GetComponent<ElderboomGate>() != null || b.GetComponentInParent<ElderboomGateLeaves>() != null;
            if (gateish && !gatesClosed) continue;
            s.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, transform = b.transform.localToWorldMatrix * Matrix4x4.Translate(b.center), size = b.size, area = 0 });
        }
        return s;
    }

    static void LoadNav(List<NavMeshBuildSource> sources)
    {
        var st = NavMesh.GetSettingsByIndex(0);
        st.agentRadius = .4f; st.agentHeight = 1.8f; st.agentClimb = .35f; st.agentSlope = 32f;
        var nav = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(st, sources, new UnityEngine.Bounds(new Vector3(5.7f, 4f, -.7f), new Vector3(300f, 40f, 300f)), Vector3.zero, Quaternion.identity);
        NavMesh.RemoveAllNavMeshData(); if (nav != null) NavMesh.AddNavMeshData(nav);
    }

    static Vector3 VillageStart()
    {
        var g = GameObject.Find("Station_PISTOLS"); var p = g != null ? g.transform.position + g.transform.forward * 2f : new Vector3(5.7f, 1, -.7f);
        return NavMesh.SamplePosition(p, out var h, 5f, NavMesh.AllAreas) ? h.position : p;
    }

    static string AreaAt(Vector3 p)
    {
        foreach (var a in Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (a.Contains(p)) return a.areaName;
        return null;
    }

    static Dictionary<string, Reach> Connectivity(string tag)
    {
        LoadNav(Sources(true));
        var res = new Dictionary<string, Reach>(); var start = VillageStart(); var path = new NavMeshPath();
        L.AppendLine("== connectivity " + tag + " from " + start.ToString("F1"));
        var spawns = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var a in Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (a.areaName == StartArea) continue;
            var targets = spawns.Where(s => a.Contains(s.transform.position)).Select(s => s.transform.position).Take(4).ToList();
            if (a.polygon != null && a.polygon.Length > 2) { var c = Vector2.zero; foreach (var v in a.polygon) c += v; c /= a.polygon.Length; var cp = new Vector3(c.x, Ground(c.x, c.y), c.y); if (a.Contains(cp)) targets.Add(cp); }
            var r = new Reach();
            foreach (var t in targets)
            {
                if (!NavMesh.SamplePosition(t, out var h, 4f, NavMesh.AllAreas)) continue;
                if (NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) { r.reach = true; r.target = h.position; break; }
            }
            res[a.areaName] = r;
            L.AppendLine("   " + a.areaName + (a.unlockedAtStart ? " (unlocked at start)" : "") + " reachable=" + r.reach + " targets " + targets.Count);
        }
        return res;
    }

    // Wall across the leak: where the leaking path leaves the Village Square, perpendicular to it, between existing blockers.
    static void SealLeak(Vector3 target)
    {
        var start = VillageStart(); var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start, target, NavMesh.AllAreas, path)) { L.AppendLine("   seal: no path"); return; }
        var c = path.corners; Vector3 cross = Vector3.zero, dir = Vector3.forward; bool found = false;
        for (int i = 0; i + 1 < c.Length && !found; i++)
        {
            float len = Vector3.Distance(c[i], c[i + 1]);
            for (float d = 0; d < len; d += .5f)
            {
                var p = Vector3.Lerp(c[i], c[i + 1], d / Mathf.Max(.01f, len));
                if (AreaAt(p) != StartArea) { cross = p; dir = c[i + 1] - c[i]; dir.y = 0; dir.Normalize(); found = true; break; }
            }
        }
        if (!found) { L.AppendLine("   seal: crossing not found"); return; }
        var n = Vector3.Cross(Vector3.up, dir).normalized;
        float a = Blocker(cross, -n), b = Blocker(cross, n);
        BuildWall(cross - n * a, cross + n * b);
        L.AppendLine("   seal at " + cross.ToString("F1") + " span " + (a + b).ToString("F1") + " m");
    }

    static float Blocker(Vector3 from, Vector3 dir)
    {
        var o = from + Vector3.up * 1f; float best = 25f;
        foreach (var h in Physics.RaycastAll(o, dir, 25f, ~((1 << 2) | (1 << 19)), QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponent<Terrain>() == null && h.distance < best) best = h.distance;
        return best + .4f;
    }

    static GameObject WallTemplate()
    {
        var root = GameObject.Find("Elderboom_Access_Redesign/churchyard_front");
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>())
            if (t.GetComponent<MeshRenderer>() != null && t.GetComponent<Collider>() != null) return t.gameObject;
        return null;
    }

    static void BuildWall(Vector3 a, Vector3 b)
    {
        var tpl = WallTemplate(); if (tpl == null) { L.AppendLine("   no wall template"); return; }
        var parent = GameObject.Find("Rework_Churchyard_Wall") ?? new GameObject("Rework_Churchyard_Wall");
        var tb = tpl.GetComponent<Renderer>().bounds; float piece = Mathf.Max(1f, Mathf.Max(tb.size.x, tb.size.z) * .92f);
        Vector3 longAxis = tb.size.x >= tb.size.z ? Vector3.right : Vector3.forward;
        var dir = b - a; dir.y = 0; float len = dir.magnitude; dir.Normalize();
        int count = Mathf.Max(1, Mathf.CeilToInt(len / piece));
        for (int i = 0; i < count; i++)
        {
            var at = a + dir * (piece * (i + .5f)); at.y = Ground(at.x, at.z);
            var g = Object.Instantiate(tpl, parent.transform); g.name = "Wall_" + i;
            g.transform.rotation = tpl.transform.rotation;
            g.transform.Rotate(Vector3.up, Vector3.SignedAngle(longAxis, dir, Vector3.up), Space.World);
            g.transform.localScale = tpl.transform.lossyScale;
            Physics.SyncTransforms();
            var gb = g.GetComponent<Renderer>().bounds;
            g.transform.position += new Vector3(at.x - gb.center.x, at.y - .08f - gb.min.y, at.z - gb.center.z);
        }
    }

    // ---------------------------------------------------------------- materials
    static Texture TexOf(Material m)
    {
        if (m.HasProperty("baseColorTexture") && m.GetTexture("baseColorTexture") != null) return m.GetTexture("baseColorTexture");
        if (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) return m.GetTexture("_BaseColorMap");
        return m.mainTexture;
    }
    static Color ColOf(Material m) => m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;

    static Material Lit(string name, Texture tex, Color col, float metallic, float smooth, bool twoSided)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseColorMap", tex); m.SetColor("_BaseColor", col);
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
        if (twoSided) { m.SetFloat("_DoubleSidedEnable", 1f); m.doubleSidedGI = true; }
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m);
        return m;
    }

    static int ConvertGltf(GameObject root, string prefix, System.Func<Material, Material> make)
    {
        var cache = new Dictionary<Material, Material>(); int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i]; if (src == null || src.shader == null || !src.shader.name.Contains("glTF")) continue;
                if (!cache.TryGetValue(src, out var dst)) { dst = make(src); cache[src] = dst; }
                mats[i] = dst; changed = true; n++;
            }
            if (changed) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
        L.AppendLine("   " + prefix + ": " + n + " slots, " + cache.Count + " materials");
        return n;
    }

    static void FixGltfMaterials()
    {
        var inn = GameObject.Find("SouthCreek_Timber_Inn");
        if (inn) ConvertGltf(inn, "inn", s => Lit("Inn_" + Safe(s.name), TexOf(s), ColOf(s) * new Color(.82f, .8f, .78f, 1f), 0f, .18f, true));
        var amph = GameObject.Find("Woodland_Stone_Amphitheatre");
        if (amph) ConvertGltf(amph, "amphitheatre", s => Lit("Amph_" + Safe(s.name), TexOf(s), ColOf(s) * new Color(.85f, .84f, .8f, 1f), 0f, .16f, true));
        var bank = GameObject.Find("Elderboom_Village_Expansion/Riverbank_Stone_And_Spearhead_Iron");
        int k = 0;
        if (bank) ConvertGltf(bank, "creek railing", s => TexOf(s) == null
            ? Lit("Rail_Iron_" + (k++), null, new Color(.075f, .075f, .085f), .65f, .32f, false)
            : Lit("Rail_Stone_" + (k++), TexOf(s), ColOf(s) * new Color(.6f, .58f, .54f, 1f), 0f, .14f, false));
        // Church plinth disc (bright slab sticking out east of the church): warmer, darker stone.
        var disc = GameObject.Find("FallenCity_Church_HumanScale/Circle.051");
        if (disc)
        {
            var r = disc.GetComponent<Renderer>(); var src = r.sharedMaterial;
            string path = MatDir + "/Church_Parvis_Stone.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(.46f, .43f, .39f)); m.SetFloat("_Smoothness", .22f);
            HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); r.sharedMaterial = m; EditorUtility.SetDirty(r);
            L.AppendLine("   church plinth restyled");
        }
    }

    static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    static void FixFoliageTemplates()
    {
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("FOLIAGE_TEMPLATE")) continue;
            bool broken = r.sharedMaterials.Any(m => m == null || (m.shader != null && m.shader.name.Contains("glTF") && TexOf(m) == null));
            if (!broken) continue;
            r.gameObject.SetActive(false); EditorUtility.SetDirty(r.gameObject);
            L.AppendLine("   disabled untextured " + Path(r.transform) + " at " + r.transform.position.ToString("F1"));
        }
    }

    // ---------------------------------------------------------------- pond
    static void FixPond()
    {
        var pond = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.name.StartsWith("SM_AssetPlatform3")).OrderBy(r => (r.bounds.center - new Vector3(-30.4f, 1f, 29.4f)).sqrMagnitude).FirstOrDefault();
        if (pond == null || (pond.bounds.center - new Vector3(-30.4f, 1f, 29.4f)).magnitude > 6f) { L.AppendLine("   pond not found"); return; }
        var water = Lit("Pond_StillWater", null, new Color(.03f, .045f, .04f), 0f, .965f, false);
        pond.sharedMaterials = pond.sharedMaterials.Select(_ => water).ToArray(); EditorUtility.SetDirty(pond);
        var pb = pond.bounds; float R = Mathf.Max(pb.extents.x, pb.extents.z);
        L.AppendLine("   pond " + pb.center.ToString("F1") + " radius " + R.ToString("F1"));
        // Decorative (non-colliding) fence pieces crossing the water.
        int hidden = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.name.Contains("Modular_Fence") || r.GetComponent<Collider>() != null) continue;
            var b = r.bounds; var c = new Vector2(b.center.x, b.center.z);
            if ((c - new Vector2(pb.center.x, pb.center.z)).magnitude > R - .3f) continue;
            r.gameObject.SetActive(false); hidden++;
        }
        L.AppendLine("   fence pieces hidden over the water: " + hidden);
        // Rim: river stones and grass tufts around the edge.
        var parent = new GameObject("Rework_Pond_Rim").transform;
        var stone = PickRock();
        var grass = GameObject.Find("Elderboom_Environment_Art/Ecological_Contact_Layers/Grass_A");
        var fern = GameObject.Find("Elderboom_Environment_Art/Ecological_Contact_Layers/Fern_B");
        var rnd = new System.Random(29);
        int stones = 0, tufts = 0;
        for (int i = 0; i < 26; i++)
        {
            float ang = i * Mathf.PI * 2f / 26f + (float)rnd.NextDouble() * .15f;
            var dirv = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            bool isStone = i % 3 == 0;
            var src = isStone ? stone : (i % 2 == 0 ? fern : grass);
            if (src == null) continue;
            var p = pb.center + dirv * (R + (isStone ? .1f : .7f + (float)rnd.NextDouble() * .8f));
            if (SpawnNear(p, 2.5f)) continue;
            var g = Object.Instantiate(src, parent); g.name = (isStone ? "RimStone_" : "RimTuft_") + i;
            float sc = isStone ? .7f + (float)rnd.NextDouble() * .5f : .9f + (float)rnd.NextDouble() * .5f;
            g.transform.localScale = src.transform.lossyScale * sc;
            g.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0) * src.transform.rotation;
            Physics.SyncTransforms();
            float gy = TerrainY(p.x, p.z);
            var gb = Bounds(g);
            g.transform.position += new Vector3(p.x - gb.center.x, gy - (isStone ? .25f * gb.size.y : .05f) - gb.min.y, p.z - gb.center.z);
            if (isStone) { foreach (var col in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col); stones++; } else tufts++;
        }
        L.AppendLine("   rim stones " + stones + " (" + (stone ? stone.name : "none") + "), tufts " + tufts);
    }

    static GameObject PickRock()
    {
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).OrderBy(r => r.name))
        {
            var n = r.name.ToLowerInvariant(); if (!(n.Contains("rock") || n.Contains("stone")) || n.Contains("wall") || n.Contains("grave") || n.Contains("bank") || n.Contains("bridge")) continue;
            var s = r.bounds.size; if (s.y < .35f || s.y > 1.3f || Mathf.Max(s.x, s.z) < .8f || Mathf.Max(s.x, s.z) > 2.6f) continue;
            if (r.transform.childCount > 0) continue;
            return r.gameObject;
        }
        return null;
    }

    static float TerrainY(float x, float z) { var t = MainTerrain(); return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y; }

    static bool SpawnNear(Vector3 p, float r) =>
        Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Any(s => Flat(s.transform.position - p) < r);
    static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

    // ---------------------------------------------------------------- red bushes
    static void FixRedBush()
    {
        var targets = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r.sharedMaterials.Any(m => m != null && m.name == "Bush_Red")).ToList();
        if (targets.Count == 0) { L.AppendLine("   no Bush_Red"); return; }
        var src = targets[0].sharedMaterials.First(m => m != null && m.name == "Bush_Red");
        Texture srcTex = null; string texProp = null;
        foreach (var p in src.GetTexturePropertyNames()) { var t = src.GetTexture(p); if (t != null && t.name.Contains("BaseColor")) { srcTex = t; texProp = p; break; } }
        if (srcTex == null) { L.AppendLine("   Bush_Red base texture not found"); return; }
        string pngPath = MatDir + "/Red_Bush_BaseColor_Muted.png";
        var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(srcTex, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, srcTex.width, srcTex.height), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i]; Color.RGBToHSV(c, out var h, out var s, out var v);
            if (s > .12f) { h = .075f + (h > .5f ? (h - 1f) : h) * .15f; if (h < 0) h += 1f; s *= .5f; v *= .78f; }
            var o = Color.HSVToRGB(h, s, v); o.a = c.a; px[i] = o;
        }
        tex.SetPixels(px); File.WriteAllBytes(pngPath, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(pngPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(pngPath); imp.sRGBTexture = true; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.SaveAndReimport();
        var muted = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
        string matPath = MatDir + "/Bush_Autumn_Muted.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(src); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture(texProp, muted); EditorUtility.SetDirty(mat);
        foreach (var r in targets) { r.sharedMaterials = r.sharedMaterials.Select(m => m == src ? mat : m).ToArray(); EditorUtility.SetDirty(r); }
        L.AppendLine("   red bush renderers retinted: " + targets.Count + " (texture prop " + texProp + ")");
    }

    // ---------------------------------------------------------------- church
    static float ChurchFloor(GameObject church, Vector3 p, float above)
    {
        float best = float.NaN, bestD = 99f;
        foreach (var h in Physics.RaycastAll(p + Vector3.up * above, Vector3.down, above + 4f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.transform.IsChildOf(church.transform) && h.distance < bestD) { bestD = h.distance; best = h.point.y; }
        return best;
    }

    static void ChurchRescale()
    {
        var church = GameObject.Find("FallenCity_Church_HumanScale");
        if (church == null) { L.AppendLine("   church missing"); return; }
        const float s = .65f;
        var cb = Bounds(church);
        var anchor = new Vector3(cb.center.x + cb.extents.x * .6f, church.transform.position.y, cb.center.z);
        var altar = GameObject.Find("Church_Altar_Weapon_Upgrader");
        var tech = GameObject.Find("Church_Tech_Setup");
        Physics.SyncTransforms();
        // Seat heights before (gap between each piece and the church floor under it).
        float altarGap = float.NaN, altarFloor0 = float.NaN;
        if (altar) { altarFloor0 = ChurchFloor(church, altar.transform.position, .6f); altarGap = altar.transform.position.y - altarFloor0; }
        var techKids = new List<(Transform t, float gap)>();
        if (tech) foreach (Transform t in tech.transform) { float f = ChurchFloor(church, t.position, .6f); techKids.Add((t, float.IsNaN(f) ? float.NaN : t.position.y - f)); }

        var ct = church.transform;
        ct.position = anchor + (ct.position - anchor) * s; ct.localScale *= s; EditorUtility.SetDirty(ct);
        Physics.SyncTransforms();
        var nb = Bounds(church);
        L.AppendLine("   church " + cb.size.ToString("F1") + " -> " + nb.size.ToString("F1") + ", x " + cb.min.x.ToString("F1") + ".." + cb.max.x.ToString("F1") + " -> " + nb.min.x.ToString("F1") + ".." + nb.max.x.ToString("F1"));

        Vector3 delta = Vector3.zero;
        if (altar)
        {
            var o = altar.transform.position; var n = anchor + (o - anchor) * s;
            float f = ChurchFloor(church, new Vector3(n.x, o.y, n.z), .6f);
            n.y = !float.IsNaN(f) && !float.IsNaN(altarGap) ? f + altarGap : anchor.y + (o.y - anchor.y) * s;
            delta = new Vector3(n.x - o.x, 0, n.z - o.z);
            altar.transform.position = n; EditorUtility.SetDirty(altar.transform);
            L.AppendLine("   altar " + o.ToString("F2") + " -> " + n.ToString("F2") + " (floor " + altarFloor0.ToString("F2") + " -> " + f.ToString("F2") + ")");
        }
        foreach (var (t, gap) in techKids)
        {
            var p = t.position + delta;
            if (!float.IsNaN(gap)) { float f = ChurchFloor(church, new Vector3(p.x, t.position.y, p.z), .6f); if (!float.IsNaN(f)) p.y = f + gap; }
            t.position = p; EditorUtility.SetDirty(t);
        }
        Physics.SyncTransforms();
        foreach (var cable in Object.FindObjectsByType<ElderboomCable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) cable.Rebuild();
        L.AppendLine("   tech pieces moved " + techKids.Count + " by " + delta.ToString("F2"));
    }

    // ---------------------------------------------------------------- houses
    static bool IsSoft(Renderer r)
    {
        if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy) return true;
        var n = r.name.ToLowerInvariant();
        foreach (var k in new[] { "grass", "fern", "bush", "tree", "alder", "foliage", "leaf", "cypress", "billboard", "puddle", "road", "decal", "landscape" }) if (n.Contains(k)) return true;
        foreach (var m in r.sharedMaterials) if (m != null && m.shader != null && (m.shader.name.Contains("Nature") || m.shader.name.Contains("Grass") || m.name.Contains("Foliage"))) return true;
        return false;
    }

    static float Clearance(GameObject g, Renderer[] all)
    {
        var b = Bounds(g); float best = 30f;
        foreach (var r in all)
        {
            if (r.transform.IsChildOf(g.transform) || IsSoft(r)) continue;
            var o = r.bounds; if (o.size.y < 1f || o.size.x > 80f || o.size.z > 80f) continue;
            float dx = Mathf.Max(0, Mathf.Max(o.min.x - b.max.x, b.min.x - o.max.x)), dz = Mathf.Max(0, Mathf.Max(o.min.z - b.max.z, b.min.z - o.max.z));
            if (dx > 30 || dz > 30) continue;
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }

    static void Houses()
    {
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < 10; i++)
        {
            var g = GameObject.Find("Perimeter_Building_" + i); if (g == null) continue;
            var b = Bounds(g); var c = new Vector3(b.center.x, b.min.y, b.center.z);
            float clear = Clearance(g, all); float span = Mathf.Max(b.size.x, b.size.z);
            float s = Mathf.Clamp(1f + 1.6f * clear / span, 1f, 1.3f);
            var t = g.transform;
            if (s >= 1.04f) { t.position = c + (t.position - c) * s; t.localScale *= s; }
            else s = 1f;
            if (i % 2 == 1) t.RotateAround(new Vector3(b.center.x, 0, b.center.z), Vector3.up, 180f);
            EditorUtility.SetDirty(t);
            L.AppendLine("   " + g.name + " clearance " + clear.ToString("F1") + " -> x" + s.ToString("F2") + (i % 2 == 1 ? ", turned 180" : ""));
        }
    }

    // ---------------------------------------------------------------- boulder on the creek fence
    static void MoveBoulder()
    {
        var target = new Vector3(77.4f, 0, -55f);
        Transform rock = null;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (r.name.StartsWith("Geom_Rock_Overgrown_A") && Flat(r.bounds.center - target) < 3f) { rock = r.transform.parent; break; }
        if (rock == null) { L.AppendLine("   boulder not found"); return; }
        // Fence line through the bank stones; push the boulder fully to the north (cemetery) side, a bit smaller.
        Vector3 a = new Vector3(70.6f, 0, -50.2f), b = new Vector3(82.7f, 0, -56.4f);
        var along = (b - a).normalized; var north = Vector3.Cross(along, Vector3.up).normalized; if (north.z < 0) north = -north;
        var rb = Bounds(rock.gameObject); var cpos = new Vector3(rb.center.x, 0, rb.center.z);
        var onLine = a + along * Vector3.Dot(cpos - a, along);
        const float s = .7f; float half = Mathf.Max(rb.extents.x, rb.extents.z) * s;
        var dest = onLine + north * (half + 1.2f);
        if (SpawnNear(dest, half + 3f)) { L.AppendLine("   boulder: spawn too close, left in place"); return; }
        rock.localScale *= s;
        Physics.SyncTransforms(); var nb = Bounds(rock.gameObject);
        rock.position += new Vector3(dest.x - nb.center.x, TerrainY(dest.x, dest.z) - .6f - nb.min.y, dest.z - nb.center.z);
        Physics.SyncTransforms();
        EditorUtility.SetDirty(rock);
        L.AppendLine("   boulder " + rb.center.ToString("F1") + " -> " + Bounds(rock.gameObject).center.ToString("F1"));
    }

    // ---------------------------------------------------------------- vignettes in the empty fields
    static void Vignettes()
    {
        var tree = GameObject.Find("Elderboom_Village_Port/SM_BlackAlder_Field_02");
        var cart = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Abandoned_Farm_Cart")?.gameObject;
        var fern = GameObject.Find("Elderboom_Environment_Art/Ecological_Contact_Layers/Fern_B");
        var grass = GameObject.Find("Elderboom_Environment_Art/Ecological_Contact_Layers/Grass_A");
        var parent = new GameObject("Rework_Vignettes").transform;
        var keep = new List<Vector3>();
        foreach (var st in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (st.name.StartsWith("Station_") && st.parent == null) keep.Add(st.position);
        foreach (var gt in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) keep.Add(gt.transform.position);
        var spots = new[] { W(280, 330), W(1500, 1450), W(200, 1350), W(420, 160), W(1230, 1500) };
        var rnd = new System.Random(7); int placed = 0, skipped = 0;
        foreach (var c in spots)
        {
            if (AreaAt(new Vector3(c.x, 0, c.z)) == null) { L.AppendLine("   spot " + c.ToString("F0") + " outside the play areas, skipped"); continue; }
            var items = new List<(GameObject src, Vector3 off, float scale, bool solid)>
            {
                (tree, new Vector3(4.5f, 0, 3f), .9f, false), (tree, new Vector3(-5f, 0, -1.5f), 1.05f, false), (tree, new Vector3(1f, 0, 7f), .8f, false),
                (cart, new Vector3(-.5f, 0, -3.5f), 1f, true),
            };
            for (int k = 0; k < 8; k++) items.Add((k % 2 == 0 ? fern : grass, new Vector3((float)rnd.NextDouble() * 10f - 5f, 0, (float)rnd.NextDouble() * 10f - 5f), 1f + (float)rnd.NextDouble() * .4f, false));
            foreach (var (src, off, sc, solid) in items)
            {
                if (src == null) continue;
                var p = c + off; p.y = TerrainY(p.x, p.z);
                bool bad = SpawnNear(p, solid ? 6f : 3f) || keep.Any(k => Flat(k - p) < 10f) || AreaAt(p) == null;
                if (!bad && solid) bad = Physics.OverlapSphere(p + Vector3.up * 1.4f, 2.2f, ~(1 << 2), QueryTriggerInteraction.Ignore).Any(col => col.GetComponent<Terrain>() == null);
                if (bad) { skipped++; continue; }
                var g = Object.Instantiate(src, parent); g.name = src.name + "_v" + placed;
                g.transform.localScale = src.transform.lossyScale * sc;
                g.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0) * src.transform.rotation;
                Physics.SyncTransforms(); var gb = Bounds(g);
                g.transform.position += new Vector3(p.x - gb.center.x, p.y - .05f - gb.min.y, p.z - gb.center.z);
                placed++;
            }
        }
        L.AppendLine("   vignette pieces placed " + placed + ", skipped " + skipped + " (tree " + (tree ? "ok" : "missing") + ", cart " + (cart ? "ok" : "missing") + ")");
    }

    // ---------------------------------------------------------------- validation on the final navigation
    static void ValidateGameplay()
    {
        var start = VillageStart(); var path = new NavMeshPath();
        int ok = 0, off = 0, unreachable = 0; float moved = 0;
        foreach (var sp in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!NavMesh.SamplePosition(sp.transform.position, out var h, 1.5f, NavMesh.AllAreas)) { off++; L.AppendLine("   spawn OFF navmesh " + sp.name + " " + sp.transform.position.ToString("F1")); continue; }
            moved = Mathf.Max(moved, Vector3.Distance(h.position, sp.transform.position));
            sp.transform.position = h.position + Vector3.up * .05f; EditorUtility.SetDirty(sp.transform);
            if (NavMesh.CalculatePath(h.position, start, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) ok++;
            else { unreachable++; L.AppendLine("   spawn cannot reach the square " + sp.name + " " + sp.transform.position.ToString("F1")); }
        }
        L.AppendLine("   spawns ok " + ok + ", off navmesh " + off + ", unreachable " + unreachable + ", max snap " + moved.ToString("F2") + " m");
        var goals = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(t => (t.name.StartsWith("Station_") && t.parent == null) || t.name == "Horse_Salvage_Objective" || t.name == "Church_Altar_Weapon_Upgrader").ToList();
        foreach (var g in goals)
        {
            bool reach = NavMesh.SamplePosition(g.position + g.forward * 1.5f, out var h, 3f, NavMesh.AllAreas) && NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            if (!reach) reach = NavMesh.SamplePosition(g.position, out h, 4f, NavMesh.AllAreas) && NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            L.AppendLine("   " + g.name + " reachable=" + reach);
        }
    }
}
