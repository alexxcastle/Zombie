using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElderboomContentFix
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";

    public static void Apply()
    {
        FixGround();
        HideBrokenLandscape();
        ElderboomZombieRig.Rebuild();
        ElderboomViewmodelCalibrator.Run();
        HDRPSliceStandaloneBuild.BuildPort();
        Debug.Log("ELDERBOOM_CONTENT_FIX_DONE");
    }

    static void FixGround()
    {
        const string normalPath = "Assets/GameAssets/Ground/Ground051_NormalGL_2K.jpg";
        var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Elderboom_Port_Ground.mat");
        if (material == null) throw new System.InvalidOperationException("Ground material missing");
        material.SetColor("_BaseColor", new Color(0.85f, 0.8f, 0.72f, 1f));
        material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ground/Ground051_Color_2K.jpg"));
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.SetTextureScale("_BaseColorMap", Vector2.one * 104f);
        material.SetTextureScale("_NormalMap", Vector2.one * 104f);
        material.SetFloat("_NormalScale", 0.5f);
        material.SetFloat("_Smoothness", 0.04f);
        material.SetFloat("_Metallic", 0f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
    }

    static void HideBrokenLandscape()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int hidden = 0;
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string id = renderer.name;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) id += " " + filter.sharedMesh.name;
            if (id.ToLowerInvariant().Contains("landscape"))
            {
                renderer.gameObject.SetActive(false);
                hidden++;
            }
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("ELDERBOOM_LANDSCAPE_HIDDEN " + hidden);
    }
}
