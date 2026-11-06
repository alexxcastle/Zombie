using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

public static class ElderboomBlockArtDirection
{
    public const string Folder="Logs/ArtDirection/Blocks";
    [Serializable] public class View { public string id,area; public Vector3 target,eye; public float size; }
    [Serializable] class Manifest { public View[] views; }
    [Serializable] class Item { public string name,path;public Vector3 min,max; }
    [Serializable] class Survey { public Item[] renderers; }
    public static void SurveyScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
        var items=Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&!r.name.Contains("Bush")&&!r.name.Contains("Alder")&&!r.name.Contains("Grass")).Select(r=>new Item{name=r.name,path=PathOf(r.transform),min=r.bounds.min,max=r.bounds.max}).ToArray();
        File.WriteAllText("Logs/ArtDirection/current-scene-survey.json",JsonUtility.ToJson(new Survey{renderers=items},true));
        Debug.Log("[ArtReconstruction] Surveyed "+items.Length+" renderers.");
    }
    public static void CaptureSavedBlock()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        int index=int.Parse(Environment.GetEnvironmentVariable("ELDERBOOM_ART_VIEW")??"0");
        if(index<0){foreach(var v in Views){Render(v,v.id+"_after");EditorUtility.UnloadUnusedAssetsImmediate();}Debug.Log("[ArtReconstruction] Six comparison captures complete");return;}
        Render(Views[index],Views[index].id+"_after");
        Debug.Log("[ArtReconstruction] Captured saved block "+Views[index].id);
    }
    public static readonly View[] Views={
        new View{id="01_village",area="Village Square",target=new Vector3(2,2,-7),eye=new Vector3(2,125,66),size=65},
        new View{id="02_farm",area="West Farm",target=new Vector3(-73,1,-20),eye=new Vector3(-135,100,-75),size=57},
        new View{id="03_sawmill",area="North Timber Yard",target=new Vector3(-17,2,62),eye=new Vector3(-75,110,125),size=57},
        new View{id="04_cemetery",area="East Cemetery",target=new Vector3(80,2,-17),eye=new Vector3(145,108,-60),size=58},
        new View{id="05_creek",area="South Creek",target=new Vector3(7,1,-80),eye=new Vector3(30,108,-145),size=62},
        new View{id="06_woods",area="Outer Woods",target=new Vector3(80,2,60),eye=new Vector3(143,110,120),size=57}
    };
    public static void Capture()
    {
        if(!Application.isBatchMode&&EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save scene before art direction capture");
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder+"/cameras.json",JsonUtility.ToJson(new Manifest{views=Views},true));
        foreach(var v in Views){Render(v,v.id+"_before",false);Render(v,v.id+"_reverse_before",true);}
        Debug.Log("[BlockArtDirection] Twelve source captures complete; scene not saved or modified");
    }
    public static void Render(View v,string file,bool reverse=false)
    {
        var go=new GameObject("ArtDirection_ReviewCamera");var camera=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();
        camera.orthographic=true;camera.orthographicSize=v.size;camera.nearClipPlane=.1f;camera.farClipPlane=600;
        var eye=v.eye;if(reverse){var d=eye-v.target;eye=v.target+new Vector3(-d.x,d.y,-d.z);}
        go.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(v.target-eye));
        var rt=new RenderTexture(1536,1024,24);var tex=new Texture2D(1536,1024,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{camera.targetTexture=rt;for(int i=0;i<3;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1536,1024),0,0);tex.Apply();File.WriteAllBytes(Folder+"/"+file+".jpg",tex.EncodeToJPG(94));}
        finally{RenderTexture.active=old;camera.targetTexture=null;Object.DestroyImmediate(go);Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
    }
}
