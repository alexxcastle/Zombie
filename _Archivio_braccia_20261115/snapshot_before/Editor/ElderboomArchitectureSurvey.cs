using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

// Explicit request file executes once in the already-open Editor, without saving the scene.
public static class ElderboomArchitectureSurvey
{
    const string Folder="Logs/ArchitectureKit";
    const string Request=Folder+"/survey.request";
    [Serializable] class Survey {public string scene;public string captureLighting;public List<Gate> gates=new List<Gate>();}
    [Serializable] class Gate {public string name,area,otherArea,sideA,sideB;public int cost;public Vector3 position,euler,scale,boxSize,boxCenter;public string[] neighbours;}
    [InitializeOnLoadMethod] static void Schedule(){EditorApplication.delayCall+=Requested;}
    static void Requested()
    {
        if(!File.Exists(Request))return;
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.delayCall+=Requested;return;}
        if(File.Exists(Folder+"/survey.running"))File.Delete(Folder+"/survey.running");
        File.Move(Request,Folder+"/survey.running");
        try{Capture();if(File.Exists(Folder+"/survey.complete"))File.Delete(Folder+"/survey.complete");File.Move(Folder+"/survey.running",Folder+"/survey.complete");}
        catch(Exception e){File.WriteAllText(Folder+"/survey-error.txt",e.ToString());Debug.LogException(e);}
    }
    [MenuItem("Tools/Elderboom/Architecture Kit/Capture Seven Gate Approaches")]
    public static void Capture()
    {
        Directory.CreateDirectory(Folder);var current=SceneManager.GetActiveScene();Scene loaded=default;
        var scene=current;
        if(!current.path.EndsWith("Elderboom_Unity_Port.unity"))
        {loaded=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity",OpenSceneMode.Additive);scene=loaded;}
        var allGates=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ElderboomGate>(true)).ToArray();
        File.WriteAllLines(Folder+"/gate-inventory.txt",allGates.Select(g=>g.name+" active="+g.gameObject.activeInHierarchy+" enabled="+g.enabled+" area="+g.areaName+" cost="+g.cost));
        var gates=allGates.Where(g=>g.gameObject.activeInHierarchy&&g.enabled&&g.name.StartsWith("Access_")).OrderBy(g=>g.name).ToArray();
        if(gates.Length!=7)throw new Exception("Expected seven active gates, got "+gates.Length);
        var temporary=new GameObject("ArchitectureSurvey_TEMP");SceneManager.MoveGameObjectToScene(temporary,scene);
        var cameraGo=new GameObject("CaptureCamera");cameraGo.transform.SetParent(temporary.transform);var camera=cameraGo.AddComponent<Camera>();
        var hd=cameraGo.AddComponent<HDAdditionalCameraData>();hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camera.fieldOfView=65;camera.nearClipPlane=.08f;camera.farClipPlane=160;
        var volumeGo=new GameObject("InspectionExposure");volumeGo.transform.SetParent(temporary.transform);var volume=volumeGo.AddComponent<Volume>();volume.priority=1000;volume.isGlobal=true;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
        var ex=profile.Add<Exposure>(true);ex.mode.Override(ExposureMode.Fixed);ex.fixedExposure.Override(6.5f);
        profile.Add<MotionBlur>(true).intensity.Override(0);profile.Add<DepthOfField>(true).focusMode.Override(DepthOfFieldMode.Off);
        var fillGo=new GameObject("InspectionFill");fillGo.transform.SetParent(temporary.transform);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;fillGo.AddComponent<HDAdditionalLightData>();fill.intensity=200;fillGo.transform.rotation=Quaternion.Euler(50,-30,0);
        var survey=new Survey{scene=scene.path,captureLighting="Temporary inspection exposure EV6.5 and 200 lux fill; scene geometry unchanged, not saved."};
        try
        {
            foreach(var gate in gates)
            {
                var col=gate.GetComponent<BoxCollider>();var bounds=col!=null?col.bounds:new Bounds(gate.transform.position,Vector3.one*3);
                // Gate builder places its root 1.35m above the walkable floor; terrain
                // sampling here would put the bridge camera under the bridge deck.
                Vector3 floor=gate.transform.position-Vector3.up*1.35f;
                Vector3 a=floor-gate.transform.forward*8,b=floor+gate.transform.forward*8;
                string Area(Vector3 p)=>Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None).FirstOrDefault(x=>x.gameObject.scene==scene&&x.Contains(p))?.areaName??"Unknown";
                var entry=new Gate{name=gate.name,area=gate.areaName,otherArea=gate.otherAreaName,cost=gate.cost,position=gate.transform.position,euler=gate.transform.eulerAngles,scale=gate.transform.lossyScale,
                  boxSize=col!=null?col.size:Vector3.zero,boxCenter=col!=null?col.center:Vector3.zero,sideA=Area(a),sideB=Area(b),
                  neighbours=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.gameObject.scene==scene&&r.gameObject.activeInHierarchy&&Vector3.Distance(r.bounds.ClosestPoint(floor),floor)<12).Select(r=>r.transform.parent!=null?r.transform.parent.name+"/"+r.name:r.name).Distinct().Take(90).ToArray()};
                survey.gates.Add(entry);
                Shot(camera,a+Vector3.up*1.65f,floor+Vector3.up*1.3f,gate.name+"_A");
                Shot(camera,b+Vector3.up*1.65f,floor+Vector3.up*1.3f,gate.name+"_B");
                Shot(camera,floor-gate.transform.forward*4.5f+Vector3.up*1.65f,floor+Vector3.up*1.3f,gate.name+"_nearA");
                Shot(camera,floor+gate.transform.forward*4.5f+Vector3.up*1.65f,floor+Vector3.up*1.3f,gate.name+"_nearB");
                Shot(camera,floor-gate.transform.forward*12+gate.transform.right*8+Vector3.up*7,floor+Vector3.up,gate.name+"_context");
                Debug.Log("GATE_SURVEY "+gate.name+" "+entry.sideA+" -> "+entry.sideB);
            }
            File.WriteAllText(Folder+"/gates.json",JsonUtility.ToJson(survey,true));
        }
        finally{Object.DestroyImmediate(temporary);Object.DestroyImmediate(profile);if(loaded.IsValid())EditorSceneManager.CloseScene(loaded,true);SceneManager.SetActiveScene(current);}
    }
    static void Shot(Camera camera,Vector3 eye,Vector3 target,string name)
    {
        camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
        var rt=new RenderTexture(1280,800,24);var tex=new Texture2D(1280,800,TextureFormat.RGB24,false);camera.targetTexture=rt;
        for(int i=0;i<3;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,800),0,0);tex.Apply();
        File.WriteAllBytes(Folder+"/"+name+".jpg",tex.EncodeToJPG(92));RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
    }
}
