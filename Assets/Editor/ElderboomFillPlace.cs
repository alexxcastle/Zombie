using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Plan file Logs/Fill/plan.txt, one piece or run per line:
//   obj <kitName> <x> <z> <lookX> <lookZ> [sink]   building/fountain, front (+Z) turned toward (lookX, lookZ)
//   run <kitName> <x1> <z1> <x2> <z2>              wall or fence modules tiled along the line, terrain following
// "place check": instantiate in a scratch root, report overlaps / distances, discard.
// "place apply": replace Elderboom_Fill_Architecture with the plan, rebake navigation, check every spawn still reaches
// the player start (and how much longer the path got), save the scene. "shots": pictures of every placed piece.
static partial class ElderboomFill
{
    const string Kit = "Assets/GameAssets/Environment/ArchitectureKit/", RootName = "Elderboom_Fill_Architecture";
    static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    struct Piece { public string kit, line; public GameObject go; }

    static List<Piece> Build(Transform root, Terrain terrain, StringBuilder sb)
    {
        var list = new List<Piece>();
        float H(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
        foreach (var raw in File.ReadAllLines(Dir + "/plan.txt"))
        {
            var line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
            var a = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + a[1] + "/" + a[1] + ".prefab");
            if (prefab == null) { sb.AppendLine("MISSING " + a[1]); continue; }
            if (a[0] == "obj")
            {
                float x = P(a[2]), z = P(a[3]); var look = new Vector3(P(a[4]) - x, 0, P(a[5]) - z);
                float sink = a.Length > 6 ? P(a[6]) : .15f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                go.transform.rotation = Quaternion.LookRotation(look.sqrMagnitude > .01f ? look.normalized : Vector3.forward);
                go.transform.position = new Vector3(x, 0, z); Physics.SyncTransforms();
                var b = Bounds(go); float lo = float.MaxValue, hi = float.MinValue;
                foreach (var c in Corners(go)) { float h = H(c.x, c.z); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
                go.transform.position = new Vector3(x, lo - sink, z);
                go.name = a[1] + "_" + list.Count;
                list.Add(new Piece { kit = a[1], line = line + $"  (yaw {go.transform.eulerAngles.y:F0}, ground range {hi - lo:F2})", go = go });
            }
            else if (a[0] == "run")
            {
                var p1 = new Vector3(P(a[2]), 0, P(a[3])); var p2 = new Vector3(P(a[4]), 0, P(a[5]));
                var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab); probe.transform.rotation = Quaternion.identity;
                float len = Bounds(probe).size.x - .02f; Object.DestroyImmediate(probe);
                var dir = (p2 - p1); float total = dir.magnitude; dir /= total; int n = Mathf.Max(1, Mathf.RoundToInt(total / len));
                var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up)) ; // local +X along the run
                rot = Quaternion.FromToRotation(rot * Vector3.right, dir) * rot;
                for (int i = 0; i < n; i++)
                {
                    var c = p1 + dir * (len * (i + .5f));
                    var e1 = c - dir * len * .5f; var e2 = c + dir * len * .5f;
                    float lo = Mathf.Min(H(e1.x, e1.z), H(e2.x, e2.z), H(c.x, c.z));
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                    go.transform.SetPositionAndRotation(new Vector3(c.x, lo - .08f, c.z), rot);
                    go.name = a[1] + "_" + list.Count;
                    list.Add(new Piece { kit = a[1], line = line + " #" + i, go = go });
                }
            }
        }
        Physics.SyncTransforms();
        return list;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>().Where(r => !r.name.Contains("LOD1") && !r.name.Contains("LOD2")).ToArray();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }
    // Footprint corners from the LOD0 mesh in the object's own frame (tight even when rotated).
    static IEnumerable<Vector3> Corners(GameObject go)
    {
        var t = go.transform; float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>().Where(m => !m.name.Contains("LOD1") && !m.name.Contains("LOD2") && m.sharedMesh))
        {
            var mb = mf.sharedMesh.bounds;
            foreach (var sx in new[] { -1, 1 }) foreach (var sy in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 })
            {
                var w = t.InverseTransformPoint(mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3(sx, sy, sz))));
                x0 = Mathf.Min(x0, w.x); x1 = Mathf.Max(x1, w.x); z0 = Mathf.Min(z0, w.z); z1 = Mathf.Max(z1, w.z);
            }
        }
        foreach (var x in new[] { x0, 0, x1 }) foreach (var z in new[] { z0, 0, z1 }) yield return t.TransformPoint(new Vector3(x, 0, z));
    }
    static Vector3 LocalSize(GameObject go)
    {
        var c = Corners(go).Select(p => go.transform.InverseTransformPoint(p)).ToList();
        return new Vector3(c.Max(p => p.x) - c.Min(p => p.x), Bounds(go).size.y, c.Max(p => p.z) - c.Min(p => p.z));
    }

    static string Check(List<Piece> pieces, Transform root, StringBuilder sb)
    {
        int hard = 0;
        var spawns = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Select(s => (s.name, s.transform.position)).ToList();
        var gates = Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Where(g => g.isActiveAndEnabled).Select(g => (g.name, g.transform.position)).ToList();
        var inter = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m is ElderboomStation || m is ElderboomAltarUpgrader).Select(m => (m.name, m.transform.position)).ToList();
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>().transform.position;
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None);
        foreach (var p in pieces)
        {
            var t = p.go.transform; var size = LocalSize(p.go);
            var center = t.position + Vector3.up * (size.y * .5f + .45f);
            var hits = Physics.OverlapBox(center, new Vector3(size.x * .5f - .15f, size.y * .5f - .2f, size.z * .5f - .15f), t.rotation, ~((1 << 30) | (1 << 2)), QueryTriggerInteraction.Ignore)
                .Where(h => !(h is TerrainCollider) && !h.transform.IsChildOf(root)).Select(h => h.transform.parent ? h.transform.parent.name + "/" + h.name : h.name).Distinct().ToList();
            float R = new Vector2(size.x, size.z).magnitude * .5f; var flat = new Vector2(t.position.x, t.position.z);
            string Near(List<(string, Vector3)> l, float lim) => string.Join(",", l.Where(q => Vector2.Distance(new Vector2(q.Item2.x, q.Item2.z), flat) < R + lim).Select(q => q.Item1 + "@" + Vector2.Distance(new Vector2(q.Item2.x, q.Item2.z), flat).ToString("F1")));
            string ns = Near(spawns, 2.5f), ng = Near(gates, 6f), ni = Near(inter, 4f);
            bool nearPlayer = Vector2.Distance(new Vector2(player.x, player.z), flat) < R + 8f;
            var area = areas.FirstOrDefault(a => a.Contains(t.position));
            bool bad = hits.Count > 0 || ns != "" || ng != "" || ni != "" || nearPlayer || area == null;
            if (bad) hard++;
            sb.AppendLine($"{(bad ? "CONFLICT" : "ok")} {p.go.name} pos {t.position.x:F1},{t.position.y:F2},{t.position.z:F1} size {size.x:F1}x{size.y:F1}x{size.z:F1} area {area?.areaName} | {p.line}"
                + (hits.Count > 0 ? " | hits " + string.Join(";", hits.Take(5)) : "") + (ns != "" ? " | spawns " + ns : "") + (ng != "" ? " | gates " + ng : "") + (ni != "" ? " | interact " + ni : "") + (nearPlayer ? " | player start" : ""));
        }
        return hard + " conflicts";
    }

    static Dictionary<string, float> Reach()
    {
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>().transform.position;
        NavMesh.SamplePosition(player, out var ph, 3f, NavMesh.AllAreas);
        var d = new Dictionary<string, float>(); var path = new NavMeshPath();
        var targets = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Select(s => (s.name, s.transform.position))
            .Concat(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m is ElderboomStation || m is ElderboomAltarUpgrader).Select(m => (m.name, m.transform.position)));
        foreach (var (name, pos) in targets)
        {
            float len = -1;
            if (NavMesh.SamplePosition(pos, out var h, 3f, NavMesh.AllAreas) && NavMesh.CalculatePath(h.position, ph.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            { len = 0; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]); }
            d[name] = len;
        }
        return d;
    }

    static string Place(string mode)
    {
        var terrain = Valley(); var sb = new StringBuilder();
        bool apply = mode.Contains("apply");
        var before = apply ? Reach() : null;
        var old = GameObject.Find(RootName); if (old) old.SetActive(false); Physics.SyncTransforms();
        var root = new GameObject(RootName + (apply ? "" : "_Check")).transform;
        var pieces = Build(root, terrain, sb);
        sb.Insert(0, Check(pieces, root, sb) + "\n");
        if (!apply) { Object.DestroyImmediate(root.gameObject); if (old) old.SetActive(true); Physics.SyncTransforms(); return sb.ToString(); }
        if (old) Object.DestroyImmediate(old);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
        sb.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        var after = Reach(); int broken = 0;
        foreach (var kv in before)
        {
            float a = after.TryGetValue(kv.Key, out var v) ? v : -1;
            bool bad = kv.Value > 0 && (a < 0 || a > kv.Value * 1.35f + 6f);
            if (bad) broken++;
            if (bad || (a > 0 && kv.Value > 0 && a > kv.Value + 3f)) sb.AppendLine($"{(bad ? "ROUTE BROKEN" : "route longer")} {kv.Key} {kv.Value:F0} -> {a:F0} m");
        }
        sb.AppendLine("routes checked " + before.Count + ", broken " + broken);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveOpenScenes();
        sb.AppendLine("APPLIED " + pieces.Count + " pieces, scene saved");
        return sb.ToString();
    }

    // The procedural timber/glass modules shipped their normal maps in the base colour slot (flat 128,128,~245):
    // timber gets the Poly Haven weathered brown planks albedo + normal it was built with, no metal.
    static string FixMaterials()
    {
        const string PH = "Assets/ThirdParty/PolyHaven/Environment/weathered_brown_planks_";
        var nrmPath = PH + "nor_gl_2k.jpg"; var ti = (TextureImporter)AssetImporter.GetAtPath(nrmPath);
        if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(PH + "diff_2k.jpg"); var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath);
        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/GameAssets/Environment/ArchitectureKit" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid); var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var tex = m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") as Texture2D : null; if (tex == null) continue;
            var tp = AssetDatabase.GetAssetPath(tex); var imp = (TextureImporter)AssetImporter.GetAtPath(tp);
            bool readable = imp.isReadable; if (!readable) { imp.isReadable = true; imp.SaveAndReimport(); }
            var small = tex.GetPixels32(Mathf.Min(4, tex.mipmapCount - 1)); double r = 0, g = 0, b = 0; foreach (var c in small) { r += c.r; g += c.g; b += c.b; } r /= small.Length; g /= small.Length; b /= small.Length;
            if (!readable) { imp.isReadable = false; imp.SaveAndReimport(); }
            bool normalAsColour = b > 200 && Mathf.Abs((float)r - 128) < 30 && Mathf.Abs((float)g - 128) < 30;
            sb.AppendLine($"{path} {tex.name} mean {r:F0},{g:F0},{b:F0} {(normalAsColour ? "-> planks" : "")}");
            if (!normalAsColour) continue;
            m.SetTexture("_BaseColorMap", diff); m.SetColor("_BaseColor", new Color(.86f, .84f, .82f, 1)); m.SetTexture("_NormalMap", nrm); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            m.SetFloat("_Metallic", 0); m.SetFloat("_Smoothness", .22f); m.SetFloat("_NormalScale", 1f);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets(); return sb.ToString();
    }

    static string Shots(string mode)
    {
        var root = GameObject.Find(RootName); if (!root) return "FAIL nothing placed";
        var sb = new StringBuilder(); Directory.CreateDirectory(Dir + "/shots");
        foreach (var f in Directory.GetFiles(Dir + "/shots")) File.Delete(f);
        var light = new GameObject("FillShotSun"); var l = light.AddComponent<Light>(); l.type = LightType.Directional; light.AddComponent<HDAdditionalLightData>(); l.intensity = 6f; light.transform.rotation = Quaternion.Euler(35, 140, 0);
        var terrain = Valley(); int i = 0;
        var done = new HashSet<string>();
        foreach (Transform t in root.transform)
        {
            string group = t.name.Substring(0, t.name.LastIndexOf('_'));
            bool building = !(group.StartsWith("fence") || group.EndsWith("_source"));
            if (!building && done.Contains(group)) continue; done.Add(group);
            var b = Bounds(t.gameObject); float dist = Mathf.Max(10f, b.size.magnitude * 1.3f);
            var dir = t.forward; if (!building) dir = t.forward;
            var eye = b.center + Quaternion.Euler(0, 35, 0) * dir * dist; eye.y = terrain.SampleHeight(eye) + terrain.transform.position.y + 1.8f + b.size.y * .15f;
            var go = new GameObject("FillShotCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.fieldOfView = 60; cam.nearClipPlane = .1f; cam.farClipPlane = 600; go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(b.center - eye));
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; for (int k = 0; k < 4; k++) cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            string file = Dir + "/shots/" + (i++).ToString("00") + "_" + t.name + ".jpg"; File.WriteAllBytes(file, tex.EncodeToJPG(85));
            RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            sb.AppendLine(file);
        }
        Object.DestroyImmediate(light);
        var areas = Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None);
        TopShot(MapRect(areas), Dir + "/top_after.jpg");
        return sb.ToString();
    }
}
