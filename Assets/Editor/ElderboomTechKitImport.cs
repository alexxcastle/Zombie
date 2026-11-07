using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Logs/claude_techkit.txt -> builds the tech field kit prefabs from Assets/GameAssets/TechKit/<module>/<module>.gltf:
// textures max 2048 (normal map / linear mask set), real-world size from kit.json, pivot at the bottom centre, a box
// collider, static for batching. Also the procedural cable prefab. Pictures of the kit: Logs/TechKit/*.jpg.
[InitializeOnLoad]
static class ElderboomTechKitImport
{
    const string Flag = "Logs/claude_techkit.txt", Result = "Logs/claude_techkit_result.txt", Root = "Assets/GameAssets/TechKit", Out = "Logs/TechKit";
    static double next;
    static ElderboomTechKitImport() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, Run(mode)); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    [System.Serializable] class Entry { public string label; public float[] size_xyz_blender; }

    static string Run(string mode)
    {
        var sb = new StringBuilder(); Directory.CreateDirectory(Out); Directory.CreateDirectory(Root + "/Prefabs");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var kit = ParseKit(File.ReadAllText(Root + "/kit.json"));
        if (EditorSceneManager.GetActiveScene().isDirty) return "FAIL the open scene has unsaved changes: save it first";
        var scene = EditorSceneManager.GetActiveScene().path;
        var prefabs = new List<(string name, GameObject prefab, Vector3 size)>();
        foreach (var kv in kit)
        {
            string name = kv.Key, folder = Root + "/" + name, gltf = folder + "/" + name + ".gltf";
            foreach (var tex in Directory.GetFiles(folder + "/Textures", "*.png"))
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(tex.Replace('\\', '/')); if (imp == null) continue;
                bool normal = tex.Contains("normal"), linear = tex.Contains("metallic");
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = !normal && !linear; imp.maxTextureSize = 2048; imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.isReadable = false; imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
            AssetDatabase.ImportAsset(gltf, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(gltf);
            if (model == null) { sb.AppendLine("FAIL no model " + gltf); continue; }
            var root = new GameObject(name);
            var visual = (GameObject)Object.Instantiate(model, root.transform); visual.name = "Visual";
            var b = Bounds(visual);
            // Blender x,y,z (z up) = Unity x,z,y. Uniform scale so the largest side matches the planned largest side.
            float target = kv.Value.size_xyz_blender.Max(), now = Mathf.Max(b.size.x, b.size.y, b.size.z);
            visual.transform.localScale = Vector3.one * (target / now);
            b = Bounds(visual);
            visual.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(visual);
            var box = root.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            int tris = root.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh ? m.sharedMesh.triangles.Length / 3 : 0);
            var path = Root + "/Prefabs/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            prefabs.Add((name, prefab, b.size));
            sb.AppendLine(name + " (" + kv.Value.label + "): size " + b.size.ToString("F2") + " m, " + tris + " tris -> " + path);
        }
        // Cable prefab with its three materials.
        Material Mat(string n, Color c, float metal, float smooth)
        {
            string p = Root + "/Cable/" + n + ".mat"; Directory.CreateDirectory(Root + "/Cable");
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_DoubleSidedEnable", 1); HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
        }
        var cableGo = new GameObject("TechKit_Cable", typeof(MeshFilter), typeof(MeshRenderer));
        cableGo.GetComponent<MeshRenderer>().sharedMaterials = new[] { Mat("Cable_Rubber", new Color(.035f, .035f, .037f), 0, .32f), Mat("Cable_Plug", new Color(.11f, .11f, .12f), .8f, .45f), Mat("Cable_Collar", new Color(.62f, .63f, .65f), 1, .62f) };
        var cable = cableGo.AddComponent<ElderboomCable>(); cable.followGround = true; cable.Rebuild();
        PrefabUtility.SaveAsPrefabAsset(cableGo, Root + "/Prefabs/Cable.prefab"); Object.DestroyImmediate(cableGo);
        sb.AppendLine("Cable.prefab (procedural, points editable in the Inspector)");
        AssetDatabase.SaveAssets();
        Showcase(prefabs);
        EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? "Assets/Scenes/Elderboom_Unity_Port.unity" : scene); // discards the showcase
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    static Dictionary<string, Entry> ParseKit(string json)
    {
        // kit.json is {"name": {"label":..., "size_xyz_blender":[x,y,z]}, ...}
        var d = new Dictionary<string, Entry>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"([0-9]{2}_[a-z_]+)\": (\\{[^}]*\\})"))
            d[m.Groups[1].Value] = JsonUtility.FromJson<Entry>(m.Groups[2].Value);
        return d;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static void Showcase(List<(string name, GameObject prefab, Vector3 size)> items)
    {
        // Real lighting and reflections: the game scene (not saved; reopened afterwards), on open ground near a station.
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var station = Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None).OrderBy(st => st.name).FirstOrDefault();
        Vector3 basePos = station ? station.transform.position + station.transform.right * 6f : Vector3.zero;
        float GroundAt(Vector3 p) { var hits = Physics.RaycastAll(p + Vector3.up * 30, Vector3.down, 80); return hits.Length > 0 ? hits.Max(h => h.point.y) : p.y; }
        var ox = new GameObject("TechKitShowcase"); ox.transform.position = new Vector3(basePos.x, GroundAt(basePos), basePos.z);
        float x = 0; var props = items.Where(i => !i.name.Contains("helicopter")).ToList();
        var placed = new List<GameObject>();
        foreach (var it in props) { var g = (GameObject)PrefabUtility.InstantiatePrefab(it.prefab); x += it.size.x * .5f; var wp = ox.transform.position + new Vector3(x, 0, 0); g.transform.position = new Vector3(wp.x, GroundAt(wp), wp.z); x += it.size.x * .5f + .35f; placed.Add(g); }
        var cablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Cable.prefab");
        if (cablePrefab && placed.Count > 1)
        {
            var c = (GameObject)PrefabUtility.InstantiatePrefab(cablePrefab); c.transform.position = placed[0].transform.position + new Vector3(.6f, 0, -.9f);
            var cb = c.GetComponent<ElderboomCable>(); cb.points = new List<Vector3> { Vector3.zero, new Vector3(1.2f, 0, -.5f), new Vector3(2.6f, 0, -.2f), new Vector3(3.6f, 0, .3f) }; cb.Rebuild();
        }
        float width = x;
        var o = ox.transform.position;
        // The scene is at night: a work light over the kit, like the floodlights would give.
        var lamp = new GameObject("ShowcaseLight"); lamp.transform.position = o + new Vector3(width * .5f, 5f, -3f); lamp.transform.LookAt(o + new Vector3(width * .5f, 0, 0));
        var spot = lamp.AddComponent<Light>(); spot.type = LightType.Spot; spot.spotAngle = 80; spot.range = 40; lamp.AddComponent<HDAdditionalLightData>(); spot.intensity = 60000; spot.color = new Color(1f, .96f, .9f);
        Shot("kit_front", o + new Vector3(width * .5f, 1.4f, -width * .55f), o + new Vector3(width * .5f, .45f, 0), 1800, 700);
        Shot("kit_high", o + new Vector3(width * .5f, 3.2f, -3.2f), o + new Vector3(width * .5f, 0, 0), 1800, 900);
        // Close-ups, lit from the camera side, from both faces.
        lamp.SetActive(false);
        var key = new GameObject("CloseLight"); var kl = key.AddComponent<Light>(); kl.type = LightType.Spot; kl.spotAngle = 70; kl.range = 15; key.AddComponent<HDAdditionalLightData>(); kl.intensity = 25000;
        foreach (var g in placed)
        {
            var gb = Bounds(g); float d = gb.extents.magnitude * 2.6f;
            foreach (int side in new[] { -1, 1 })
            {
                var eye = gb.center + new Vector3(d * .55f, d * .45f, side * d);
                key.transform.position = eye + Vector3.up * .5f; key.transform.LookAt(gb.center);
                foreach (var other in placed) other.SetActive(other == g);
                Shot("close_" + g.name + (side < 0 ? "_a" : "_b"), eye, gb.center, 800, 600);
            }
        }
        foreach (var other in placed) other.SetActive(true);
        Object.DestroyImmediate(key); lamp.SetActive(true);
        foreach (var g in placed) Object.DestroyImmediate(g);
        var heli = items.FirstOrDefault(i => i.name.Contains("helicopter"));
        if (heli.prefab)
        {
            var h = (GameObject)PrefabUtility.InstantiatePrefab(heli.prefab);
            var hp = ox.transform.position + new Vector3(width * .5f, 0, 14f); h.transform.position = new Vector3(hp.x, GroundAt(hp), hp.z);
            var c0 = h.transform.position;
            var lamp2 = new GameObject("ShowcaseLight2"); lamp2.transform.position = c0 + new Vector3(8, 9, -10); lamp2.transform.LookAt(c0 + Vector3.up * 2);
            var spot2 = lamp2.AddComponent<Light>(); spot2.type = LightType.Spot; spot2.spotAngle = 90; spot2.range = 60; lamp2.AddComponent<HDAdditionalLightData>(); spot2.intensity = 200000;
            float s = Mathf.Max(heli.size.x, heli.size.z);
            Shot("helicopter", c0 + new Vector3(s * .8f, s * .38f, -s * .9f), c0 + new Vector3(0, heli.size.y * .45f, 0), 1600, 1000);
            Shot("helicopter_side", c0 + new Vector3(-s * 1.15f, s * .15f, 0), c0 + new Vector3(0, heli.size.y * .45f, 0), 1600, 1000);
            // A person-sized reference next to it.
            var man = GameObject.CreatePrimitive(PrimitiveType.Capsule); man.transform.position = c0 + new Vector3(heli.size.x * .5f + 1.2f, .9f, -1); man.transform.localScale = new Vector3(.5f, .9f, .5f);
            Shot("helicopter_scale", c0 + new Vector3(s * .7f, 2.2f, -s * .7f), c0 + new Vector3(0, 1.6f, 0), 1600, 1000);
        }
    }

    static void Shot(string name, Vector3 eye, Vector3 target, int w, int h)
    {
        var go = new GameObject("Cam"); var cam = go.AddComponent<Camera>(); var data = go.AddComponent<HDAdditionalCameraData>();
                cam.fieldOfView = 40; cam.nearClipPlane = .05f; cam.farClipPlane = 300; go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; for (int i = 0; i < 6; i++) cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        File.WriteAllBytes(Out + "/" + name + ".jpg", tex.EncodeToJPG(90));
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
    }
}
