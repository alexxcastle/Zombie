using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class HDRPSliceBuilder
{
    const string ScenePath = "Assets/Scenes/Elderboom_HDRP_PlayableSlice.unity";
    const string PipelinePath = "Assets/Settings/Elderboom_HDRP.asset";
    const string VolumeProfilePath = "Assets/Settings/Elderboom_Night_Profile.asset";
    const string ArmsPath = "Assets/GameAssets/Soph/Soph_AKMN_Plasma.gltf";
    const string HousePath = "Assets/GameAssets/Elderboom/ElderboomHouseSample.glb";
    const string ZombiePath = "Assets/GameAssets/Zombie/StaticZombie.fbx";
    const string LampPath = "Assets/GameAssets/StreetLamp/StreetLamp.gltf";

    [MenuItem("Elderboom HDRP/Import Tools/Build Fresh Playable Slice")]
    static void BuildFromMenu()
    {
        if (ElderboomEditorGuard.Confirm("Build fresh playable slice", "Regenerates the original HDRP slice scene from code. Manual changes to it are lost.")) Build();
    }
    public static void Build()
    {
        EnsureFolders();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in new[] { ArmsPath, HousePath, ZombiePath, LampPath })
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Slice source asset missing", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        ConfigureHDRP();
        ConfigureTextureImporters();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildNightVolume();
        BuildLighting();
        BuildRoadAndGround();

        var environmentRoot = new GameObject("Elderboom_Slice_Environment");
        var house = InstantiateAsset(HousePath, environmentRoot.transform, "Elderboom_Cottage_Facade");
        PositionAssetByBounds(house, new Vector3(-8.1f, 0f, 12.5f), null);
        ApplyHouseMaterialFallbacks(house);
        AddHouseCollider(house);

        var lamp = InstantiateAsset(LampPath, environmentRoot.transform, "Elderboom_Streetlamp");
        PositionAssetByBounds(lamp, new Vector3(3.35f, 0f, 6.2f), 4.6f);
        BuildLampLighting(lamp.transform);

        var player = BuildPlayer();
        var zombie = InstantiateAsset(ZombiePath, environmentRoot.transform, "Zombie_Road_Approach");
        zombie.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        PositionAssetByBounds(zombie, new Vector3(0.15f, 0f, 16f), 1.82f);
        ApplyZombieMaterials(zombie);
        var targetCollider = zombie.AddComponent<CapsuleCollider>();
        Bounds zombieBounds = WorldBounds(zombie);
        targetCollider.direction = 1;
        targetCollider.center = (zombieBounds.center - zombie.transform.position) / zombie.transform.lossyScale.y;
        targetCollider.height = Mathf.Max(1f, zombieBounds.size.y / zombie.transform.lossyScale.y);
        targetCollider.radius = Mathf.Clamp(Mathf.Min(zombieBounds.size.x, zombieBounds.size.z) * 0.27f / zombie.transform.lossyScale.x, 0.2f, 0.48f);
        foreach (var animator in zombie.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
        var zombieAnimation = ConfigureZombieAnimation(zombie);
        var zombieLogic = zombie.AddComponent<HDRPSliceZombie>();
        zombieLogic.Initialize(player.transform, player.GetComponent<HDRPSlicePlayer>());
        if (zombieAnimation != null) Debug.Log("HDRP slice zombie animation: " + zombieAnimation);

        BuildBoundaries();
        ConfigureCameraAndPlayer(player);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("Built clean HDRP playable slice with one Soph/AKMN/holstered plasma GLB, one Elderboom house, one road zombie, one lit lamp and a short textured gravel road.");
    }

    static void EnsureFolders()
    {
        foreach (string folder in new[] { "Assets/Settings", "Assets/Materials", "Assets/Animations", "Assets/Scenes" })
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
    }

    static void ConfigureHDRP()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelinePath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            asset.name = "Elderboom_HDRP";
            AssetDatabase.CreateAsset(asset, PipelinePath);
            typeof(HDRenderPipelineAsset).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(asset, null);
        }

        GraphicsSettings.defaultRenderPipeline = asset;
        EditorUtility.SetDirty(asset);

        // Unity keeps HDRP's project-wide shader, default volume and frame settings in
        // a separate global settings asset. The HDRP wizard performs this same ensure step.
        var settingsType = typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings");
        var ensure = settingsType?.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic);
        if (ensure != null) ensure.Invoke(null, new object[] { true });
        AssetDatabase.SaveAssets();
    }

    static void ConfigureTextureImporters()
    {
        foreach (string path in new[]
        {
            "Assets/GameAssets/Ground/Ground051_NormalGL_2K.jpg",
            "Assets/GameAssets/Roads/normal_gl_2k.png"
        })
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }
    }

    static void BuildNightVolume()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile != null) AssetDatabase.DeleteAsset(VolumeProfilePath);
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, VolumeProfilePath);

        var environment = profile.Add<VisualEnvironment>(true);
        environment.active = true;
        environment.skyType.value = (int)SkyType.HDRI;
        environment.cloudType.value = 0;
        environment.skyAmbientMode.value = SkyAmbientMode.Dynamic;

        var sky = profile.Add<HDRISky>(true);
        sky.active = true;
        sky.hdriSky.value = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/AllSkyFree/Night MoonBurst/NightMoonBurst_OverVillage.png");
        sky.skyIntensityMode.value = SkyIntensityMode.Lux;
        sky.desiredLuxValue.value = 1400f;
        sky.multiplier.value = 1f;
        sky.rotation.value = 0f;
        sky.distortionMode.value = HDRISky.DistortionMode.None;

        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.active = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        var exposure = profile.Add<Exposure>(true);
        exposure.active = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.value = 7.2f;

        var bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.intensity.value = 0.14f;
        bloom.threshold.value = 1.25f;
        bloom.scatter.value = 0.55f;

        var fog = profile.Add<UnityEngine.Rendering.HighDefinition.Fog>(true);
        fog.active = true;
        fog.enabled.value = true;
        fog.colorMode.value = FogColorMode.ConstantColor;
        fog.color.value = new Color(0.145f, 0.175f, 0.22f);
        fog.meanFreePath.value = 26f;
        fog.baseHeight.value = -1f;
        fog.maximumHeight.value = 18f;
        fog.enableVolumetricFog.value = false;
        fog.maxFogDistance.value = 320f;

        var volumeObject = new GameObject("HDRP_Night_Environment_Volume");
        var volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = profile;
    }

    static void BuildLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.075f, 0.12f);

        var moonObject = new GameObject("Moonlight_Directional");
        moonObject.transform.rotation = new Quaternion(0.25901439f, -0.32877086f, 0.09427353f, 0.90329052f);
        var moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.16641437f, 0.2840521f, 0.41911763f);
        moon.useColorTemperature = false;
        moon.intensity = 2200f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.72f;
    }

    static void BuildRoadAndGround()
    {
        var groundMaterial = NewHDRPMaterial("Ground_Grass_Dirt", new Color(0.56f, 0.61f, 0.43f), 0.06f);
        groundMaterial.SetTexture("_BaseColorMap", LoadTexture("Assets/GameAssets/Ground/Ground051_Color_2K.jpg"));
        groundMaterial.SetTextureScale("_BaseColorMap", new Vector2(28f, 18f));
        groundMaterial.SetTexture("_NormalMap", LoadTexture("Assets/GameAssets/Ground/Ground051_NormalGL_2K.jpg"));
        groundMaterial.SetFloat("_NormalScale", 0.52f);
        groundMaterial.EnableKeyword("_NORMALMAP");

        var roadMaterial = NewHDRPMaterial("Elderboom_River_Pebble_Road", new Color(0.68f, 0.68f, 0.68f), 0.14f);
        roadMaterial.SetTexture("_BaseColorMap", LoadTexture("Assets/GameAssets/Roads/albedo_4k.jpg"));
        roadMaterial.SetTextureScale("_BaseColorMap", new Vector2(1.25f, 6f));
        roadMaterial.SetTexture("_NormalMap", LoadTexture("Assets/GameAssets/Roads/normal_gl_2k.png"));
        roadMaterial.SetTextureScale("_NormalMap", new Vector2(1.25f, 6f));
        roadMaterial.SetFloat("_NormalScale", 0.8f);
        roadMaterial.EnableKeyword("_NORMALMAP");

        var shoulderMaterial = NewHDRPMaterial("Roadside_Dark_Stone", new Color(0.2f, 0.22f, 0.23f), 0.1f);

        CreatePlane("Elderboom_Ground", new Vector3(0f, -0.055f, 14f), new Vector3(11f, 1f, 6.8f), groundMaterial, true);
        CreatePlane("Ganges_Pebble_Road", new Vector3(0f, -0.015f, 14f), new Vector3(0.58f, 1f, 2.65f), roadMaterial, false);

        // Low rough verges help read the road edges while keeping the slice simple.
        for (int side = -1; side <= 1; side += 2)
        {
            var verge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            verge.name = side < 0 ? "Road_Edge_Left" : "Road_Edge_Right";
            verge.transform.position = new Vector3(side * 3.1f, 0.08f, 14f);
            verge.transform.localScale = new Vector3(0.24f, 0.16f, 27f);
            verge.GetComponent<Renderer>().sharedMaterial = shoulderMaterial;
        }
    }

    static Material NewHDRPMaterial(string name, Color tint, float smoothness)
    {
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null) throw new InvalidOperationException("HDRP/Lit shader is unavailable; verify the HDRP package is installed before building the scene.");
        string path = "Assets/Materials/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Texture2D LoadTexture(string path)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null) throw new FileNotFoundException("HDRP slice texture missing", path);
        return texture;
    }

    static void CreatePlane(string name, Vector3 position, Vector3 scale, Material material, bool collider)
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = name;
        plane.transform.position = position;
        plane.transform.localScale = scale;
        plane.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider)
        {
            var c = plane.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }
    }

    [MenuItem("Elderboom HDRP/Correct First Person Camera")]
    public static void CorrectFirstPersonCamera()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before saving the camera framing.");
        var model = GameObject.Find("Soph_Integrated_Arms_AKMN_Plasma");
        if (model == null) throw new InvalidOperationException("Open the Soph playable slice first.");
        Undo.RecordObjects(new UnityEngine.Object[] { model.transform, model.transform.parent }, "Correct Soph first person camera");
        AlignViewmodelEyes(model.transform, model.transform.parent);
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        EditorSceneManager.MarkSceneDirty(model.scene);
        EditorSceneManager.SaveScene(model.scene);
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void AlignViewmodelEyes(Transform model, Transform pivot)
    {
        // The viewmodel's authored eye position belongs at the camera origin.
        // Moving it 0.82 m forward exposes both shoulders like a third-person actor.
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one * 0.72f;
        var eyes = FindDeepChild(model, "Eyes");
        if (eyes == null) throw new InvalidOperationException("Soph's authored Eyes locator is missing.");
        Vector3 eyeInModel = model.InverseTransformPoint(eyes.position);
        model.localPosition = -Vector3.Scale(eyeInModel, model.localScale);
        Debug.Log($"Soph FPS framing: Eyes at camera-local {pivot.parent.InverseTransformPoint(eyes.position):F4}; artificial camera/model distance removed.");
    }

    static GameObject BuildPlayer()
    {
        var player = new GameObject("Soph_Player");
        player.transform.position = new Vector3(0f, 0f, -1.2f);
        var character = player.AddComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.32f;
        character.center = new Vector3(0f, 0.9f, 0f);
        character.slopeLimit = 48f;
        character.stepOffset = 0.3f;

        var cameraObject = new GameObject("PlayerCamera_Eyes");
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 72f;
        camera.nearClipPlane = 0.025f;
        camera.farClipPlane = 120f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;
        var additional = cameraObject.AddComponent<HDAdditionalCameraData>();
        additional.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

        // Viewmodels need a consistent soft fill independent of the world lighting.
        var fillObject = new GameObject("Viewmodel_Soft_Fill");
        fillObject.transform.SetParent(cameraObject.transform, false);
        fillObject.transform.localPosition = new Vector3(0f, 0.06f, 0.52f);
        var viewFill = fillObject.AddComponent<Light>();
        viewFill.type = LightType.Spot;
        viewFill.color = new Color(0.86f, 0.93f, 1f);
        viewFill.intensity = 55000f;
        viewFill.range = 38f;
        viewFill.spotAngle = 64f;
        viewFill.innerSpotAngle = 34f;
        viewFill.useColorTemperature = true;
        viewFill.colorTemperature = 7800f;
        viewFill.shadows = LightShadows.None;

        var viewmodelRoot = new GameObject("Soph_Viewmodel_Pivot");
        viewmodelRoot.transform.SetParent(cameraObject.transform, false);
        var model = InstantiateAsset(ArmsPath, viewmodelRoot.transform, "Soph_Integrated_Arms_AKMN_Plasma");
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * 0.72f;

        // Preserve the metal-sight ADS choice from the source project: hide only
        // the B13 rail instance in this game slice, leaving the integrated source GLB untouched.
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, "b13", StringComparison.OrdinalIgnoreCase))
                foreach (var renderer in transform.GetComponents<Renderer>()) renderer.enabled = false;

        var animationData = ConfigureSophAnimation(model);
        // Imported animations are evaluated at runtime, so a freshly opened scene
        // otherwise shows the GLB bind pose (arms apart). Sample the idle's first
        // authored frame once into this scene instance for a useful Edit Mode pose;
        // the Animator still owns all animation playback in Play Mode.
        var idlePreview = AssetDatabase.LoadAllAssetsAtPath(ArmsPath).OfType<AnimationClip>()
            .FirstOrDefault(c => c != null && string.Equals(c.name, "Idle_Breathing", StringComparison.OrdinalIgnoreCase));
        if (idlePreview != null)
        {
            idlePreview.SampleAnimation(model, 0f);
            Debug.Log("Sampled Soph idle pose in Edit Mode at 0.000s; runtime animation controller remains active.");
        }
        AlignViewmodelEyes(model.transform, viewmodelRoot.transform);
        Transform rearSight = FindDeepChild(model.transform, "Soph_ADS_Rear");
        Transform frontSight = FindDeepChild(model.transform, "Soph_ADS_Front");
        Transform muzzle = FindDeepChild(model.transform, "Soph_Rifle_Muzzle");
        Light flash = null;
        if (muzzle != null)
        {
            var flashObject = new GameObject("AKMN_Muzzle_Flash_Light");
            flashObject.transform.SetParent(muzzle, false);
            flashObject.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.54f, 0.19f);
            flash.intensity = 0f;
            flash.range = 2.8f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        var controller = player.AddComponent<HDRPSlicePlayer>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("viewCamera").objectReferenceValue = camera;
        serialized.FindProperty("weaponRoot").objectReferenceValue = viewmodelRoot.transform;
        serialized.FindProperty("adsRearSight").objectReferenceValue = rearSight;
        serialized.FindProperty("adsFrontSight").objectReferenceValue = frontSight;
        serialized.FindProperty("rifleMuzzle").objectReferenceValue = muzzle;
        serialized.FindProperty("weaponAnimator").objectReferenceValue = animationData.animator;
        serialized.FindProperty("weaponLegacyAnimation").objectReferenceValue = animationData.legacy;
        serialized.FindProperty("muzzleFlash").objectReferenceValue = flash;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var originalBounds = WorldBounds(model);
        Debug.Log($"Soph GLB imported as one rigged actor: renderer bounds {originalBounds.size:F2} units; ADS rear sight {rearSight != null}; muzzle {muzzle != null}; clips [{string.Join(", ", animationData.clipNames)}].");
        return player;
    }

    struct AnimationSetup
    {
        public Animator animator;
        public Animation legacy;
        public string[] clipNames;
    }

    static AnimationSetup ConfigureSophAnimation(GameObject root)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ArmsPath).OfType<AnimationClip>()
            .Where(c => c != null && !c.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase)).ToArray();
        var result = new AnimationSetup { clipNames = clips.Select(c => c.name + (c.legacy ? " [legacy]" : " [mecanim]")).ToArray() };
        if (clips.Length == 0) return result;
        if (clips.Any(c => !c.legacy))
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            // Keep the source animation setup intact. These clips are meant to
            // animate the imported rig in Play Mode; only the camera pivot above
            // controls their on-screen composition.
            const string avatarPath = "Assets/Animations/Soph_GenericAvatar.asset";
            Avatar avatar = null;
            try { avatar = AvatarBuilder.BuildGenericAvatar(root, "CustomFPS_Arms_RIG"); }
            catch (Exception exception) { Debug.LogError("Soph Avatar build failed: " + exception); }
            if (avatar != null && avatar.isValid)
            {
                var oldAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                if (oldAvatar != null) AssetDatabase.DeleteAsset(avatarPath);
                avatar.name = "Soph_GenericAvatar";
                AssetDatabase.CreateAsset(avatar, avatarPath);
                animator.avatar = avatar;
                Debug.Log("Soph viewmodel Generic Avatar valid: " + avatar.isValid);
            }
            else
            {
                Debug.LogError("Could not build a valid Generic Avatar for the Soph viewmodel.");
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
            }
            string controllerPath = "Assets/Animations/Soph_Integrated_Arms.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null) AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            AnimationClip idle = FindClip(clips, "Idle_Breathing") ?? clips[0];
            var idleState = sm.AddState("Idle_Breathing");
            idleState.motion = idle;
            sm.defaultState = idleState;
            AddActionState(controller, sm, idleState, FindClip(clips, "Reload_AK"), "Reload_AK");
            AddActionState(controller, sm, idleState, FindClip(clips, "Special_Plasma"), "Special_Plasma");
            animator.runtimeAnimatorController = controller;
            result.animator = animator;
        }
        else
        {
            var animation = root.GetComponent<Animation>() ?? root.AddComponent<Animation>();
            foreach (var clip in clips)
            {
                if (!animation.GetClip(clip.name)) animation.AddClip(clip, clip.name);
                animation[clip.name].wrapMode = string.Equals(clip.name, "Idle_Breathing", StringComparison.OrdinalIgnoreCase) ? WrapMode.Loop : WrapMode.Once;
            }
            var idle = FindClip(clips, "Idle_Breathing") ?? clips[0];
            animation.clip = idle;
            animation.playAutomatically = true;
            animation.wrapMode = WrapMode.Loop;
            animation.Play(idle.name);
            result.legacy = animation;
        }
        return result;
    }

    static void AddActionState(AnimatorController controller, AnimatorStateMachine sm, AnimatorState idle, AnimationClip clip, string trigger)
    {
        if (clip == null) return;
        var state = sm.AddState(trigger);
        state.motion = clip;
        controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        var toAction = idle.AddTransition(state);
        toAction.hasExitTime = false;
        toAction.duration = 0.08f;
        toAction.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        var back = state.AddTransition(idle);
        back.hasExitTime = true;
        back.exitTime = 0.96f;
        back.duration = 0.1f;
    }

    static AnimationClip FindClip(AnimationClip[] clips, string name)
        => clips.FirstOrDefault(c => string.Equals(c.name.Replace(" ", "_"), name.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase)
                                  || c.name.IndexOf(name.Replace("_", " "), StringComparison.OrdinalIgnoreCase) >= 0);

    static string ConfigureZombieAnimation(GameObject zombie)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ZombiePath).OfType<AnimationClip>().Where(c => c != null).ToArray();
        if (clips.Length == 0) return null;
        var animator = zombie.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = zombie.AddComponent<Animator>();
        animator.applyRootMotion = false;
        string path = "Assets/Animations/Zombie_Approach.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var state = controller.layers[0].stateMachine.defaultState;
        if (state == null) state = controller.layers[0].stateMachine.AddState("Walk");
        var walk = clips.FirstOrDefault(c => c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0) ?? clips[0];
        state.name = "Walk_Approach";
        state.motion = walk;
        controller.layers[0].stateMachine.defaultState = state;
        animator.runtimeAnimatorController = controller;
        return string.Join(", ", clips.Select(c => c.name));
    }

    static void ApplyZombieMaterials(GameObject zombie)
    {
        var textures = new[]
        {
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Zombie/StaticZombie_0.png"),
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Zombie/StaticZombie_1.png")
        };
        if (textures.Any(t => t == null))
        {
            Debug.LogError("Zombie albedo texture atlas is missing.");
            return;
        }

        var shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogError("HDRP/Lit shader was not found; zombie texture could not be assigned.");
            return;
        }

        var zombieMaterials = new Material[2];
        for (int i = 0; i < zombieMaterials.Length; i++)
        {
            string path = $"Assets/Materials/Zombie_StaticZombie_{i}_HDRP.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseColorMap", textures[i]);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.22f);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            zombieMaterials[i] = material;
        }

        int rendererCount = 0;
        foreach (var renderer in zombie.GetComponentsInChildren<Renderer>(true))
        {
            rendererCount++;
            var slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0) slots = new Material[1];
            for (int i = 0; i < slots.Length; i++)
            {
                string slotName = slots[i] != null ? slots[i].name : string.Empty;
                int atlas = slotName.EndsWith("_1", StringComparison.OrdinalIgnoreCase)
                         || slotName.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ? 1 : i % 2;
                slots[i] = zombieMaterials[atlas];
            }
            renderer.sharedMaterials = slots;
            Debug.Log($"Zombie textured renderer '{renderer.name}' with {slots.Length} HDRP atlas material slot(s).");
        }
        if (rendererCount == 0) Debug.LogError("Zombie asset has no renderers to texture.");
    }

    static void ConfigureCameraAndPlayer(GameObject player)
    {
        Physics.gravity = new Vector3(0f, -9.81f, 0f);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        var camera = player.GetComponentInChildren<Camera>();
        if (camera != null) camera.transform.localRotation = Quaternion.identity;
    }

    static void BuildLampLighting(Transform lamp)
    {
        var spotObject = new GameObject("Lamp_Warm_Spotlight");
        spotObject.transform.SetParent(lamp, false);
        spotObject.transform.localPosition = new Vector3(0f, 2.12f, 0f);
        spotObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var spot = spotObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.72f, 0.46f);
        spot.intensity = 2600f;
        spot.range = 17f;
        spot.spotAngle = 62f;
        spot.innerSpotAngle = 29f;
        spot.shadows = LightShadows.Soft;
        spot.shadowStrength = 0.78f;

        var fillObject = new GameObject("Lamp_Bulb_Fill");
        fillObject.transform.SetParent(lamp, false);
        fillObject.transform.localPosition = new Vector3(0f, 2.08f, 0f);
        var fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(1f, 0.53f, 0.25f);
        fill.intensity = 190f;
        fill.range = 5.5f;
        fill.shadows = LightShadows.None;
    }

    static void BuildBoundaries()
    {
        // The player can walk the short street but remains in the intended test composition.
        CreateBoundary("Slice_Boundary_Left", new Vector3(-11f, 1.7f, 12f), new Vector3(0.35f, 3.4f, 35f));
        CreateBoundary("Slice_Boundary_Right", new Vector3(11f, 1.7f, 12f), new Vector3(0.35f, 3.4f, 35f));
        CreateBoundary("Slice_Boundary_Far", new Vector3(0f, 1.7f, 31f), new Vector3(22f, 3.4f, 0.35f));
    }

    static void CreateBoundary(string name, Vector3 position, Vector3 size)
    {
        var wall = new GameObject(name);
        wall.transform.position = position;
        wall.AddComponent<BoxCollider>().size = size;
    }

    static void AddHouseCollider(GameObject house)
    {
        var bounds = WorldBounds(house);
        var box = house.AddComponent<BoxCollider>();
        box.center = (bounds.center - house.transform.position) / house.transform.lossyScale.x;
        box.size = new Vector3(bounds.size.x, Mathf.Min(bounds.size.y, 4.8f), bounds.size.z) / house.transform.lossyScale.x;
    }

    static GameObject InstantiateAsset(string path, Transform parent, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("glTFast/FBX import did not create a GameObject for " + path + ". Check Package Manager and the Console.");
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null) instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = name;
        StripMissingScripts(instance);
        if (parent != null) instance.transform.SetParent(parent, true);
        return instance;
    }

    static void StripMissingScripts(GameObject root)
    {
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
            if (missing <= 0) continue;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(item.gameObject);
            Debug.LogWarning($"Removed {missing} broken component reference(s) from imported asset object '{item.name}'.");
        }
    }

    static void ApplyHouseMaterialFallbacks(GameObject house)
    {
        // The supplied Elderboom GLB contains modular geometry but no image textures
        // or authored PBR factors, so its imported pieces all default to white.
        // Give the single façade a restrained, night-readable material palette.
        var wall = NewHDRPMaterial("Cottage_Worn_Plaster", new Color(0.36f, 0.30f, 0.23f), 0.18f);
        var roof = NewHDRPMaterial("Cottage_Charcoal_Roof", new Color(0.075f, 0.085f, 0.10f), 0.12f);
        var wood = NewHDRPMaterial("Cottage_Aged_Wood", new Color(0.16f, 0.085f, 0.045f), 0.24f);
        var stone = NewHDRPMaterial("Cottage_Dark_Stone", new Color(0.19f, 0.20f, 0.21f), 0.16f);
        var glass = NewHDRPMaterial("Cottage_Dark_Glass", new Color(0.035f, 0.055f, 0.075f), 0.42f);
        glass.SetFloat("_Metallic", 0.18f);

        foreach (var renderer in house.GetComponentsInChildren<Renderer>(true))
        {
            string part = renderer.gameObject.name.ToLowerInvariant();
            Material material =
                part.Contains("gable") || part.Contains("roof") || part.Contains("shingle") ? roof :
                part.Contains("door") || part.Contains("wood") || part.Contains("beam") || part.Contains("bollard") ? wood :
                part.Contains("window") || part.Contains("glass") ? glass :
                part.Contains("stone") || part.Contains("wall_400") ? stone : wall;
            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Applied HDRP fallback palette to {house.GetComponentsInChildren<Renderer>(true).Length} cottage renderers.");
    }

    static void PositionAssetByBounds(GameObject asset, Vector3 targetCenter, float? targetHeight)
    {
        asset.transform.position = Vector3.zero;
        asset.transform.rotation = Quaternion.identity;
        asset.transform.localScale = Vector3.one;
        Bounds bounds = WorldBounds(asset);
        if (targetHeight.HasValue && bounds.size.y > 0.001f)
        {
            float scale = targetHeight.Value / bounds.size.y;
            asset.transform.localScale = Vector3.one * scale;
            bounds = WorldBounds(asset);
        }
        asset.transform.position += new Vector3(targetCenter.x - bounds.center.x, -bounds.min.y, targetCenter.z - bounds.center.z);
    }

    static Bounds WorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r != null && r.enabled).ToArray();
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Transform FindDeepChild(Transform parent, string requested)
    {
        foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, requested, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }
}
