using System;
using UnityEditor;
using UnityEngine;

// Scoped exclusively to this museum package. Existing weapon and village imports are untouched.
public sealed class MuseumTextureImportRules : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        const string prefix = "Assets/GameAssets/Museum/Textures/";
        if (!assetPath.StartsWith(prefix, StringComparison.Ordinal)) return;
        var importer = (TextureImporter)assetImporter;
        string name = System.IO.Path.GetFileName(assetPath);
        bool normal = name.StartsWith("normal_", StringComparison.Ordinal);
        bool data = name.StartsWith("data_", StringComparison.Ordinal);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !(normal || data);
        importer.isReadable = false;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.compressionQuality = 50;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.alphaIsTransparency = !normal && !data;
    }
}
