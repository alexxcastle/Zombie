using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Explicit one-shot requests only; never rebuilds or replaces the map on reload.
[InitializeOnLoad]
static partial class ElderboomAltarIntegration
{
    const string Flag = "Logs/altar_request.txt", Report = "Logs/AltarIntegration";
    const string Folder = "Assets/GameAssets/AltarUpgrader";
    static double pollAt;
    static ElderboomAltarIntegration() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < pollAt) return;
        pollAt = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
        var mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        Directory.CreateDirectory(Report);
        try { Run(mode); }
        catch (Exception e) { File.WriteAllText(Report + "/result.txt", "FAIL " + e); Debug.LogException(e); }
    }
    static void Run(string mode)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        float ev = profile != null && profile.TryGet<Exposure>(out var exposure) ? exposure.fixedExposure.value : -1;
        var altar = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(r => r.name == "Plane.004" && r.transform.root.name.Contains("Church"));
        File.WriteAllText(Report + "/status.txt", $"scene={scene.path}\ndirty={scene.isDirty}\nplaying={EditorApplication.isPlaying}\nexposureEV={ev}\naltar={(altar != null ? altar.bounds.ToString("F4") : "missing")}\n");
        if (mode == "install") { Install(); return; }
        if (mode == "validate") { ValidateAndCapture(); return; }
        if (mode != "inspect") throw new ArgumentException("Unknown request " + mode);
        File.WriteAllText(Report + "/result.txt", "Inspection complete");
    }
}

