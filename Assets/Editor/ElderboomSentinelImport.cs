using UnityEditor;

// Import settings for the Steel Sentinel (Resources/Elderboom/Sentinel): normal map and linear mask map, 2K textures.
sealed class ElderboomSentinelImport : AssetPostprocessor
{
    const string Folder = "Assets/Resources/Elderboom/Sentinel/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/Elderboom/Sentinel/")) return;
        Apply((TextureImporter)assetImporter, assetPath);
    }

    static bool Apply(TextureImporter importer, string path)
    {
        bool normal = path.EndsWith("_normal.png"), mask = path.EndsWith("_mask.png");
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        bool srgb = !mask && !normal;
        bool changed = importer.maxTextureSize != 2048 || importer.textureType != type || (!normal && importer.sRGBTexture != srgb);
        importer.maxTextureSize = 2048; importer.textureType = type;
        if (!normal) importer.sRGBTexture = srgb;
        return changed;
    }

    // Textures that were imported before this script compiled get fixed on the next editor load.
    [InitializeOnLoadMethod]
    static void Verify()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (var name in new[] { "Sentinel_texture_0.png", "Sentinel_texture_0_normal.png", "Sentinel_mask.png" })
            {
                var importer = AssetImporter.GetAtPath(Folder + name) as TextureImporter;
                if (importer != null && Apply(importer, Folder + name)) importer.SaveAndReimport();
            }
        };
    }
}
