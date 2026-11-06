using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>Builds the playable Unity HDRP port of the current Elderboom loop.</summary>
public static class ElderboomPortBuilder
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string SourceSlice = "Assets/Scenes/Elderboom_HDRP_PlayableSlice.unity";
    const string VillagePath = "Assets/GameAssets/Ported/ElderboomVillage/ElderBoomVillage_Full.glb";
    const string MaleZombiePath = "Assets/GameAssets/Ported/ZombieMale/Meshy_AI_male_zombie_rigged_biped/Meshy_AI_male_zombie_rigged_biped_Meshy_Merged_Animations.fbx";
    const string LampPath = "Assets/GameAssets/StreetLamp/StreetLamp.gltf";
    const string EliPath = "Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore.gltf";
    const string TacticalPath = "Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack.gltf";
    const string ZombiePrefabPath = "Assets/Resources/Elderboom/Zombie.prefab";
    const string VolumeProfilePath = "Assets/Settings/Elderboom_Night_Profile.asset";

    [MenuItem("Elderboom HDRP/Import Tools/Rebuild Zombie Prefabs")]
    static void RebuildZombiesFromMenu()
    {
        if (ElderboomEditorGuard.Confirm("Rebuild zombie prefabs", "Regenerates the zombie prefabs from the source rigs. Manual edits to those prefabs are lost.")) RebuildZombies();
    }
    public static void RebuildZombies()
    {
        ElderboomZombieRig.Rebuild();
    }

    [MenuItem("Elderboom HDRP/Import Tools/Rebuild Full Unity Port Scene")]
    static void BuildFromMenu()
    {
        if (ElderboomEditorGuard.Confirm("Rebuild the Elderboom scene", "Regenerates Elderboom_Unity_Port.unity from the Godot source. Everything placed or changed by hand in that scene is lost.")) Build();
    }
    public static void Build()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Elderboom");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        RequireAsset(VillagePath);
        RequireAsset(MaleZombiePath);
        RequireAsset(LampPath);
        RequireAsset(EliPath);
        RequireAsset(TacticalPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildWorldLighting();
        BuildGroundAndRoads();
        BuildVillage();
        BuildVillageBoundaries();
        BuildSpawnRing();
        BuildGates();
        var player = CloneProvenPlayer();
        ConfigureOperatorViewmodels(player);
        ElderboomZombieRig.Rebuild();
        ElderboomArsenalBuilder.Build();
        ElderboomArsenalBuilder.Stations();
        ElderboomWorldPort.BakeNavigation();
        var game = new GameObject("Elderboom_Game_Director");
        game.AddComponent<ElderboomGame>();
        game.AddComponent<ElderboomHud>();
        player.name = "FPS_Player";

        EditorSceneManager.SaveScene(scene, ScenePath);
        BuildMenu();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/ZombieTest_Menu.unity", true),new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene("Assets/Scenes/ZombieTest_Menu.unity", OpenSceneMode.Single);
        Debug.Log("Elderboom Unity port built: menu, operator selection, HDRP village, waves, safe spawn scoring, permanent plasma blindness, acoustic/shockwave abilities and reusable zombie combat.");
    }

    static void BuildMenu()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var camera=new GameObject("Menu_Camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.02f,.028f);camera.gameObject.AddComponent<AudioListener>();
        new GameObject("ZombieTest_Menu").AddComponent<ElderboomMenu>();
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/ZombieTest_Menu.unity");
    }

    static void RequireAsset(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new FileNotFoundException("Cannot import source model", path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void BuildWorldLighting()
    {
        var volume = new GameObject("Elderboom_HDRP_Night_Volume").AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 20f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/AllSkyFree/Deep Dusk/Deep Dusk Equirect.mat");
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.13235295f, 0.09342561f, 0.11613321f);
        RenderSettings.fogStartDistance = 1f;
        RenderSettings.fogEndDistance = 1000f;
        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.type = LightType.Directional; moon.color = new Color(0.16641437f, 0.2840521f, 0.41911763f); moon.intensity = 2200f;
        moon.useColorTemperature = false;
        moon.shadows = LightShadows.Soft; moon.shadowStrength = .78f;
        moon.transform.rotation = new Quaternion(0.25901439f, -0.32877086f, 0.09427353f, 0.90329052f);
    }

    static Material Material(string name, Color baseColor, float smoothness)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("HDRP/Lit")) { name = name };
        material.SetColor("_BaseColor", baseColor); material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void BuildGroundAndRoads()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Elderboom_Playable_Ground";
        ground.transform.localScale = Vector3.one * 16f;
        ground.transform.position = new Vector3(5.7f,1f,-.7f);
        var groundMaterial = Material("Elderboom_Port_Ground", new Color(.85f, .8f, .72f), .04f);
        groundMaterial.SetColor("_BaseColor", new Color(.85f, .8f, .72f, 1f));
        groundMaterial.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ground/Ground051_Color_2K.jpg"));
        groundMaterial.SetTextureScale("_BaseColorMap",Vector2.one*104f);
        groundMaterial.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ground/Ground051_NormalGL_2K.jpg"));
        groundMaterial.SetTextureScale("_NormalMap",Vector2.one*104f);
        groundMaterial.SetFloat("_NormalScale", .5f);
        groundMaterial.SetFloat("_Smoothness", .04f);
        groundMaterial.SetFloat("_Metallic", 0f);
        groundMaterial.EnableKeyword("_NORMALMAP");
        groundMaterial.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        ElderboomWorldPort.BuildRoads();
    }

    static void CreateRoad(string name, Vector3 position, Vector3 scale, Material material)
    {
        var road = GameObject.CreatePrimitive(PrimitiveType.Plane);
        road.name = name; road.transform.position = position; road.transform.localScale = scale;
        road.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(road.GetComponent<Collider>());
    }

    static void BuildVillage()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePath);
        var root = PrefabUtility.InstantiatePrefab(asset) as GameObject ?? UnityEngine.Object.Instantiate(asset);
        root.name = "Elderboom_Village_Port";
        if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        ElderboomWorldPort.Apply(root);
        AddStreetLamp(new Vector3(-5.5f, 0f, 5f));
        AddStreetLamp(new Vector3(5.4f, 0f, 9f));
        AddStreetLamp(new Vector3(-6.2f, 0f, -12f));
        Debug.Log("Elderboom map repaired using the current Godot map metadata.");
    }

    static void AddStreetLamp(Vector3 position)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(LampPath);
        var lamp = PrefabUtility.InstantiatePrefab(asset) as GameObject ?? UnityEngine.Object.Instantiate(asset);
        lamp.name = "Elderboom_Streetlamp"; lamp.transform.position = position;
        var light = new GameObject("Warm_Lamp_Light").AddComponent<Light>();
        light.transform.position = position + Vector3.up * 3.1f;
        light.type = LightType.Point; light.color = new Color(1f, .56f, .24f); light.intensity = 520f; light.range = 14f; light.shadows = LightShadows.Soft;
    }

    static void BuildVillageBoundaries()
    {
        Boundary("Elderboom_North_Boundary", new Vector3(0, 3, 77), new Vector3(150, 6, 1));
        Boundary("Elderboom_South_Boundary", new Vector3(0, 3, -77), new Vector3(150, 6, 1));
        Boundary("Elderboom_West_Boundary", new Vector3(-77, 3, 0), new Vector3(1, 6, 150));
        Boundary("Elderboom_East_Boundary", new Vector3(77, 3, 0), new Vector3(1, 6, 150));
    }
    static void Boundary(string name, Vector3 position, Vector3 size)
    {
        var wall = new GameObject(name); wall.transform.position = position; wall.AddComponent<BoxCollider>().size = size;
    }

    static void BuildSpawnRing()
    {
        for (int i = 0; i < 16; i++)
        {
            float angle = i / 16f * Mathf.PI * 2f;
            var marker = new GameObject("Zombie_Spawn_" + i);
            marker.transform.position = new Vector3(Mathf.Sin(angle) * 48f, .05f, Mathf.Cos(angle) * 48f);
            marker.AddComponent<ElderboomSpawnPoint>();
        }
    }

    static void BuildGates()
    {
        var material = Material("Elderboom_Gate_Metal", new Color(.09f, .11f, .12f), .45f);
        FenceYard(new Rect(-62,-48,20,30),new Vector2(-42,-30),material);
        FenceYard(new Rect(-12,50,30,16),new Vector2(6,50),material);
        for (int i = 0; i < 2; i++)
        {
            var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gate.name = i == 0 ? "West_Farm_Gate" : "North_Timber_Yard_Gate";
            gate.transform.position = i==0 ? ElderboomWorldPort.Map(-42,2.25f,30) : ElderboomWorldPort.Map(6,2.25f,-50);
            gate.transform.localScale = new Vector3(4.4f, 2.5f, .3f);
            if(i==0)gate.transform.rotation=Quaternion.Euler(0,90,0);
            gate.GetComponent<Renderer>().sharedMaterial = material;
            var logic=gate.AddComponent<ElderboomGate>();logic.cost=i==0?500:750;logic.areaName=i==0?"West Farm":"North Timber Yard";
        }
    }

    static void FenceYard(Rect bounds,Vector2 gate,Material material)
    {
        var corners=new[]{bounds.min,new Vector2(bounds.xMax,bounds.yMin),bounds.max,new Vector2(bounds.xMin,bounds.yMax)};
        for(int i=0;i<4;i++)
        {
            Vector2 a=corners[i],b=corners[(i+1)%4],direction=(b-a).normalized;
            bool gateOnEdge=Mathf.Abs(Vector2.Distance(a,gate)+Vector2.Distance(gate,b)-Vector2.Distance(a,b))<.01f;
            if(gateOnEdge){FenceSegment(a,gate-direction*2.2f,material);FenceSegment(gate+direction*2.2f,b,material);}else FenceSegment(a,b,material);
        }
    }
    static void FenceSegment(Vector2 a,Vector2 b,Material material)
    {
        var fence=GameObject.CreatePrimitive(PrimitiveType.Cube);fence.name="Yard_Fence";
        fence.transform.position=new Vector3((a.x+b.x)*.5f,2.25f,(a.y+b.y)*.5f);fence.transform.rotation=Quaternion.LookRotation(new Vector3(b.x-a.x,0,b.y-a.y));
        fence.transform.localScale=new Vector3(.22f,2.5f,Vector2.Distance(a,b));fence.GetComponent<Renderer>().sharedMaterial=material;
    }

    static GameObject CloneProvenPlayer()
    {
        var source = EditorSceneManager.OpenScene(SourceSlice, OpenSceneMode.Additive);
        var sourcePlayer = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HDRPSlicePlayer>(true)).FirstOrDefault();
        if (sourcePlayer == null) throw new InvalidOperationException("Source FPS player missing from HDRP slice.");
        var player = UnityEngine.Object.Instantiate(sourcePlayer.gameObject);
        player.transform.position = new Vector3(0f, 1.08f, -8f);
        SceneManager.MoveGameObjectToScene(player, SceneManager.GetActiveScene());
        EditorSceneManager.CloseScene(source, true);
        return player;
    }

    static void ConfigureOperatorViewmodels(GameObject player)
    {
        var camera = player.GetComponentInChildren<Camera>(true);
        if(camera!=null && camera.GetComponent<AudioListener>()==null)camera.gameObject.AddComponent<AudioListener>();
        var controller = player.GetComponent<HDRPSlicePlayer>();
        if (camera == null || controller == null) throw new InvalidOperationException("Port player is missing its FPS camera or controller.");
        Transform soph = camera.transform.Find("Soph_Viewmodel_Pivot");
        Transform eli = CreateOperatorViewmodel(EliPath, "Eli_Viewmodel_Pivot", camera.transform);
        Transform tactical = CreateOperatorViewmodel(TacticalPath, "Tactical_Viewmodel_Pivot", camera.transform);
        if (eli != null) eli.gameObject.SetActive(false);
        if (tactical != null) tactical.gameObject.SetActive(false);
        controller.ConfigurePortedViewmodels(soph, eli, tactical);
        ConfigureViewmodel(soph, "Assets/GameAssets/Soph/Soph_AKMN_Plasma.gltf", "SOPH");
        ConfigureViewmodel(eli, EliPath, "ELI");
        ConfigureViewmodel(tactical, TacticalPath, "TACTICAL");
    }

    static Transform CreateOperatorViewmodel(string assetPath, string name, Transform camera)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(camera, false);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        var model = PrefabUtility.InstantiatePrefab(asset, pivot) as GameObject ?? UnityEngine.Object.Instantiate(asset, pivot);
        model.name = name + "_Model";
        if(PrefabUtility.IsPartOfPrefabInstance(model))PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        var eyes = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name, "Eyes", StringComparison.OrdinalIgnoreCase) || t.name=="Camera");
        if (eyes != null)
        {
            model.transform.localRotation=Quaternion.Inverse(eyes.rotation)*pivot.rotation;
            model.transform.localPosition -= pivot.InverseTransformPoint(eyes.position);
        }
        else
            model.transform.localPosition = new Vector3(0f, -.12f, .34f);
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
        foreach(var cameraNode in model.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(cameraNode);
        foreach(var audioListener in model.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(audioListener);
        return pivot;
    }

    static void ConfigureViewmodel(Transform pivot,string assetPath,string id)
    {
        if(pivot==null)return;
        var model=pivot.GetChild(0).gameObject;
        foreach(var animator in model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        var clips=AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToArray();
        var driver=pivot.gameObject.AddComponent<ElderboomViewmodel>();
        driver.model=model;driver.operatorId=id;
        driver.idle=clips.FirstOrDefault(c=>c.name==(id=="ELI"?"FPS_Idle_Breathing":id=="SOPH"?"Idle_Breathing":"shockwave"));
        driver.reload=clips.FirstOrDefault(c=>c.name==(id=="ELI"?"FPS_Reload_Tactical":"Reload_AK"));
        driver.ability=clips.FirstOrDefault(c=>c.name==(id=="ELI"?"Eli_Acoustic_Ignore_Sig550":id=="SOPH"?"Special_Plasma":"shockwave"));
        if(driver.idle!=null)driver.idle.SampleAnimation(model,0);
        foreach(var node in model.GetComponentsInChildren<Transform>(true))
        {
            if(node.name=="output_unwrapped.002" || node.name=="output_unwrapped_002")node.gameObject.SetActive(id!="TACTICAL");
        }
        if(id!="SOPH")
        {
            string meshName=id=="ELI"?"output_unwrapped.001":"output_unwrapped.003";
            var weapon=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==meshName || t.name==meshName.Replace('.','_'));
            if(weapon!=null)
            {
                var rear=new GameObject("MainWeapon_ADS_Rear").transform;rear.SetParent(weapon,false);
                var front=new GameObject("MainWeapon_ADS_Front").transform;front.SetParent(weapon,false);
                rear.localPosition=id=="ELI"?new Vector3(-.063f,.187f,.004f):new Vector3(.085f,.225f,0);
                front.localPosition=id=="ELI"?new Vector3(.267f,.195f,.004f):new Vector3(.419f,.21241f,0);
            }
        }
    }

    static void BuildZombiePrefab()
    {
        foreach(bool female in new[]{false,true})
        {
            string path=female?"Assets/GameAssets/Ported/ZombieFemale/source/female-zombie-1/female zombie mesh/Meshy_AI_zombie_hoodie_rigged__biped_Animation_Walking_frame_rate_60.fbx":MaleZombiePath;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = new GameObject(female?"ZombieFemale":"Zombie");
            var visual = UnityEngine.Object.Instantiate(source, root.transform);
            visual.name = female?"Female_Zombie_Visual":"Male_Zombie_Visual";
            var renderers=visual.GetComponentsInChildren<Renderer>(true);
            var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            visual.transform.localScale*=1.82f/Mathf.Max(.01f,bounds.size.y);
            bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            visual.transform.localPosition-=Vector3.up*bounds.min.y;
            var animator=visual.GetComponentInChildren<Animator>()??visual.AddComponent<Animator>();
            string controllerPath="Assets/Animations/Port_"+root.name+".controller";AssetDatabase.DeleteAsset(controllerPath);
            var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            string clipRoot=female?"Assets/GameAssets/Ported/ZombieFemale/source/female-zombie-1/":"Assets/GameAssets/Ported/ZombieMale/";
            var mapping=female
                ? new[]{("WalkA","Female Zombie Walking 1.fbx"),("WalkB","Female Zombie Walk 2.fbx"),("AttackA","Zombie Attack.fbx"),("AttackPunch","Zombie Attack 2.fbx"),("DeathA","Zombie Dying.fbx")}
                : new[]{("WalkA","Zombie Walk.fbx"),("WalkB","male-zombie-1Walking-2.fbx"),("AttackA","Zombie Attack.fbx"),("AttackPunch","Zombie Punching attack.fbx"),("DeathA","Zombie Dying 1.fbx")};
            AnimatorState defaultState=null;
            foreach(var pair in mapping)
            {
                var clip=AssetDatabase.LoadAllAssetsAtPath(clipRoot+pair.Item2).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
                if(clip==null)continue;
                var state=controller.layers[0].stateMachine.AddState(pair.Item1);state.motion=clip;
                if(pair.Item1=="WalkA")defaultState=state;
            }
            if(defaultState!=null)controller.layers[0].stateMachine.defaultState=defaultState;
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
            if(female)
            {
                var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Ported/ZombieFemale/source/female-zombie-1/female zombie mesh/Meshy_AI_zombie_hoodie_rigged__biped_texture_0.png");
                foreach(var renderer in renderers)
                {
                    var slots=renderer.sharedMaterials;
                    for(int i=0;i<slots.Length;i++)
                    {
                        var mat=Material("Port_Zombie_Female_"+i,Color.white,.35f);
                        if(albedo!=null)mat.SetTexture("_BaseColorMap",albedo);
                        slots[i]=mat;
                    }
                    renderer.sharedMaterials=slots;
                }
            }
            else
            foreach(var renderer in renderers)
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    var mat=Material("Port_Zombie_Male_"+i,Color.white,.2f);
                    mat.SetTexture("_BaseColorMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Path.GetDirectoryName(MaleZombiePath)+"/Meshy_AI_male_zombie_rigged_biped_Meshy_Merged_Animations_"+i+".png"));slots[i]=mat;
                }
                renderer.sharedMaterials=slots;
            }
            var hitbox=root.AddComponent<CapsuleCollider>();hitbox.center=Vector3.up*.9f;hitbox.height=1.8f;hitbox.radius=.38f;
            root.AddComponent<ElderboomZombie>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, female?"Assets/Resources/Elderboom/ZombieFemale.prefab":ZombiePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException("Failed to create Elderboom zombie prefab.");
        }
    }
}