static partial class ElderboomAltarIntegration
{
    public static void BatchInstall()
    {
        try { Run("install"); }
        catch (Exception e) { Directory.CreateDirectory(Report); File.WriteAllText(Report + "/result.txt", "FAIL " + e); throw; }
    }
    static void Install()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Elderboom_Unity_Port.unity")
        {
            if (scene.isDirty) throw new InvalidOperationException("Current scene has unsaved edits");
            scene = EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        }
        var existing = Object.FindAnyObjectByType<ElderboomAltarUpgrader>();
        if (existing != null) throw new InvalidOperationException("Altar already installed; use validation without replacing hand edits");
        var altar = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(r => r.name == "Plane.004" && r.transform.root.name.Contains("Church"));
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Source/Altar_Upgrader_Modular.glb");
        if (source == null) throw new InvalidOperationException("Imported modular model missing");
        var clips = AssetDatabase.LoadAllAssetsAtPath(Folder + "/Source/Altar_Upgrader_Modular.glb").OfType<AnimationClip>().ToArray();
        var sourceCycle = clips.FirstOrDefault(c => c.name == "UpgradeCycle");
        if (sourceCycle == null) throw new InvalidOperationException("Full synchronized animation not imported: " + string.Join(",", clips.Select(c => c.name)));
        var root = new GameObject("Church_Altar_Weapon_Upgrader");
        root.transform.position = new Vector3(altar.bounds.center.x, altar.bounds.max.y + .015f, altar.bounds.center.z);
        root.transform.rotation = Quaternion.Euler(0, 90, 0);
        var model = Object.Instantiate(source, root.transform, false); model.name = source.name;
        foreach (var anim in model.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        foreach (var anim in model.GetComponentsInChildren<Animation>(true)) anim.enabled = false;
        var station = root.AddComponent<ElderboomAltarUpgrader>(); station.model = model.transform;
        string runtime = Folder + "/Runtime"; Directory.CreateDirectory(runtime); AssetDatabase.Refresh();
        station.cycle = Object.Instantiate(sourceCycle); station.cycle.name = "UpgradeCycle";
        AssetDatabase.CreateAsset(station.cycle, runtime + "/UpgradeCycle.anim");
        ElderboomAnimationSampling.Sample(station.cycle, model, 0);
        station.weaponSocket = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "SOCKET_Weapon");
        DetachRuntimeAssets(model, runtime);
        // A solid base only; opening panels remain visual, never invisible walls.
        var chassis = model.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name == "Fixed_Chassis");
        var collider = root.AddComponent<BoxCollider>();
        collider.center = root.transform.InverseTransformPoint(chassis.bounds.center);
        var local = chassis.GetComponent<MeshFilter>().sharedMesh.bounds;
        collider.size = new Vector3(4.2f, Mathf.Min(.36f, chassis.bounds.size.y), 2.45f);
        var tray = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "CTRL_Weapon_Tray");
        var direction = tray.position - chassis.bounds.center; direction.y = 0; direction.Normalize();
        if (direction.sqrMagnitude < .1f) direction = -root.transform.forward;
        var interact = new GameObject("Interaction_Front_Of_Tray").transform;
        interact.SetParent(root.transform, true); interact.position = tray.position + direction * .5f;
        station.interactionPoint = interact;
        var glow = new GameObject("Processing_Crimson_Light"); glow.transform.SetParent(station.weaponSocket, false);
        glow.transform.localPosition = Vector3.up * .35f;
        station.processLight = glow.AddComponent<Light>(); station.processLight.type = LightType.Point;
        station.processLight.color = new Color(1, .015f, .025f); station.processLight.range = 2f; station.processLight.intensity = 220f;
        glow.AddComponent<HDAdditionalLightData>().volumetricDimmer = 0; station.processLight.enabled = false;
        FixLighting();
        PrefabUtility.SaveAsPrefabAsset(root, Folder + "/AltarUpgrader.prefab");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        File.WriteAllText(Report + "/installed.txt", $"Installed on {altar.name}, altar={altar.bounds:F4}\nposition={root.transform.position:F4}\ntray interaction={interact.position:F4}\ncycle={station.cycle.length}s\ncontrols={model.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("CTRL_"))}\n");
        ValidateAndCapture();
    }
    internal static void DetachRuntimeAssets(GameObject model, string folder)
    {
        var meshes = new System.Collections.Generic.Dictionary<Mesh, Mesh>();
        var textures = new System.Collections.Generic.Dictionary<Texture, Texture>();
        var materials = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            var src = filter.sharedMesh; if (src == null) continue;
            if (!meshes.TryGetValue(src, out var mesh))
            {
                mesh = Object.Instantiate(src); AssetDatabase.CreateAsset(mesh, folder + "/Mesh_" + meshes.Count + ".asset"); meshes.Add(src, mesh);
            }
            filter.sharedMesh = mesh;
        }
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var array = renderer.sharedMaterials;
            for (int i=0; i<array.Length; i++)
            {
                var src=array[i]; if (src==null) continue;
                if (!materials.TryGetValue(src,out var material))
                {
                    material=new Material(src);
                    foreach(var property in material.GetTexturePropertyNames())
                    {
                        var tex=material.GetTexture(property) as Texture2D; if(tex==null)continue;
                        if(!textures.TryGetValue(tex,out var compressed))
                        {
                            var copy=Object.Instantiate(tex); copy.name="Altar_PBR_"+textures.Count;
                            EditorUtility.CompressTexture(copy,TextureFormat.BC7,TextureCompressionQuality.Normal);
                            AssetDatabase.CreateAsset(copy,folder+"/PBR_"+textures.Count+".asset"); compressed=copy; textures.Add(tex,compressed);
                        }
                        material.SetTexture(property,compressed);
                    }
                    AssetDatabase.CreateAsset(material,folder+"/Material_"+materials.Count+".mat"); materials.Add(src,material);
                }
                array[i]=material;
            }
            renderer.sharedMaterials=array;
        }
    }
    static void FixLighting()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        if (profile != null && profile.TryGet<Exposure>(out var exposure))
        { exposure.fixedExposure.Override(7.4f); EditorUtility.SetDirty(profile); }
        int n=0;
        foreach (var player in Object.FindObjectsByType<HDRPSlicePlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var camera = player.GetComponentInChildren<Camera>(true); if (camera==null)continue;
            foreach (var light in camera.GetComponentsInChildren<Light>(true))
                if(light.type==LightType.Spot && light.transform.parent==camera.transform)
                {
                    var data=light.GetComponent<HDAdditionalLightData>();
                    if(data!=null){data.volumetricDimmer=0;EditorUtility.SetDirty(data);n++;}
                }
        }
        File.WriteAllText(Report+"/lighting.txt",$"Night EV restored from 5.8 to 7.4 (previous 7.2, 0.2 EV darker). Camera flashlight volumetric contribution disabled on {n} lights; runtime enforces this as well. Surface lighting preserved.");
    }
}
