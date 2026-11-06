using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class ElderboomChurchFloorReview
{
    const string Folder="Logs/ArtDirection/ChurchFloor";
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");Directory.CreateDirectory(Folder);
        var church=GameObject.Find("FallenCity_Church_HumanScale");var terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();var log=new StringBuilder();
        foreach(var r in church.GetComponentsInChildren<Renderer>())
            log.AppendLine(r.name+" bounds="+r.bounds+" minY="+r.bounds.min.y.ToString("F4")+" maxY="+r.bounds.max.y.ToString("F4")+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"NULL")));
        foreach(var p in new[]{new Vector3(-24,0,-12),new Vector3(-22,0,-12),new Vector3(0,0,8),new Vector3(0,0,10),new Vector3(48,0,-12)})
            log.AppendLine("Exterior terrain at "+p+" = "+(terrain.SampleHeight(p)+terrain.transform.position.y).ToString("F4"));
        File.WriteAllText(Folder+"/inspection.txt",log.ToString());
        Capture("before");
    }
    static void Capture(string suffix)
    {
        ElderboomBlockArtDirection.Render(new ElderboomBlockArtDirection.View{id="church_front",eye=new Vector3(-34,3.4f,-4),target=new Vector3(-21.5f,1.6f,-12.7f),size=3.4f},"../ChurchFloor/front_"+suffix);
        ElderboomBlockArtDirection.Render(new ElderboomBlockArtDirection.View{id="church_side",eye=new Vector3(-9,3.4f,17),target=new Vector3(0,1.35f,7),size=3},"../ChurchFloor/side_"+suffix);
    }
    public static void LowerAndVerify()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");Directory.CreateDirectory(Folder);
        var church=GameObject.Find("FallenCity_Church_HumanScale");
        if(church.transform.Find("Floor_Seating_Adjusted_22cm"))throw new InvalidOperationException("Floor seating already adjusted");
        var floor=church.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Circle.051");
        float previous=floor.bounds.max.y;church.transform.position+=Vector3.down*.22f;
        var marker=new GameObject("Floor_Seating_Adjusted_22cm");marker.transform.SetParent(church.transform,false);
        Physics.SyncTransforms();
        File.WriteAllText(Folder+"/adjustment.txt",$"Church root lowered 0.220m to preserve structural floor/column alignment. Floor surface {previous:F4} -> {floor.bounds.max.y:F4}m. Exterior terrain 0.9106m; remaining threshold {floor.bounds.max.y-.9106f:F4}m. Scale unchanged.\n");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        ElderboomClaudeBlocks.RebakeChurchFloorNavigation();
        Capture("after");Debug.Log("[ChurchFloor] Lowered 22cm and visually captured");
    }
}

static partial class ElderboomClaudeBlocks
{
    public static void RebakeChurchFloorNavigation()
    {
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        string result=BakeAccessNavigation();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        File.AppendAllText("Logs/ArtDirection/ChurchFloor/adjustment.txt",result);
    }
}
