using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;
using Random=UnityEngine.Random;

// Authored layout derived from the six saved imagegen targets. Idempotent: existing
// reconstruction is never silently overwritten, so subsequent hand edits are safe.
static partial class ElderboomClaudeBlocks
{
    const string ReconstructionRoot="Elderboom_Concept_Reconstruction";
    const string ReconGenerated="Assets/GameAssets/Elderboom/ArtDirectionGenerated/";
    const string ReconFree="Assets/ThirdParty/PolyHaven/ArtDirection/";
    public static void FinalizeRuntimeArt()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");var root=GameObject.Find(ReconstructionRoot);
        string folder=ReconGenerated+"RuntimeMeshes";Directory.CreateDirectory(folder);var meshes=new Dictionary<Mesh,Mesh>();
        Mesh Copy(Mesh source)
        {
            if(!source||!AssetDatabase.GetAssetPath(source).EndsWith(".glb"))return source;
            if(meshes.TryGetValue(source,out var existing))return existing;
            var copy=Object.Instantiate(source);AssetDatabase.CreateAsset(copy,folder+"/Mesh_"+meshes.Count+".asset");meshes.Add(source,copy);return copy;
        }
        foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true))mf.sharedMesh=Copy(mf.sharedMesh);
        foreach(var col in root.GetComponentsInChildren<MeshCollider>(true))col.sharedMesh=Copy(col.sharedMesh);
        foreach(var lod in root.GetComponentsInChildren<LODGroup>())lod.RecalculateBounds();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/ArtDirection/runtime-meshes.txt","Standalone shared meshes: "+meshes.Count+". PBR materials and textures already compressed; avoids source GLB texture dependencies.");
        EditorUtility.UnloadUnusedAssetsImmediate();
        foreach(var v in ElderboomBlockArtDirection.Views){ElderboomBlockArtDirection.Render(v,v.id+"_after");EditorUtility.UnloadUnusedAssetsImmediate();}
        Debug.Log("[ArtReconstruction] Final runtime meshes and six captures complete");
    }
    public static void CompressReconstructionTextures()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var root=GameObject.Find(ReconstructionRoot);string folder="Assets/GameAssets/Elderboom/ArtDirectionGenerated/RuntimeMaterials";Directory.CreateDirectory(folder);
        foreach(var lod in root.GetComponentsInChildren<LODGroup>()){lod.fadeMode=LODFadeMode.None;lod.animateCrossFading=false;}
        var textures=new Dictionary<Texture,Texture>();var materials=new Dictionary<Material,Material>();int n=0;
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var array=renderer.sharedMaterials;
            for(int i=0;i<array.Length;i++)
            {
                var source=array[i];if(!source||!AssetDatabase.GetAssetPath(source).EndsWith(".glb"))continue;
                if(!materials.TryGetValue(source,out var material))
                {
                    material=new Material(source);
                    foreach(var property in material.GetTexturePropertyNames())
                    {
                        var tex=material.GetTexture(property) as Texture2D;if(!tex)continue;
                        if(!textures.TryGetValue(tex,out var compressed))
                        {
                            var copy=Object.Instantiate(tex);copy.name="PBR_"+n;
                            EditorUtility.CompressTexture(copy,TextureFormat.BC7,TextureCompressionQuality.Normal);
                            AssetDatabase.CreateAsset(copy,folder+"/PBR_"+n+++".asset");compressed=copy;textures.Add(tex,compressed);
                        }
                        material.SetTexture(property,compressed);
                    }
                    AssetDatabase.CreateAsset(material,folder+"/Material_"+materials.Count+".mat");materials.Add(source,material);
                }
                array[i]=material;
            }
            renderer.sharedMaterials=array;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/ArtDirection/texture-compression.txt",$"BC7 GPU textures at original resolution: {textures.Count}; shared PBR materials: {materials.Count}");
        Debug.Log("[ArtReconstruction] Compressed textures "+textures.Count);
    }
    public static void FinishArtVisibility()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        if(profile.TryGet<Exposure>(out var exposure))exposure.fixedExposure.Override(7.4f);
        EditorUtility.SetDirty(profile);
        var o=terrain.transform.position;var sz=data.size;int before=data.treeInstanceCount;
        bool WorkingLand(Vector3 p)=> (p.x>-94&&p.x<-62&&p.z>-46&&p.z<0)||(p.x>-43&&p.x<4&&p.z>51&&p.z<94);
        data.SetTreeInstances(data.treeInstances.Where(t=>!WorkingLand(o+Vector3.Scale(t.position,sz))).ToArray(),true);
        // The working yards need contiguous open space. Keep all woodland outside them.
        int disabled=0;foreach(var go in scene.GetRootGameObjects())foreach(var lod in go.GetComponentsInChildren<LODGroup>())
        {
            if(lod.transform.IsChildOf(GameObject.Find(ReconstructionRoot).transform))continue;
            string name=lod.name.ToLowerInvariant();if(!(name.Contains("alder")||name.Contains("tree")||name.Contains("pine")))continue;
            if(WorkingLand(lod.transform.position)){lod.gameObject.SetActive(false);disabled++;}
        }
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/ArtDirection/visibility.txt",$"Night fixed EV 7.4; terrain trees removed only in two working yards: {before-data.treeInstanceCount}; standalone trees disabled: {disabled}");
        Debug.Log("[ArtReconstruction] Visibility pass saved");
    }
    public static void FinishConceptBlocks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        recon=GameObject.Find(ReconstructionRoot).transform;reconLog=new StringBuilder();reconStructures.Clear();reconSites.Clear();
        if(recon.Find("Final_Props_And_Forester_Cabin"))throw new InvalidOperationException("Final art pass already present");
        var t=ReconGroup("Final_Props_And_Forester_Cabin");
        var woods=recon.Find("06_Woodland_Ruin_Precinct");
        foreach(Transform child in woods)if(child.name=="Working_LeanTo")child.gameObject.SetActive(false);
        ReconModel("forester_cabin",t,new Vector2(61,71),4.5f,160);
        foreach(var p in new[]{new Vector2(20.1f,14),new Vector2(31.8f,12),new Vector2(-75,-21),new Vector2(25,-66)})
            ReconFreeProp("wicker_basket_01",t,p,.37f,23);
        foreach(var p in new[]{new Vector2(-72,-34),new Vector2(-31,14),new Vector2(44,32)})ReconFreeProp("watering_can_metal_01",t,p,.38f,Random.Range(0,180));
        foreach(var site in new[]{new Vector2(-82,-39),new Vector2(-70,-36),new Vector2(-29,12),new Vector2(43,30)})
        for(int row=0;row<3;row++)for(int col=0;col<5;col++)
            ReconFreeProp("shrub_sorrel_01",t,site+new Vector2((row-1)*1.1f,(col-2)*.9f),.3f,Random.Range(0,360),false,"shrub_sorrel_01_b");
        foreach(var site in new[]{new Vector2(18.8f,13.6f),new Vector2(29,12)})
        for(int k=0;k<7;k++)
        {
            var p=site+Random.insideUnitCircle*.32f;var go=ReconFreeProp(k%2==0?"yellow_onion":"sweet_potato",t,p,.08f,Random.Range(0,180));
            go.transform.position+=Vector3.up*1.2f;
        }
        Physics.SyncTransforms();reconLog.AppendLine(BakeAccessNavigation());
        AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/ArtDirection/final-dressing.txt",reconLog.ToString());
        Debug.Log("[ArtReconstruction] Final cabin and CC0 garden/market props saved");
    }
    static StringBuilder reconLog;
    static Transform recon;
    static readonly List<GameObject> reconStructures=new List<GameObject>();
    static readonly List<Vector2[]> reconPaths=new List<Vector2[]>();
    static readonly List<Bounds> reconSites=new List<Bounds>();

    public static void ReconstructConceptBlocks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        if(GameObject.Find(ReconstructionRoot))throw new InvalidOperationException("Reconstruction already present; inspect instead of replacing hand edits.");
        foreach(var id in new[]{"market_stall","hay_barn","sawmill_shed","stone_ossuary","fishing_shelter","ruined_stone_gateway"})
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(ReconGenerated+id+".glb"))throw new InvalidOperationException("Generated model not imported: "+id);
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        artWood=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/weathered_brown_planks_Material.asset");
        artIron=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/ForgedIron.asset");
        artSoil=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/mud_forest_Material.asset");
        artStone=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/RiverStone.asset");
        if(!artWood||!artIron||!artStone)throw new InvalidOperationException("Missing scene materials");
        Random.InitState(531105);reconLog=new StringBuilder();reconStructures.Clear();reconPaths.Clear();reconSites.Clear();
        recon=new GameObject(ReconstructionRoot).transform;
        VillageConcept();FarmConcept();TimberConcept();CemeteryConcept();CreekConcept();WoodsConcept();
        foreach(var p in reconPaths)PaintLandmarkPath(p,1.55f);
        ReconUnderstory();Physics.SyncTransforms();
        reconLog.AppendLine(BakeAccessNavigation());
        // Spawn markers may be displaced by a new building, never left inside one.
        foreach(var spawn in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None))
        {
            if(NavMesh.SamplePosition(spawn.transform.position,out var hit,8,NavMesh.AllAreas))
            {if(Vector3.Distance(hit.position,spawn.transform.position)>.8f)reconLog.AppendLine("Reseated spawn "+spawn.name+" "+spawn.transform.position+" -> "+hit.position);spawn.transform.position=hit.position;}
            else throw new InvalidOperationException("No navigation near spawn "+spawn.name);
        }
        ValidateReconstruction();
        AssetDatabase.SaveAssets();var scene=EditorSceneManager.GetActiveScene();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/ArtDirection/reconstruction-report.txt",reconLog.ToString());
        CaptureReconstruction();EditorSceneManager.SaveScene(scene);
        Debug.Log("[ArtReconstruction] Saved six dressed blocks and comparison views.");
    }

    static Transform ReconGroup(string name)=>ArtGroup(name,recon);
    static GameObject ReconModel(string id,Transform group,Vector2 p,float height,float yaw,string name=null)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ReconGenerated+id+".glb");
        var go=Object.Instantiate(source,group);go.name=name??id;go.transform.rotation=Quaternion.Euler(0,yaw,0);
        var collision=go.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="COLLISION");
        if(!collision)throw new InvalidOperationException("Missing simplified collision: "+id);
        foreach(var r in collision.GetComponentsInChildren<Renderer>())Object.DestroyImmediate(r);SolidMeshes(collision.gameObject);
        var high=go.GetComponentsInChildren<Renderer>().Where(r=>r.name=="LOD0").ToArray();
        var low=go.GetComponentsInChildren<Renderer>().Where(r=>r.name=="LOD1").ToArray();
        if(high.Length==0||low.Length==0)throw new InvalidOperationException("Missing model LODs: "+id);
        var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.13f,high),new LOD(.018f,low)});lod.fadeMode=LODFadeMode.CrossFade;lod.RecalculateBounds();
        var b=RB(go);go.transform.localScale*=height/b.size.y;SetCenter(go,At(p));Seat(go,p,.04f);
        b=RB(go);ClearLandmarkSite(b,go,reconLog);LevelLandmarkSite(b,reconLog);
        go.isStatic=true;reconStructures.Add(go);reconSites.Add(b);
        reconLog.AppendLine(go.name+" center="+p+" size="+b.size+" yaw="+yaw);
        return go;
    }
    static GameObject ReconFreeProp(string id,Transform parent,Vector2 p,float height,float yaw=0,bool solid=false,string piece=null)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ReconFree+id+"/"+id+"_2k.gltf");
        if(!source)throw new InvalidOperationException("Missing CC0 prop "+id);
        GameObject go;
        if(piece==null)go=Object.Instantiate(source,parent);
        else
        {
            var part=source.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==piece);
            if(!part)throw new InvalidOperationException("Missing modular piece "+piece);go=Object.Instantiate(part.gameObject,parent);
        }
        go.name="CC0_"+id+(piece==null?"":"_"+piece);go.SetActive(true);go.transform.rotation=Quaternion.Euler(0,yaw,0);
        var b=RB(go);go.transform.localScale*=height/Mathf.Max(.01f,b.size.y);SetCenter(go,At(p));Seat(go,p,.02f);
        if(solid)SolidMeshes(go);go.isStatic=true;return go;
    }
    static void ReconPath(params Vector2[] p){reconPaths.Add(p);}
    static void ReconLight(Transform parent,Vector2 p,float height=2.4f,float power=360)
    {Lantern(parent,At(p,height),power,8);}
    static void ReconCrates(Transform t,Vector2 at,int count,float yaw=0)
    {
        for(int n=0;n<count;n++)
        {
            var p=at+new Vector2(n%2*.8f,n/2*.7f);
            ReconFreeProp("wooden_crate_01",t,p,.48f,yaw+Random.Range(-8,8),true);
        }
        ReconFreeProp("wine_barrel_01",t,at+new Vector2(-.8f,.25f),.92f,yaw+15,true);
    }
    static void ReconFence(Transform t,params Vector2[] p)
    {for(int i=1;i<p.Length;i++){int start=t.childCount;FenceArt(t,p[i-1],p[i]);if(t.childCount>start)ReconLowWall(t.GetChild(start),1.2f);}}
    static void ReconWall(Transform t,params Vector2[] p)
    {for(int i=1;i<p.Length;i++){int start=t.childCount;FenceArt(t,p[i-1],p[i],true);if(t.childCount>start)ReconLowWall(t.GetChild(start),1.25f);}}
    static void ReconLowWall(Transform group,float height)
    {
        foreach(var child in group.Cast<Transform>().ToArray())
        {
            var b=RB(child.gameObject);var wrapper=new GameObject("Low_enclosure_section").transform;wrapper.SetParent(group);wrapper.position=new Vector3(b.center.x,b.min.y,b.center.z);
            child.SetParent(wrapper,true);wrapper.localScale=new Vector3(1,Mathf.Min(1,height/b.size.y),1);
        }
    }
    static void ReconPlot(Transform t,Vector2 center,int rows,int columns,float yaw=0)
    {
        var q=Quaternion.Euler(0,yaw,0);var leaf=DetailFoliage("Fern",new Color(.39f,.46f,.26f));
        for(int row=0;row<rows;row++)
        {
            Vector2 Point(float z){var d=q*new Vector3((row-(rows-1)*.5f)*1.1f,0,z);return center+new Vector2(d.x,d.z);}
            PaintLandmarkPath(new[]{Point(-columns*.45f),Point(columns*.45f)},.37f);
            for(int k=0;k<columns;k++)
            {var p=Point((k-(columns-1)*.5f)*.9f);EcoPlant(t,"Fern_B",leaf,p,Random.Range(.24f,.37f));}
        }
    }

    static void VillageConcept()
    {
        var t=ReconGroup("01_Village_Market_And_Domestic_Gardens");
        ReconModel("market_stall",t,new Vector2(18,13),3.05f,0,"North_forecourt_linen_stall");
        ReconModel("market_stall",t,new Vector2(29.5f,11.3f),2.9f,-15,"North_forecourt_produce_stall");
        ReconCrates(t,new Vector2(20.2f,13),3,6);ReconCrates(t,new Vector2(33,12),2,19);
        ReconLight(t,new Vector2(16,14),2.5f,420);ReconLight(t,new Vector2(29,15),2.5f,420);
        ReconFreeProp("wooden_broom",t,new Vector2(20,15),1.25f,80);
        ReconPath(new Vector2(7,24),new Vector2(17,23),new Vector2(29,23),new Vector2(38,25));
        // Side gardens sit behind the existing facades, not in the church footprint.
        ReconPlot(t,new Vector2(-29,12),4,6);ReconFence(t,new Vector2(-33,8),new Vector2(-33,17),new Vector2(-25,17),new Vector2(-25,14));
        ReconPlot(t,new Vector2(43,30),4,5);ReconFence(t,new Vector2(39,29),new Vector2(39,34),new Vector2(47,34),new Vector2(47,28));
        ReconCrates(t,new Vector2(-30,6.5f),2,10);
        Prop("S_Chopped_Wood_Pile",t,new Vector2(46,27.2f),90,.5f);
        Prop("S_Old_Wooden_Bench",t,new Vector2(34,22),180,.9f);
    }
    static void FarmConcept()
    {
        var t=ReconGroup("02_Farm_Barn_Paddocks_And_Crops");
        ReconModel("hay_barn",t,new Vector2(-77,-15),6.8f,165);
        Shed(t,new Vector2(-67,-28),new Vector2(5,3),180);
        BuildCart(t,new Vector2(-67,-27.6f),175);
        ReconCrates(t,new Vector2(-84,-12),3,15);
        ReconFreeProp("wooden_broom",t,new Vector2(-73.5f,-20),1.3f,160);
        ReconFreeProp("wooden_axe",t,new Vector2(-70,-26),.75f,55);
        ReconLight(t,new Vector2(-74,-19),2.7f,550);
        ReconPath(new Vector2(-42,-1),new Vector2(-57,-10),new Vector2(-67,-20),new Vector2(-76,-23),new Vector2(-88,-27),new Vector2(-86,-40),new Vector2(-75,-47),new Vector2(-61,-44));
        ReconPath(new Vector2(-75,-23),new Vector2(-69,-31),new Vector2(-61,-34),new Vector2(-55,-29));
        ReconFence(t,new Vector2(-90,-7),new Vector2(-90,-20),new Vector2(-85,-20));
        ReconFence(t,new Vector2(-90,-30),new Vector2(-94,-30),new Vector2(-94,-45),new Vector2(-84,-47),new Vector2(-79,-44));
        ReconFence(t,new Vector2(-74,-33),new Vector2(-74,-41),new Vector2(-65,-41));
        ReconPlot(t,new Vector2(-82,-39),5,9,8);
        ReconPlot(t,new Vector2(-70,-36),4,6,-3);
        foreach(var p in new[]{new Vector2(-85,-29),new Vector2(-87,-31),new Vector2(-84,-32)})Prop("S_Round_Hay_Bale",t,p,Random.Range(0,180),1.15f);
        Prop("S_Old_Wooden_Trough",t,new Vector2(-91,-38),85,.9f);
        Prop("S_Chopped_Wood_Pile",t,new Vector2(-81,-9),90,.55f);
    }
    static void TimberConcept()
    {
        var t=ReconGroup("03_Sawmill_Cutting_Seasoning_And_Loading");
        // The generated open sawshed replaces the previous small primitive shelter.
        var old=GameObject.Find("Elderboom_Environment_Art")?.transform;
        foreach(var name in new[]{"Timber_Sawmill_Yard","Sawmill_Receiving_And_Cutting"}){var part=old?.Find(name);if(part)part.gameObject.SetActive(false);}
        ReconModel("sawmill_shed",t,new Vector2(-18,69),5.5f,180);
        ReconPath(new Vector2(7,33),new Vector2(-1,42),new Vector2(-17,49),new Vector2(-26,57),new Vector2(-29,71),new Vector2(-27,80),new Vector2(-15,86),new Vector2(0,83),new Vector2(12,72),new Vector2(12,60));
        ReconPath(new Vector2(-26,61),new Vector2(-17,61),new Vector2(-7,66),new Vector2(1,74),new Vector2(12,72));
        for(int i=0;i<3;i++)
        {
            var p=new Vector2(-28+i*10,87);Shed(t,p,new Vector2(6,3.2f),0);
            for(int layer=0;layer<5;layer++)for(int n=0;n<5;n++)
                ArtBox("Seasoned_oak_board",t,At(p+new Vector2((n-2)*.35f,0),.25f+layer*.18f),new Vector3(.32f,.12f,2.7f+Random.Range(-.12f,.12f)),artWood);
        }
        for(int i=0;i<3;i++)Prop("S_Chopped_Wood_Pile",t,new Vector2(-39+i*.6f,57+i*5),90,.85f);
        var at=At(new Vector2(-36,70));
        foreach(float x in new[]{-3f,3f})
        {
            ArtBox("Loading_gantry_upright",t,at+new Vector3(x,2,0),new Vector3(.35f,4,.35f),artWood,default,true);
            ArtBox("Gantry_diagonal_brace",t,at+new Vector3(x-Mathf.Sign(x)*.6f,3.2f,0),new Vector3(.23f,1.9f,.23f),artWood,Quaternion.Euler(0,0,x<0?-40:40));
        }
        ArtBox("Gantry_crossbeam",t,at+Vector3.up*4,new Vector3(6.8f,.4f,.4f),artWood);
        ArtBox("Hanging_chain",t,at+Vector3.up*2.8f,new Vector3(.045f,2.2f,.045f),artIron);
        ReconLight(t,new Vector2(-22,65),3.6f,650);ReconLight(t,new Vector2(-14,65),3.6f,650);
        ReconCrates(t,new Vector2(-9,70),2);BuildCart(t,new Vector2(1,80),-20);
        ReconFreeProp("wooden_axe",t,new Vector2(-29,66),.8f,20);
        ReconFence(t,new Vector2(-42,56),new Vector2(-42,76),new Vector2(-35,82));
        ReconFence(t,new Vector2(-34,92),new Vector2(-15,94),new Vector2(4,91));
    }
    static void CemeteryConcept()
    {
        var t=ReconGroup("04_Cemetery_Ossuary_And_Family_Plots");
        ReconModel("stone_ossuary",t,new Vector2(96,-21),4.4f,235);
        ReconModel("stone_ossuary",t,new Vector2(91,4),3.1f,270,"Small_family_vault");
        ReconPath(new Vector2(56,0),new Vector2(64,-4),new Vector2(73,-4),new Vector2(85,-6),new Vector2(92,-14),new Vector2(91,-25),new Vector2(81,-33),new Vector2(66,-28),new Vector2(62,-17),new Vector2(64,-4));
        ReconPath(new Vector2(85,-6),new Vector2(91,-2),new Vector2(96,8),new Vector2(87,16),new Vector2(77,19));
        var occupied=GameObject.Find("Elderboom_Environment_Art")?.transform.Find("Cemetery_Consecrated_Ground");
        var existing=occupied?occupied.GetComponentsInChildren<Renderer>().Select(r=>new Vector2(r.bounds.center.x,r.bounds.center.z)).ToArray():new Vector2[0];
        for(int row=0;row<6;row++)for(int col=0;col<5;col++)
        {
            var p=new Vector2(84+col*2.5f,-1-row*3.2f)+Random.insideUnitCircle*.22f;
            if(reconSites.Any(b=>p.x>b.min.x-1&&p.x<b.max.x+1&&p.y>b.min.z-1&&p.y<b.max.z+1)||reconPaths.Any(path=>LineDist(p,path)<2.2f)||existing.Any(e=>Vector2.Distance(e,p)<1.5f))continue;
            var g=Prop("S_Old_Gravestone",t,p,270+Random.Range(-8,8),Random.Range(.7f,1.05f));
            if(g)g.transform.Rotate(0,0,Random.Range(-4f,4f));
            PaintLandmarkPath(new[]{p+Vector2.left*.2f,p+Vector2.left*1.5f},.42f);
        }
        ReconWall(t,new Vector2(100,-26),new Vector2(104,-17),new Vector2(105,-4),new Vector2(103,9),new Vector2(96,16));
        ReconWall(t,new Vector2(87,-35),new Vector2(75,-37),new Vector2(66,-33));
        var cypress=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Trees/Cypress/Cypress.prefab");
        foreach(var p in new[]{new Vector2(101,-23),new Vector2(98,3),new Vector2(89,-30),new Vector2(75,-33)})Place(cypress,t,p,Random.Range(6.3f,8.2f),.04f);
        ReconLight(t,new Vector2(93,-22),2.3f,180);ReconLight(t,new Vector2(89,3),2.1f,140);
        ReconFreeProp("wooden_broom",t,new Vector2(94,-23),1.35f,45);
        Prop("S_Old_Wooden_Bench",t,new Vector2(88,10),270,.85f);
    }
    static void CreekConcept()
    {
        var t=ReconGroup("05_Creek_Fishing_And_Cargo_Landing");
        ReconModel("fishing_shelter",t,new Vector2(23,-68),3.3f,15);
        ReconCrates(t,new Vector2(19,-68),4,5);ReconCrates(t,new Vector2(27,-70),2,19);
        ReconPath(new Vector2(7,-55),new Vector2(14,-59),new Vector2(24,-61),new Vector2(33,-66),new Vector2(38,-76),new Vector2(31,-86),new Vector2(18,-89));
        ReconPath(new Vector2(-9,-61),new Vector2(-21,-66),new Vector2(-31,-73),new Vector2(-34,-84));
        ReconLight(t,new Vector2(23,-65),2.5f,460);
        // One landing along the south bank; never add a second crossing or replace the stone bridge.
        var dock=ReconFreeProp("modular_wooden_pier",t,new Vector2(25,-54),2,90,true,"modular_wooden_pier_section_01");
        var b=RB(dock);dock.transform.position+=Vector3.up*(.4f-b.max.y);
        Prop("S_Old_Wooden_Trough",t,new Vector2(35,-68),15,.8f);
        LicensedModel("wooden_bucket_01",t,new Vector2(26,-62),.45f);
    }
    static void WoodsConcept()
    {
        var t=ReconGroup("06_Woodland_Ruin_Precinct");
        ReconModel("ruined_stone_gateway",t,new Vector2(87,89),3.8f,25);
        // A small woodsman's work shelter complements the retained amphitheatre.
        Shed(t,new Vector2(61,71),new Vector2(5,3.5f),-20);
        Prop("S_Chopped_Wood_Pile",t,new Vector2(59,70),70,.6f);
        ReconCrates(t,new Vector2(63,70),2,25);
        ReconFreeProp("wooden_axe",t,new Vector2(62,69),.75f,10);
        ReconPath(new Vector2(81,59),new Vector2(75,64),new Vector2(65,67),new Vector2(61,74),new Vector2(66,83),new Vector2(74,89),new Vector2(84,88),new Vector2(90,83),new Vector2(94,75),new Vector2(90,66),new Vector2(81,59));
        ReconPath(new Vector2(79,68),new Vector2(79,70.5f));
        ReconWall(t,new Vector2(58,78),new Vector2(62,85),new Vector2(73,92),new Vector2(82,92));
        ReconWall(t,new Vector2(92,90),new Vector2(98,83),new Vector2(99,72),new Vector2(95,65));
        Prop("S_Old_Wooden_Bench",t,new Vector2(92,76),90,.8f);
        ReconLight(t,new Vector2(61,69),2.4f,250);
        ReconLight(t,new Vector2(86,89),2.2f,140);
        BuildCart(t,new Vector2(93,81),35);
    }
    static void ReconUnderstory()
    {
        var t=ReconGroup("Contact_Foliage_And_Path_Edges");
        var fern=DetailFoliage("Fern",new Color(.42f,.49f,.3f));
        foreach(var path in reconPaths)for(int k=1;k<path.Length;k++)
        {
            var a=path[k-1];var b=path[k];var d=(b-a).normalized;var n=new Vector2(-d.y,d.x);
            int count=Mathf.FloorToInt(Vector2.Distance(a,b)/2.4f);
            for(int j=0;j<count;j++)foreach(float side in new[]{-1f,1f})
            {
                var p=Vector2.Lerp(a,b,(j+.5f)/Mathf.Max(1,count))+n*side*Random.Range(2.3f,3.2f);
                if(Ground(p)<-.2f||PassDist(p)<6||reconSites.Any(bb=>p.x>bb.min.x-1&&p.x<bb.max.x+1&&p.y>bb.min.z-1&&p.y<bb.max.z+1))continue;
                EcoPlant(t,j%2==0?"Fern_B":"Fern_C",fern,p,Random.Range(.28f,.58f));
            }
        }
    }
    static void ValidateReconstruction()
    {
        int bad=0;
        foreach(var r in recon.GetComponentsInChildren<Renderer>())if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name.Contains("Error"))){bad++;reconLog.AppendLine("FAIL material "+r.name);}
        foreach(var model in reconStructures)
        {
            var b=RB(model);bool grounded=Mathf.Abs(b.min.y-Ground(new Vector2(b.center.x,b.center.z)))<.2f;
            reconLog.AppendLine((grounded?"PASS ":"FAIL ")+model.name+" grounded, LODs="+model.GetComponent<LODGroup>().lodCount+", colliders="+model.GetComponentsInChildren<MeshCollider>().Length);
            if(!grounded)bad++;
        }
        foreach(var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None))
        {
            var p=gate.transform.position;bool clear=!reconStructures.Any(go=>{var b=RB(go);b.Expand(2);return b.Contains(p+Vector3.up);});
            reconLog.AppendLine((clear?"PASS ":"FAIL ")+"gate clearance "+gate.name);if(!clear)bad++;
        }
        var horse=Object.FindAnyObjectByType<ElderboomHorseAssembly>();
        foreach(var part in horse.parts)
        {
            bool clear=!reconStructures.Any(go=>RB(go).Contains(part.anchor.position));
            reconLog.AppendLine((clear?"PASS ":"FAIL ")+"horse part clear "+part.label);if(!clear)bad++;
        }
        reconLog.AppendLine("New renderers="+recon.GetComponentsInChildren<Renderer>().Length+"; failures="+bad);
        File.WriteAllText("Logs/ArtDirection/reconstruction-report.txt",reconLog.ToString());
        if(bad>0)throw new InvalidOperationException("Art reconstruction validation failed: "+bad);
    }
    public static void CaptureReconstruction()
    {
        Directory.CreateDirectory(ElderboomBlockArtDirection.Folder);
        foreach(var v in ElderboomBlockArtDirection.Views)
        {ElderboomBlockArtDirection.Render(v,v.id+"_after");ElderboomBlockArtDirection.Render(v,v.id+"_reverse_after",true);}
    }
    public static void ValidateFinishedConcept()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        recon=GameObject.Find(ReconstructionRoot).transform;reconLog=new StringBuilder();
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        reconStructures.Clear();foreach(var lod in recon.GetComponentsInChildren<LODGroup>())if(lod.transform.Find("COLLISION"))reconStructures.Add(lod.gameObject);
        foreach(var lod in recon.GetComponentsInChildren<LODGroup>()){lod.fadeMode=LODFadeMode.None;lod.animateCrossFading=false;}
        foreach(var group in recon.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Enclosure"||t.name=="Burial_wall"))
            foreach(var child in group.GetComponentsInChildren<Transform>(true))child.gameObject.SetActive(true);
        ValidateReconstruction();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        ElderboomMapValidation.Run();
    }
}
