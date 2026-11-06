using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomShockwavePreview
{
    const string ScenePath = "Assets/Scenes/ShockwavePreview.unity";
    const string OutDir = "Logs/shockwave_preview";

    public static void Play()
    {
        Directory.CreateDirectory(OutDir);
        string done = Path.Combine(OutDir, "play_done.txt");
        if (File.Exists(done)) File.Delete(done);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = BuildCamera();
        BuildVolume();
        BuildSun();
        BuildGround();
        BuildZombie();
        var rig = camera.gameObject.AddComponent<ElderboomShockwaveRig>();
        rig.captureOnPlay = true;
        rig.origin = new Vector3(0f, 0.9f, 0.6f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        double deadline = EditorApplication.timeSinceStartup + 480;
        EditorApplication.update += () =>
        {
            if (File.Exists(done)) return;
            if (EditorApplication.timeSinceStartup < deadline) return;
            File.AppendAllText(Path.Combine(OutDir, "report.txt"), "\nPLAY TIMEOUT\n");
            EditorApplication.Exit(2);
        };
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    static Camera BuildCamera()
    {
        var camera = new GameObject("ShockwaveCamera").AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 250f;
        camera.transform.position = new Vector3(3.4f, 2.15f, -7.6f);
        camera.transform.LookAt(new Vector3(0f, 1.05f, 0.6f));
        var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
        data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        return camera;
    }

    static void BuildVolume()
    {
        var volume = new GameObject("PreviewVolume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
    }

    static void BuildSun()
    {
        var sun = new GameObject("Moonlight");
        sun.transform.rotation = new Quaternion(0.25901439f, -0.32877086f, 0.09427353f, 0.90329052f);
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.5058824f, 0.27058825f, 0.1764706f);
        light.useColorTemperature = false;
        light.intensity = 12000f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.78f;
        sun.AddComponent<HDAdditionalLightData>();
    }

    static void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        var material = new Material(Shader.Find("HDRP/Lit"));
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ground/Ground051_Color_2K.jpg");
        if (texture != null)
        {
            material.SetTexture("_BaseColorMap", texture);
            material.SetTextureScale("_BaseColorMap", new Vector2(8f, 8f));
        }
        material.SetColor("_BaseColor", new Color(0.55f, 0.46f, 0.36f));
        material.SetFloat("_Smoothness", 0.08f);
        HDMaterial.ValidateMaterial(material);
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;

        var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = "Crate";
        crate.transform.position = new Vector3(1.7f, 0.55f, 1.4f);
        crate.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
        var crateMaterial = new Material(Shader.Find("HDRP/Lit"));
        crateMaterial.SetColor("_BaseColor", new Color(0.32f, 0.2f, 0.14f));
        crateMaterial.SetFloat("_Smoothness", 0.12f);
        HDMaterial.ValidateMaterial(crateMaterial);
        crate.GetComponent<MeshRenderer>().sharedMaterial = crateMaterial;
    }

    static void BuildZombie()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Zombie.prefab");
        var zombie = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        zombie.transform.position = new Vector3(0f, 0f, 0.6f);
        zombie.transform.rotation = Quaternion.Euler(0f, 210f, 0f);
        foreach (var agent in zombie.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
        var brain = zombie.GetComponent<ElderboomZombie>();
        if (brain != null) brain.enabled = false;
        foreach (var skin in zombie.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.updateWhenOffscreen = true;
        var animator = zombie.GetComponentInChildren<Animator>();
        if (animator != null) animator.speed = 1f;
    }
}
