using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.AI;
using Object = UnityEngine.Object;

static partial class ElderboomClaudeBlocks
{
    const string StoneBridgeFolder = "Assets/ThirdParty/CGTrader/OldStoneBridge/";
    const string StoneBridgeModel = StoneBridgeFolder + "Old stone bridgeFBX.fbx";
    const string StoneBridgeName = "SouthCreek_OldStoneBridge";
    const string BridgeReview = "Logs/ArtReview/Bridge";

    static Material ImportStoneBridgeMaterial()
    {
        AssetDatabase.ImportAsset(StoneBridgeModel, ImportAssetOptions.ForceSynchronousImport);
        var model = (ModelImporter)AssetImporter.GetAtPath(StoneBridgeModel);
        model.importAnimation = false;
        model.isReadable = true;
        model.SaveAndReimport();
        foreach (var file in new[]{"TEXTURE.jpg", "NORMALMAP.jpg"})
        {
            AssetDatabase.ImportAsset(StoneBridgeFolder + file, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(StoneBridgeFolder + file);
            importer.textureType = file.StartsWith("NORMAL") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !file.StartsWith("NORMAL");
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        var material = new Material(Shader.Find("HDRP/Lit")){name="OldStoneBridge_Stone_HDRP"};
        material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StoneBridgeFolder+"TEXTURE.jpg"));
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StoneBridgeFolder+"NORMALMAP.jpg"));
        material.SetColor("_BaseColor",new Color(.84f,.85f,.81f));
        material.SetFloat("_Metallic",0);
        material.SetFloat("_Smoothness",.18f);
        material.SetFloat("_NormalScale",.65f);
        HDMaterial.ValidateMaterial(material);
        return SaveArt(material,"OldStoneBridge_Stone_HDRP");
    }

    static GameObject InstantiateStoneBridge(Material material)
    {
        var root = new GameObject(StoneBridgeName);
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(StoneBridgeModel),root.transform);
        model.name="Original_FBX";
        var bounds=RB(root);
        // Long axis across the creek; preserve the model's proportions uniformly.
        if(bounds.size.x>bounds.size.z)root.transform.rotation=Quaternion.Euler(0,90,0);
        bounds=RB(root);
        root.transform.localScale=Vector3.one*(3.6f/bounds.size.x);
        bounds=RB(root);
        root.transform.position+=new Vector3(6.7f-bounds.center.x,-bounds.min.y,-47.7f-bounds.center.z);
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.BatchingStatic);
        }
        SolidMeshes(root);
        return root;
    }

    internal static float BridgeSurface(GameObject bridge,float x,float z)
    {
        float y=float.NegativeInfinity;
        foreach(var col in bridge.GetComponentsInChildren<MeshCollider>())
            if(col.Raycast(new Ray(new Vector3(x,50,z),Vector3.down),out var hit,100))y=Mathf.Max(y,hit.point.y);
        if(float.IsNegativeInfinity(y))throw new InvalidOperationException("No bridge surface at "+x+", "+z);
        return y;
    }

    public static void BridgeSurvey()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        Directory.CreateDirectory(BridgeReview);
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var bridge=InstantiateStoneBridge(ImportStoneBridgeMaterial());
        Physics.SyncTransforms();
        var b=RB(bridge);var report=new StringBuilder("Imported FBX bounds: "+b+"\n");
        for(int i=1;i<20;i++)
        {
            float z=Mathf.Lerp(b.min.z,b.max.z,i/20f);
            report.AppendLine($"z={z:F3}, deck={BridgeSurface(bridge,b.center.x,z):F3}, terrain={Ground(new Vector2(b.center.x,z)):F3}");
        }
        File.WriteAllText(BridgeReview+"/survey.txt",report.ToString());
        Debug.Log(report.ToString());
        // Survey does not save scene placement.
    }

    [MenuItem("Elderboom HDRP/Import Stone Bridge")]
    public static void BridgeBuild()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before importing the bridge");
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Elderboom_Unity_Port.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        ApplyStoneBridge();
        Physics.SyncTransforms();
        string nav=Bake();
        Directory.CreateDirectory(BridgeReview);
        File.WriteAllText(BridgeReview+"/navigation.txt",nav);
        ValidateStoneBridge();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        BridgeCapture();
        Selection.activeGameObject=GameObject.Find(StoneBridgeName);
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("Stone bridge imported, validated and saved in the open scene.");
    }

    static void ApplyStoneBridge()
    {
        if(!File.Exists(StoneBridgeModel))return;
        Directory.CreateDirectory(BridgeReview);
        var gate=Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(g=>g.areaName=="South Creek");
        var old=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(t=>t.name=="Bridge_Deck");
        if(old)old.gameObject.SetActive(false);
        var previous=GameObject.Find(StoneBridgeName);if(previous)Object.DestroyImmediate(previous);
        var bridge=InstantiateStoneBridge(ImportStoneBridgeMaterial());
        Physics.SyncTransforms();var b=RB(bridge);
        float za=Mathf.Lerp(b.min.z,b.max.z,.07f),zb=Mathf.Lerp(b.min.z,b.max.z,.93f);
        float da=Ground(new Vector2(b.center.x,za))-BridgeSurface(bridge,b.center.x,za);
        float db=Ground(new Vector2(b.center.x,zb))-BridgeSurface(bridge,b.center.x,zb);
        bridge.transform.position+=Vector3.up*(Mathf.Min(da,db)-.035f);
        Physics.SyncTransforms();b=RB(bridge);
        // The masonry exists before purchase. A visible timber barricade, rather
        // than the bridge itself, is removed when the passage is unlocked.
        gate.reveal=null;
        foreach(Transform child in gate.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        float deck=BridgeSurface(bridge,b.center.x,b.center.z);
        gate.transform.SetPositionAndRotation(new Vector3(b.center.x,deck+1.4f,b.center.z),Quaternion.identity);
        var box=gate.GetComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(3.6f,2.8f,.65f);
        var wood=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/weathered_brown_planks_Material.asset");
        if(!wood)throw new InvalidOperationException("Bridge barricade wood material missing");
        for(int i=0;i<10;i++)ArtBox("Barricade_plank_"+i,gate.transform,new Vector3(b.center.x+(i-4.5f)*.34f,deck+1.25f,b.center.z),new Vector3(.32f,2.5f,.16f),wood);
        foreach(float x in new[]{-1.7f,1.7f})ArtBox("Barricade_post",gate.transform,new Vector3(b.center.x+x,deck+1.35f,b.center.z),new Vector3(.2f,2.7f,.22f),wood);
        foreach(float y in new[]{.55f,1.9f})ArtBox("Barricade_crossbrace",gate.transform,new Vector3(b.center.x,deck+y,b.center.z+.12f),new Vector3(3.6f,.19f,.13f),wood);
        EditorUtility.SetDirty(gate);
        var report=new StringBuilder("Native FBX import; supplied colour and normal maps; HDRP/Lit.\n");
        report.AppendLine("World bounds: "+b+"; uniform scale "+bridge.transform.localScale.x);
        report.AppendLine("Original timber deck disabled. Stone bridge always visible; purchase removes timber barricade.\nCollision follows original arched mesh, no flat invisible deck.");
        for(int i=1;i<20;i++)
        {
            float z=Mathf.Lerp(b.min.z,b.max.z,i/20f);report.AppendLine($"z={z:F3}, deck={BridgeSurface(bridge,b.center.x,z):F3}, ground={Ground(new Vector2(b.center.x,z)):F3}");
        }
        File.WriteAllText(BridgeReview+"/placement.txt",report.ToString());
    }

    public static void BridgeCapture()
    {
        Directory.CreateDirectory(BridgeReview);
        BridgeStill("01-creek-side",new Vector3(22,4,-45),new Vector3(6.7f,1.4f,-47.7f));
        BridgeStill("02-north-approach",new Vector3(6.7f,2.8f,-32),new Vector3(6.7f,2,-49));
        BridgeStill("03-south-approach",new Vector3(9,3,-63),new Vector3(6.7f,2,-46));
        var gate=Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).First(g=>g.areaName=="South Creek");
        gate.gameObject.SetActive(false);
        BridgeStill("04-open-crossing",new Vector3(6.7f,2.8f,-36),new Vector3(6.7f,2,-51));
        gate.gameObject.SetActive(true);
    }

    static void ValidateStoneBridge()
    {
        var bridge=GameObject.Find(StoneBridgeName);var b=RB(bridge);
        var gate=Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).First(g=>g.areaName=="South Creek");
        var report=new StringBuilder();int failures=0;
        void Check(bool ok,string message){report.AppendLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
        gate.gameObject.SetActive(false);Physics.SyncTransforms();
        var a=new Vector3(b.center.x,Ground(new Vector2(b.center.x,b.min.z-1)),b.min.z-1);
        var c=new Vector3(b.center.x,Ground(new Vector2(b.center.x,b.max.z+1)),b.max.z+1);
        var path=new NavMeshPath();
        bool valid=NavMesh.SamplePosition(a,out var ha,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(c,out var hc,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(ha.position,hc.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
        float length=0;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);
        Check(valid && length<30,$"Open bridge navigation path: {length:F2}m");
        foreach(float radius in new[]{.3f,.55f})foreach(int direction in new[]{-1,1})
        {
            Vector3 start=direction>0?a:c,goal=direction>0?c:a;
            var probe=new GameObject("Bridge_Physical_Probe");probe.transform.position=start+Vector3.up*.05f;
            var controller=probe.AddComponent<CharacterController>();controller.radius=radius;controller.height=1.8f;controller.center=Vector3.up*.9f;controller.stepOffset=.35f;controller.slopeLimit=45;controller.skinWidth=.025f;
            float vy=0;
            for(int frame=0;frame<600;frame++)
            {
                if(Mathf.Abs(probe.transform.position.z-goal.z)<.15f)break;
                if(controller.isGrounded&&vy<0)vy=-2;else vy-=20/60f;
                controller.Move(new Vector3(0,vy/60f,direction*3/60f));
            }
            Check(Mathf.Abs(probe.transform.position.z-goal.z)<.2f,$"Physical crossing radius {radius:F2}, direction {direction}, reached {probe.transform.position:F2}");
            Object.DestroyImmediate(probe);
        }
        gate.gameObject.SetActive(true);Physics.SyncTransforms();
        Check(gate.GetComponent<Collider>().enabled,"Closed barricade retains collider");
        Check(bridge.GetComponentsInChildren<MeshCollider>().Length>0,"Collision uses arched FBX surface");
        report.AppendLine("Failures: "+failures);File.WriteAllText(BridgeReview+"/validation.txt",report.ToString());
        if(failures>0)throw new InvalidOperationException(report.ToString());
    }

    static void CaptureAccessReview()
    {
        const string folder="Logs/MapAccessRedesign";
        BridgeStill("map-overhead",new Vector3(7,190,-6),new Vector3(7,0,-6),folder,true);
        foreach(var gate in GameObject.Find("Elderboom_Access_Redesign").GetComponentsInChildren<ElderboomGate>())
        {
            var eye=gate.transform.position-gate.transform.forward*6.5f;
            eye.y=Ground(new Vector2(eye.x,eye.z))+1.7f;
            BridgeStill(gate.name,eye,gate.transform.position,folder);
        }
    }

    static void BridgeStill(string name,Vector3 eye,Vector3 target,string folder=BridgeReview,bool overhead=false)
    {
        var go=new GameObject("Bridge_Review_Camera");var camera=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();
        camera.fieldOfView=62;camera.nearClipPlane=.1f;camera.farClipPlane=500;
        go.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye,overhead?Vector3.forward:Vector3.up));
        camera.orthographic=overhead;if(overhead)camera.orthographicSize=125;
        var rt=new RenderTexture(1400,900,24);var tex=new Texture2D(1400,900,TextureFormat.RGB24,false);camera.targetTexture=rt;
        for(int i=0;i<12;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,900),0,0);tex.Apply();
        File.WriteAllBytes(folder+"/"+name+".jpg",tex.EncodeToJPG(92));
        RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);
    }
}
