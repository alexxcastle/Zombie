using UnityEditor;
using UnityEngine;

// Texture import rules for the operator arms (Resources/Elderboom/Grips) and the watch cosmetics:
// *_Normal* -> normal map, ORM / roughness / metallic maps -> linear, arm maps keep their 4K resolution.
// Logs/claude_gripreimport.txt -> force-reimports those folders.
[InitializeOnLoad]
sealed class ElderboomGripTextureImport : AssetPostprocessor
{
    static bool Ours(string p) => p.Contains("/Elderboom/Grips/") || p.Contains("/Elderboom/Cosmetics/");

    void OnPreprocessTexture()
    {
        if (!Ours(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        ti.maxTextureSize = 4096;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
        if (n.Contains("normal")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
        else if (n.Contains("orm") || n.Contains("rough") || n.Contains("metal")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; }
    }

    static ElderboomGripTextureImport() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1;
        const string Flag = "Logs/claude_gripreimport.txt";
        if (!System.IO.File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        System.IO.File.Delete(Flag);
        int count = 0;
        foreach (var dir in new[] { "Assets/Resources/Elderboom/Grips", "Assets/Resources/Elderboom/Cosmetics" })
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            { AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate); count++; }
        foreach (var dir in new[] { "Assets/Resources/Elderboom/Grips" })
            foreach (var p in System.IO.Directory.GetFiles(dir, "*.gltf")) AssetDatabase.ImportAsset(p.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        System.IO.File.WriteAllText("Logs/claude_gripreimport_result.txt", "reimported " + count + " textures " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
    }
}
