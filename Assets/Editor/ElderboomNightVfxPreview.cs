using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class ElderboomNightVfxPreview
{
    const string Flag = "Logs/night_preview_now.txt";

    static ElderboomNightVfxPreview()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.update += Kick;
    }

    static void Kick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Kick;
        File.Delete(Flag);
        Play();
    }

    const string SourceSky = "Assets/AllSkyFree/Night MoonBurst/AllSky_Night_MoonBurst Equirect.png";
    const string PitchedSky = "Assets/AllSkyFree/Night MoonBurst/NightMoonBurst_OverVillage.png";
    const string ProfilePath = "Assets/Settings/Elderboom_Night_Profile.asset";
    const string ScenePath = "Assets/Scenes/NightVfxPreview.unity";
    const string OutDir = "Logs/night_vfx_preview";

    public static void Play()
    {
        Directory.CreateDirectory(OutDir);
        string done = Path.Combine(OutDir, "play_done.txt");
        string report = Path.Combine(OutDir, "report.txt");
        if (File.Exists(done)) File.Delete(done);
        if (File.Exists(report)) File.Delete(report);
        if (!File.Exists(PitchedSky)) PitchCloudsOverVillage();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildCamera();
        BuildVolume();
        BuildMoon();
        BuildGround();
        BuildZombie();
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

    static void PitchCloudsOverVillage()
    {
        var importer = AssetImporter.GetAtPath(SourceSky) as TextureImporter;
        var previousShape = importer.textureShape;
        var previousCubemap = importer.generateCubemap;
        bool wasReadable = importer.isReadable;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceSky);
        if (source == null) throw new System.InvalidOperationException("Night sky texture did not import as a readable image");
        var sourcePixels = source.GetPixels();
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        const int width = 2048;
        const int height = 1024;
        const float pitch = 22f * Mathf.Deg2Rad;
        float cp = Mathf.Cos(pitch);
        float sp = Mathf.Sin(pitch);
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float lat = ((y + 0.5f) / height - 0.5f) * Mathf.PI;
            float sy = Mathf.Sin(lat);
            float cy = Mathf.Cos(lat);
            for (int x = 0; x < width; x++)
            {
                float lon = ((x + 0.5f) / width - 0.5f) * Mathf.PI * 2f;
                float dx = Mathf.Sin(lon) * cy;
                float dy = sy;
                float dz = Mathf.Cos(lon) * cy;
                float ry = dy * cp - dz * sp;
                float rz = dy * sp + dz * cp;
                float lat2 = Mathf.Asin(Mathf.Clamp(ry, -1f, 1f));
                float lon2 = Mathf.Atan2(dx, rz);
                float u = lon2 / (Mathf.PI * 2f) + 0.5f;
                float v = lat2 / Mathf.PI + 0.5f;
                pixels[y * width + x] = Sample(sourcePixels, sourceWidth, sourceHeight, u, v);
            }
        }
        var pitched = new Texture2D(width, height, TextureFormat.RGBA32, false);
        pitched.SetPixels(pixels);
        pitched.Apply();
        File.WriteAllBytes(PitchedSky, pitched.EncodeToPNG());
        Object.DestroyImmediate(pitched);
        importer.textureShape = previousShape;
        importer.generateCubemap = previousCubemap;
        importer.isReadable = wasReadable;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset(PitchedSky, ImportAssetOptions.ForceUpdate);
        var pitchedImporter = AssetImporter.GetAtPath(PitchedSky) as TextureImporter;
        pitchedImporter.textureShape = TextureImporterShape.TextureCube;
        pitchedImporter.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        pitchedImporter.sRGBTexture = true;
        pitchedImporter.mipmapEnabled = false;
        pitchedImporter.maxTextureSize = 2048;
        pitchedImporter.SaveAndReimport();
        var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(PitchedSky);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile.TryGet(out HDRISky sky))
        {
            sky.hdriSky.Override(cubemap);
            sky.rotation.Override(0f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
        File.AppendAllText(Path.Combine(OutDir, "report.txt"), "sky=" + (cubemap != null) + " " + PitchedSky + "\n");
    }

    static Color Sample(Color[] pixels, int width, int height, float u, float v)
    {
        u = u - Mathf.Floor(u);
        v = Mathf.Clamp01(v);
        float x = u * (width - 1);
        float y = v * (height - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = (x0 + 1) % width;
        int y1 = Mathf.Min(y0 + 1, height - 1);
        float tx = x - x0;
        float ty = y - y0;
        Color a = Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], tx);
        Color b = Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], tx);
        return Color.Lerp(a, b, ty);
    }

    static void BuildCamera()
    {
        var camera = new GameObject("NightCamera").AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 300f;
        camera.transform.position = new Vector3(1.6f, 1.7f, -7.2f);
        camera.transform.LookAt(new Vector3(0f, 6.2f, 5f));
        var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
        data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        camera.gameObject.AddComponent<ElderboomNightVfxRig>().captureOnPlay = true;
    }

    static void BuildVolume()
    {
        var volume = new GameObject("PreviewVolume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
    }

    static void BuildMoon()
    {
        var moon = new GameObject("Moonlight");
        moon.transform.rotation = new Quaternion(0.25901439f, -0.32877086f, 0.09427353f, 0.90329052f);
        var light = moon.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.16641437f, 0.2840521f, 0.41911763f);
        light.intensity = 2200f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.78f;
        moon.AddComponent<HDAdditionalLightData>();
    }

    static void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(6f, 1f, 6f);
        var material = new Material(Shader.Find("HDRP/Lit"));
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ground/Ground051_Color_2K.jpg");
        if (texture != null)
        {
            material.SetTexture("_BaseColorMap", texture);
            material.SetTextureScale("_BaseColorMap", new Vector2(10f, 10f));
        }
        material.SetColor("_BaseColor", new Color(0.42f, 0.4f, 0.38f));
        material.SetFloat("_Smoothness", 0.06f);
        HDMaterial.ValidateMaterial(material);
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;

        var wall = new Material(Shader.Find("HDRP/Lit"));
        wall.SetColor("_BaseColor", new Color(0.34f, 0.3f, 0.28f));
        HDMaterial.ValidateMaterial(wall);
        MakeHouse(new Vector3(-3.2f, 1.6f, 2.4f), new Vector3(3.2f, 3.2f, 2.4f), wall);
        MakeHouse(new Vector3(3.4f, 2.1f, 5.2f), new Vector3(2.8f, 4.2f, 3.2f), wall);
    }

    static void MakeHouse(Vector3 position, Vector3 scale, Material material)
    {
        var house = GameObject.CreatePrimitive(PrimitiveType.Cube);
        house.name = "House";
        house.transform.position = position;
        house.transform.localScale = scale;
        house.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void BuildZombie()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Zombie.prefab");
        var zombie = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        zombie.transform.position = new Vector3(0f, 0f, 1.2f);
        zombie.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        foreach (var agent in zombie.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
        var brain = zombie.GetComponent<ElderboomZombie>();
        if (brain != null) brain.enabled = false;
        foreach (var skin in zombie.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skin.updateWhenOffscreen = true;
    }
}
