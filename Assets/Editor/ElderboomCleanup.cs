using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor automation for the cleanup pass (Logs/claude_cleanup.txt -> Logs/claude_cleanup_result.txt).
// "deps": every asset the game can reach (build scenes, Resources, StreamingAssets, plus the folders the editor
// tools load by path), and per top folder how much is reachable and how much is not. Unused paths listed in
// Logs/cleanup_unused.txt.
[InitializeOnLoad]
static class ElderboomCleanup
{
    const string Flag = "Logs/claude_cleanup.txt", Result = "Logs/claude_cleanup_result.txt";
    static double next;
    static ElderboomCleanup() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, mode.StartsWith("deps") ? Deps() : mode.StartsWith("missing") ? Missing() : mode.StartsWith("textures") ? Textures() : "FAIL unknown mode " + mode); }
        catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static string Deps()
    {
        var roots = new List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled) roots.Add(s.path);
        roots.AddRange(AssetDatabase.FindAssets("", new[] { "Assets/Resources" }).Select(AssetDatabase.GUIDToAssetPath));
        if (AssetDatabase.IsValidFolder("Assets/StreamingAssets")) roots.AddRange(Directory.GetFiles("Assets/StreamingAssets", "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta")));
        // Settings assets the build always takes (render pipeline, volume profiles, input, quality).
        foreach (var p in Directory.GetFiles("ProjectSettings", "*.asset"))
            foreach (var d in AssetDatabase.GetDependencies(p, true)) roots.Add(d);
        var gp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline; if (gp != null) roots.Add(AssetDatabase.GetAssetPath(gp));
        for (int i = 0; i < QualitySettings.names.Length; i++) { var rp = QualitySettings.GetRenderPipelineAssetAt(i); if (rp != null) roots.Add(AssetDatabase.GetAssetPath(rp)); }
        var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true));
        foreach (var r in roots) used.Add(r);

        var sb = new StringBuilder();
        var unused = new StringBuilder();
        var all = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p)).ToList();
        var groups = new Dictionary<string, (long total, long usedBytes, int files, int usedFiles)>();
        foreach (var p in all)
        {
            var parts = p.Split('/');
            string key = parts.Length > 2 ? parts[1] + (parts[1] == "GameAssets" && parts.Length > 3 ? "/" + parts[2] : "") : parts[1];
            long size = 0; try { size = new FileInfo(p).Length; } catch { }
            bool u = used.Contains(p) || p.EndsWith(".cs") || p.EndsWith(".asmdef") || p.Contains("/Editor/");
            var g = groups.TryGetValue(key, out var x) ? x : (0, 0, 0, 0);
            groups[key] = (g.total + size, g.usedBytes + (u ? size : 0), g.files + 1, g.usedFiles + (u ? 1 : 0));
            if (!u) unused.AppendLine(p);
        }
        sb.AppendLine("roots " + roots.Count + ", reachable assets " + used.Count);
        sb.AppendLine("folder | files used/total | MB used/total");
        foreach (var kv in groups.OrderByDescending(k => k.Value.total))
            sb.AppendLine(kv.Key + " | " + kv.Value.usedFiles + "/" + kv.Value.files + " | " + (kv.Value.usedBytes / 1048576) + "/" + (kv.Value.total / 1048576));
        sb.AppendLine("-- packages reached:");
        foreach (var pk in used.Where(p => p.StartsWith("Packages/")).Select(p => p.Split('/')[1]).GroupBy(x => x).OrderByDescending(g => g.Count()))
            sb.AppendLine("  " + pk.Key + " " + pk.Count());
        File.WriteAllText("Logs/cleanup_unused.txt", unused.ToString());
        File.WriteAllText("Logs/cleanup_used.txt", string.Join("\n", used.OrderBy(x => x)));
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    // Scenes in the build and every prefab under Resources: missing scripts, URP-only components, broken material/mesh
    // references. Nothing is changed.
    static string Missing()
    {
        var sb = new StringBuilder();
        var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (!s.enabled) continue;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(s.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            int missing = 0, urp = 0, noMat = 0, noMesh = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (m > 0) { missing += m; if (missing < 15) sb.AppendLine("  missing script on " + t.name); }
                    foreach (var c in t.GetComponents<Component>()) if (c != null && c.GetType().FullName.Contains("Universal")) { urp++; if (urp < 8) sb.AppendLine("  URP component " + c.GetType().Name + " on " + t.name); }
                    var r = t.GetComponent<Renderer>(); if (r != null && r.sharedMaterials.Any(x => x == null)) { noMat++; if (noMat < 10) sb.AppendLine("  null material on " + t.name); }
                    var mf = t.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh == null) { noMesh++; if (noMesh < 10) sb.AppendLine("  null mesh on " + t.name); }
                }
            sb.AppendLine(s.path + ": missing scripts " + missing + ", URP components " + urp + ", renderers with null material " + noMat + ", null meshes " + noMesh);
        }
        if (!string.IsNullOrEmpty(active)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(active);
        int pm = 0, pu = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources", "Assets/GameAssets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g); var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (go == null) continue;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject); if (m > 0) { pm += m; if (pm < 10) sb.AppendLine("  prefab missing script: " + path + " / " + t.name); }
                foreach (var c in t.GetComponents<Component>()) if (c != null && c.GetType().FullName.Contains("Universal")) { pu++; if (pu < 10) sb.AppendLine("  prefab URP component: " + path + " / " + t.name); }
            }
        }
        sb.AppendLine("prefabs: missing scripts " + pm + ", URP components " + pu);
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    // Texture files split out of the .glb models (Logs/texture_roles.json): normal maps as normal maps, data maps
    // linear, colour maps sRGB; all compressed with a size limit. Then the models are reimported on them.
    [System.Serializable] class Role { public string role; public int max; }
    static string Textures()
    {
        var text = File.ReadAllText("Logs/texture_roles.json");
        var sb = new StringBuilder();
        int changed = 0, missing = 0;
        var models = new HashSet<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"]+)\":\\s*\\{\\s*\"role\":\\s*\"(\\w+)\",\\s*\"max\":\\s*(\\d+)"))
            {
                string path = m.Groups[1].Value, role = m.Groups[2].Value; int max = int.Parse(m.Groups[3].Value);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) { missing++; continue; }
                imp.textureShape = TextureImporterShape.Texture2D;
                imp.textureType = role == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = role == "color";
                imp.maxTextureSize = max;
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.mipmapEnabled = true;
                imp.isReadable = false;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
                changed++;
                var dir = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                var stem = Path.GetFileName(Path.GetDirectoryName(path)); if (stem.EndsWith("_Textures")) models.Add(dir + "/" + stem.Substring(0, stem.Length - 9) + ".gltf");
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        foreach (var model in models) if (File.Exists(model)) AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
        sb.AppendLine("textures set " + changed + ", not found " + missing + ", models reimported " + models.Count);
        // Report what the models hold now.
        long total = 0;
        foreach (var model in models)
        {
            long bytes = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(model)) if (o is Material mat)
                foreach (var name in mat.GetTexturePropertyNames()) { var t = mat.GetTexture(name) as Texture2D; if (t != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t); }
            int nullMats = 0; foreach (var o in AssetDatabase.LoadAllAssetsAtPath(model)) if (o is Renderer r && r.sharedMaterials.Any(x => x == null)) nullMats++;
            sb.AppendLine("  " + model + " textures in materials " + (bytes / 1048576) + " MB" + (nullMats > 0 ? " null materials " + nullMats : ""));
            total += bytes;
        }
        sb.AppendLine("total texture memory referenced by these models (shared counted per model) " + (total / 1048576) + " MB");
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
