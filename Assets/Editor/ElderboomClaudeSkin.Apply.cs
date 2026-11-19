using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// "apply": imports Assets/GameAssets/OperatorSkin textures, builds one HDRP/Lit material per arm atlas and maps every
// glTF material that uses that atlas (operator models and Resources/Elderboom/Grips copies, matched by file content)
// to it in Resources/Elderboom/OperatorSkin.asset.
static partial class ElderboomClaudeSkin
{
    const string SkinDir = "Assets/GameAssets/OperatorSkin";
    static readonly (string name, string albedoSource)[] Jobs =
    {
        ("Soph_Arms", "Assets/GameAssets/Soph/Soph_AKMN_Plasma_Textures/27_texture_0.png"),
        ("Eli_ArmL", "Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/05_texture_0.png"),
        ("Eli_ArmR", "Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore_Textures/07_texture_0.png"),
        ("Tactical_A", "Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/00_texture_0.png"),
        ("Tactical_B", "Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack_Textures/06_texture_0.png"),
    };

    static string Hash(string path) { using var md5 = System.Security.Cryptography.MD5.Create(); return System.BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))); }

    static Texture2D Import(string path, bool normal, bool srgb)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb && !normal;
        ti.maxTextureSize = 2048; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static partial void ApplyImpl(string mode, StringBuilder sb)
    {
        AssetDatabase.Refresh();
        var byHash = new Dictionary<string, Material>();
        foreach (var (name, src) in Jobs)
        {
            var alb = Import(SkinDir + "/" + name + "_Albedo.png", false, true);
            var nrm = Import(SkinDir + "/" + name + "_Normal.png", true, false);
            var msk = Import(SkinDir + "/" + name + "_Mask.png", false, false);
            string path = SkinDir + "/" + name + "_Skin.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseColorMap", alb); m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_NormalMap", nrm); m.SetFloat("_NormalScale", 1f); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            m.SetTexture("_MaskMap", msk); m.EnableKeyword("_MASKMAP");
            m.SetFloat("_MetallicRemapMin", 0); m.SetFloat("_MetallicRemapMax", 1); m.SetFloat("_SmoothnessRemapMin", 0); m.SetFloat("_SmoothnessRemapMax", 1);
            m.SetFloat("_AORemapMin", 0); m.SetFloat("_AORemapMax", 1); m.SetFloat("_Metallic", 0); m.SetFloat("_Smoothness", .5f);
            byHash[Hash(src)] = m;
            sb.AppendLine("material " + path);
        }
        var from = new List<Material>(); var to = new List<Material>();
        var files = new List<string>();
        foreach (var dir in new[] { "Assets/GameAssets/Soph", "Assets/GameAssets/Ported/Eli", "Assets/GameAssets/Ported/Tactical", "Assets/Resources/Elderboom/Grips" })
            files.AddRange(Directory.GetFiles(dir).Where(f => f.EndsWith(".gltf") || f.EndsWith(".glb")).Select(f => f.Replace('\\', '/')));
        foreach (var file in files)
            foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(file).OfType<Material>())
            {
                if (!mat.HasProperty("baseColorTexture")) continue;
                var tex = mat.GetTexture("baseColorTexture"); if (!tex) continue;
                string tp = AssetDatabase.GetAssetPath(tex); if (string.IsNullOrEmpty(tp) || !File.Exists(tp)) continue;
                if (!byHash.TryGetValue(Hash(tp), out var target)) continue;
                // The emissive watch layer (Eli's left arm) carries over.
                if (mat.HasProperty("emissiveTexture") && mat.GetTexture("emissiveTexture") && !target.GetTexture("_EmissiveColorMap"))
                {
                    var ef = mat.HasProperty("emissiveFactor") ? mat.GetColor("emissiveFactor") : Color.white;
                    target.SetTexture("_EmissiveColorMap", mat.GetTexture("emissiveTexture")); target.SetColor("_EmissiveColor", ef);
                    target.SetFloat("_UseEmissiveIntensity", 0); target.EnableKeyword("_EMISSIVE_COLOR_MAP");
                }
                if (mat.HasProperty("baseColorFactor")) target.SetColor("_BaseColor", mat.GetColor("baseColorFactor"));
                from.Add(mat); to.Add(target);
                sb.AppendLine("  " + file + " : " + mat.name + " -> " + target.name);
            }
        foreach (var m in to.Distinct()) { HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); }
        string mapPath = "Assets/Resources/Elderboom/OperatorSkin.asset";
        var map = AssetDatabase.LoadAssetAtPath<ElderboomOperatorSkin>(mapPath);
        if (map == null) { map = ScriptableObject.CreateInstance<ElderboomOperatorSkin>(); AssetDatabase.CreateAsset(map, mapPath); }
        map.from = from.ToArray(); map.to = to.ToArray(); EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        sb.AppendLine("mapped " + from.Count + " glTF materials");
    }
}
