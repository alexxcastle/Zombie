using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class MuseumSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Museum_First_Map_HDRP.unity";
    const string SourceScene = "Assets/Scenes/Elderboom_HDRP_PlayableSlice.unity";
    const string AssetRoot = "Assets/GameAssets/Museum";
    const string VolumePath = "Assets/Settings/Museum_Interior_Profile.asset";
    const string ReportPath = "Logs/museum_import_result.json";

    [Serializable] class Layout { public Marker[] markers; public Room[] rooms; }
    [Serializable] class Marker { public string name; public float[] position; public string role; }
    [Serializable] class Room { public string name; public string render_object; public int source_objects; public int faces; }
    [Serializable] class PortalFile { public Portal[] portals; }
    [Serializable] class Portal { public string name; public float x, y, width, height, depth; }
    [Serializable] class DoorCheck { public string name; public bool clear; public string obstruction; public bool floor; }
    [Serializable] class ImportReport
    {
        public string status, scene, sourceScene, message;
        public int renderers, meshColliders, materials, lights, missingShaders, nullMaterials;
        public bool sourceSceneUnchanged, playerPresent, spawnHasFloor;
        public DoorCheck[] doors;
        public string[] animationClips;
    }

    static Vector3 FromBlender(float[] p) => new Vector3(-p[0], p[2], -p[1]);
    static Vector3 FromBlender(float x, float y, float z) => new Vector3(-x, z, -y);

    [MenuItem("Museum/Import and Build HDRP Museum Scene")]
    public static void Build()
    {
        Directory.CreateDirectory("Logs");
        byte[] originalScene = File.ReadAllBytes(SourceScene);
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string glb = AssetRoot + "/Museum_Map.gltf";
            if (!File.Exists(glb)) throw new FileNotFoundException("Museum GLB not exported", glb);
            AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (prefab == null) throw new InvalidOperationException("glTFast did not create the museum prefab.");
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(AssetRoot + "/Museum_Layout.json"));

            // Clone only the already corrected FPS actor. Keep the street test scene byte-for-byte intact.
            Scene street = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            GameObject actor = street.GetRootGameObjects().FirstOrDefault(x => x.name == "Soph_Player");
            if (actor == null) throw new InvalidOperationException("Soph_Player is absent from the existing HDRP test.");
            Scene museum = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var player = UnityEngine.Object.Instantiate(actor);
            player.name = "Soph_Player";
            SceneManager.MoveGameObjectToScene(player, museum);
            SceneManager.SetActiveScene(museum);
            EditorSceneManager.CloseScene(street, true);

            GameObject environment = PrefabUtility.InstantiatePrefab(prefab, museum) as GameObject;
            if (environment == null) throw new InvalidOperationException("Could not instantiate museum prefab.");
            environment.name = "Museum_Architecture";
            int colliderCount = 0;
            foreach (var node in environment.GetComponentsInChildren<Transform>(true))
            {
                if (node.name.StartsWith("COLLIDER_", StringComparison.Ordinal))
                {
                    foreach (var filter in node.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || filter.GetComponent<MeshCollider>() != null) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false;
                        collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning
                            | MeshColliderCookingOptions.WeldColocatedVertices
                            | MeshColliderCookingOptions.UseFastMidphase
                            | MeshColliderCookingOptions.CookForFasterSimulation;
                        colliderCount++;
                    }
                    foreach (var renderer in node.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                }
                node.gameObject.isStatic = true;
            }
            var spawn = layout.markers.FirstOrDefault(x => x.role == "player_spawn");
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = spawn != null ? FromBlender(spawn.position) : new Vector3(0f, 0f, 33f);
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            cc.enabled = true;
            Camera camera = player.GetComponentInChildren<Camera>();
            camera.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            camera.transform.localRotation = Quaternion.identity;
            camera.farClipPlane = 240f;
            camera.nearClipPlane = 0.025f;
            var fill = player.GetComponentsInChildren<Light>().FirstOrDefault(x => x.name == "Viewmodel_Soft_Fill");
            if (fill != null) { fill.intensity = 60f; fill.range = 2.2f; fill.renderingLayerMask = 2; }
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true)) renderer.renderingLayerMask |= 2;

            BuildInteriorVolume();
            BuildLighting();
            var markers = new GameObject("Museum_Gameplay_Markers");
            foreach (var marker in layout.markers)
            {
                var point = new GameObject(marker.name);
                point.transform.SetParent(markers.transform, false);
                point.transform.position = FromBlender(marker.position);
            }
            // Reference cameras make it easy to inspect the museum without moving Soph's validated viewmodel.
            var overviewObject = new GameObject("Museum_Overview_Camera");
            var overview = overviewObject.AddComponent<Camera>();
            overviewObject.transform.position = new Vector3(-104f, 132f, 155f);
            overviewObject.transform.LookAt(Vector3.zero);
            overview.fieldOfView = 50f; overview.farClipPlane = 400f; overview.enabled = false;
            overviewObject.AddComponent<HDAdditionalCameraData>();

            Physics.SyncTransforms();
            cc.enabled = false;
            bool previousBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            bool floor = Physics.Raycast(player.transform.position + Vector3.up * 0.3f, Vector3.down, out _, 0.8f);
            PortalFile portals = JsonUtility.FromJson<PortalFile>(File.ReadAllText(AssetRoot + "/Museum_Portal_Checks.json"));
            var checks = new List<DoorCheck>();
            foreach (var door in portals.portals.GroupBy(p => p.name.Replace(" Recheck", "")).Select(g => g.Last()))
            {
                Vector3 a = FromBlender(door.x, door.y - door.depth / 2f - .08f, 1.0f);
                Vector3 b = FromBlender(door.x, door.y + door.depth / 2f + .08f, 1.0f);
                bool blocked = Physics.SphereCast(a, .2f, (b-a).normalized, out RaycastHit obstruction, Vector3.Distance(a,b), ~0, QueryTriggerInteraction.Ignore);
                bool hasFloor = Physics.Raycast(FromBlender(door.x,door.y,.3f), Vector3.down, out _, .8f);
                checks.Add(new DoorCheck { name = door.name, clear = !blocked, obstruction = blocked ? obstruction.collider.name : "", floor = hasFloor });
            }
            Physics.queriesHitBackfaces = previousBackfaces;
            cc.enabled = true;
            var materials = environment.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var report = new ImportReport
            {
                status = "imported",
                scene = ScenePath,
                sourceScene = SourceScene,
                sourceSceneUnchanged = originalScene.SequenceEqual(File.ReadAllBytes(SourceScene)),
                playerPresent = player.GetComponent<HDRPSlicePlayer>() != null,
                spawnHasFloor = floor,
                renderers = environment.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled),
                meshColliders = colliderCount,
                materials = materials.Length,
                lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length,
                missingShaders = materials.Count(m => m != null && (m.shader == null || m.shader.name.Contains("InternalErrorShader"))),
                nullMaterials = materials.Count(m => m == null),
                doors = checks.ToArray(),
                animationClips = AssetDatabase.LoadAllAssetsAtPath("Assets/GameAssets/Soph/Soph_AKMN_Plasma.glb").OfType<AnimationClip>().Select(c => c.name).ToArray(),
                message = "Museum imported into a separate HDRP scene with the existing Soph FPS actor, physical collision geometry and warm interior lighting."
            };
            if (!report.sourceSceneUnchanged || !report.playerPresent || !floor || report.missingShaders > 0 || report.nullMaterials > 0)
                throw new InvalidOperationException("Museum import validation failed: " + JsonUtility.ToJson(report));
            EditorSceneManager.SaveScene(museum, ScenePath);
            AssetDatabase.SaveAssets();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report,true));
            Debug.Log("MUSEUM_IMPORT_COMPLETE " + JsonUtility.ToJson(report));
            if (checks.Any(c => !c.clear || !c.floor)) Debug.LogWarning("Some museum thresholds need additional collision review; see " + ReportPath);
            FrameSceneView();
        }
        catch (Exception exception)
        {
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new ImportReport { status = "failed", scene = ScenePath, message = exception.ToString() },true));
            Debug.LogException(exception);
            throw;
        }
    }

    static void BuildInteriorVolume()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,VolumePath); }
        else foreach (var component in profile.components.ToArray()) { profile.components.Remove(component); UnityEngine.Object.DestroyImmediate(component,true); }
        var visual = profile.Add<VisualEnvironment>(true);
        visual.skyType.value = (int)SkyType.Gradient; visual.skyAmbientMode.value = SkyAmbientMode.Dynamic;
        var sky = profile.Add<GradientSky>(true);
        sky.top.value = new Color(.22f,.28f,.34f); sky.middle.value = new Color(.38f,.40f,.42f); sky.bottom.value = new Color(.13f,.115f,.09f);
        var exposure = profile.Add<Exposure>(true); exposure.mode.value = ExposureMode.Fixed; exposure.fixedExposure.value = 4.6f;
        var tone = profile.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.ACES;
        var bloom = profile.Add<Bloom>(true); bloom.intensity.value = .08f; bloom.threshold.value = 1.4f;
        var color = profile.Add<ColorAdjustments>(true); color.saturation.value = -4f; color.contrast.value = 4f;
        var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true); ao.intensity.value = .6f; ao.radius.value = .8f;
        EditorUtility.SetDirty(profile);
        var volume = new GameObject("Museum_HDRP_Interior_Volume").AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 20; volume.sharedProfile = profile;
    }

    static Light Point(string name,Vector3 position,float candela,float range,Color color,Transform parent)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent,false); light.transform.position = position;
        light.type = LightType.Point; light.intensity = candela; light.lightUnit = LightUnit.Candela;
        light.range = range; light.color = color; light.shadows = LightShadows.None;
        return light;
    }

    static void BuildLighting()
    {
        var root = new GameObject("Museum_Interior_Lighting").transform;
        Color warm = new Color(1f,.83f,.64f);
        // Use a bounded number of local lights, rather than all decorative source lights.
        for (int side = -1; side <= 1; side += 2)
            for (int x = -28; x <= 28; x += 8) Point("Concourse_Practical",FromBlender(x,side*23,3.65f),600f,8.5f,warm,root);
        foreach (float y in new[] { -63f,63f })
            for (int x = -24; x <= 24; x += 12) Point("Return_Gallery_Practical",FromBlender(x,y,3.7f),650f,10f,warm,root);
        for (int side = -1; side <= 1; side += 2)
            foreach (int y in new[] { -14,0,14 }) Point("Ambulatory_Practical",FromBlender(side*33,y,3.5f),700f,10f,warm,root);
        foreach (float x in new[] { -22f,0f,22f })
            foreach (int y in new[] { 31,41,51 }) Point("North_Wing_Soft_Fill",FromBlender(x,y,4.5f),1050f,13f,warm,root);
        foreach (float x in new[] { -22f,22f })
            foreach (int y in new[] { -31,-41,-51 }) Point("South_Wing_Soft_Fill",FromBlender(x,y,3.4f),650f,x<0?7f:11f,warm,root);
        foreach (float y in new[] { -10f,0f,10f }) Point("Atrium_Soft_Fill",FromBlender(0,y,8.5f),2700f,20f,new Color(1f,.89f,.73f),root);
        Point("Reception_Practical",FromBlender(0,-32,3.7f),700f,10f,warm,root);
        Point("Atrium_North_Link",FromBlender(0,17,3.3f),550f,8f,warm,root);
        Point("Atrium_South_Link",FromBlender(0,-17,3.5f),550f,8f,warm,root);
        var daylight = new GameObject("Museum_Skylight_Daylight").AddComponent<Light>();
        daylight.transform.SetParent(root,false); daylight.type = LightType.Directional;
        daylight.transform.rotation = Quaternion.Euler(58f,-24f,0f); daylight.lightUnit = LightUnit.Lux;
        daylight.intensity = 1600f; daylight.color = new Color(.91f,.95f,1f); daylight.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = AmbientMode.Skybox;
    }

    [MenuItem("Museum/Open First Museum Map")]
    public static void Open()
    {
        EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        FrameSceneView();
    }

    static void FrameSceneView()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        // Open directly inside the atrium in Scene View; Game View starts at reception.
        view.pivot = FromBlender(0,0,2f);
        view.rotation = Quaternion.Euler(0f,180f,0f);
        view.size = 13f;
        view.orthographic = false;
        view.Repaint();
    }
}
