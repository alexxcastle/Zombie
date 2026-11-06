using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

static partial class ElderboomClaudeBlocks
{
    const string LandmarkFolder="Assets/GameAssets/Elderboom/GeneratedLandmarks/";
    const string LandmarkReview="Logs/ArtReview/RecoveredLandmarks";
    const string LandmarkRoot="Elderboom_Recovered_Landmarks";

    static string BuildRecoveredLandmarks()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.isDirty)throw new InvalidOperationException("Save the scene before adding recovered landmarks.");
        Directory.CreateDirectory(LandmarkReview);
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        if(GameObject.Find(LandmarkRoot)!=null)throw new InvalidOperationException("Landmarks already exist; use landmark-review to inspect, not replace manual edits.");
        var log=new StringBuilder();
        var root=new GameObject(LandmarkRoot);Undo.RegisterCreatedObjectUndo(root,"Place recovered landmarks");
        var amph=Landmark("StoneAmphitheatre","Woodland_Stone_Amphitheatre",root.transform,new Vector2(79,77),3.35f,0,log);
        var inn=Landmark("TimberInn","SouthCreek_Timber_Inn",root.transform,new Vector2(-18,-79),9.5f,0,log);
        foreach(var go in new[]{amph,inn})
        {
            var b=RB(go);ClearLandmarkSite(b,go,log);LevelLandmarkSite(b,log);
            log.AppendLine(go.name+" bounds "+b+" transform "+go.transform.position+" scale "+go.transform.localScale);
        }
        Physics.SyncTransforms();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText(LandmarkReview+"/placement.txt",log.ToString());
        CaptureRecoveredLandmarks();
        EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    static GameObject Landmark(string asset,string name,Transform parent,Vector2 at,float height,float yaw,StringBuilder log)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(LandmarkFolder+asset+".glb");
        var collision=AssetDatabase.LoadAssetAtPath<GameObject>(LandmarkFolder+asset+"_Collision.glb");
        if(source==null||collision==null)throw new InvalidOperationException("Missing source or simplified collider for "+asset);
        var root=new GameObject(name);root.transform.SetParent(parent,false);
        var visual=Object.Instantiate(source,root.transform);visual.name="Original_Textured_Model";
        var b=RB(root);root.transform.localScale=Vector3.one*(height/b.size.y);root.transform.rotation=Quaternion.Euler(0,yaw,0);
        b=RB(root);root.transform.position+=new Vector3(at.x-b.center.x,Ground(at)-.075f-b.min.y,at.y-b.center.z);
        var coll=Object.Instantiate(collision,root.transform);coll.name="Simplified_Physical_Collision";
        foreach(var renderer in coll.GetComponentsInChildren<Renderer>())Object.DestroyImmediate(renderer);
        SolidMeshes(coll);
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if(renderer.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name.Contains("Error")))throw new InvalidOperationException("Invalid landmark material");
            log.AppendLine(name+" material "+string.Join(",",renderer.sharedMaterials.Select(m=>m.shader.name)));
        }
        return root;
    }

    static void ClearLandmarkSite(Bounds b,GameObject landmark,StringBuilder log)
    {
        var expanded=b;expanded.Expand(new Vector3(4,0,4));
        bool Inside(Vector3 p)=>p.x>expanded.min.x&&p.x<expanded.max.x&&p.z>expanded.min.z&&p.z<expanded.max.z;
        int removed=0;var o=terrain.transform.position;var sz=data.size;
        var keep=data.treeInstances.Where(t=>{bool remove=Inside(o+Vector3.Scale(t.position,sz));if(remove)removed++;return !remove;}).ToArray();
        Undo.RegisterCompleteObjectUndo(data,"Clear landmark footprint");data.SetTreeInstances(keep,true);
        // Only vegetation is removed; buildings, gates and gameplay objects must never be bulldozed automatically.
        foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if(r.transform.IsChildOf(landmark.transform)||!Inside(r.transform.position))continue;
            if(r.name.Contains("BlackAlder")||r.name.Contains("Red_Bush")){Undo.RecordObject(r.gameObject,"Clear landmark vegetation");r.gameObject.SetActive(false);removed++;}
        }
        foreach(var c in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None))if(Inside(c.transform.position))throw new InvalidOperationException("Landmark intersects access "+c.name);
        foreach(var c in Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None))if(Inside(c.transform.position))throw new InvalidOperationException("Landmark intersects station "+c.name);
        foreach(var q in Object.FindObjectsByType<ElderboomHorseAssembly>(FindObjectsSortMode.None))foreach(var p in q.parts)if(Inside(p.anchor.position))throw new InvalidOperationException("Landmark intersects horse component "+p.label);
        log.AppendLine(landmark.name+": removed "+removed+" vegetation instances inside footprint only");
    }

    static void LevelLandmarkSite(Bounds b,StringBuilder log)
    {
        int n=data.heightmapResolution;var o=terrain.transform.position;var sz=data.size;float border=3;
        int x0=Mathf.Clamp(Mathf.FloorToInt((b.min.x-border-o.x)/sz.x*(n-1)),0,n-1),z0=Mathf.Clamp(Mathf.FloorToInt((b.min.z-border-o.z)/sz.z*(n-1)),0,n-1);
        int x1=Mathf.Clamp(Mathf.CeilToInt((b.max.x+border-o.x)/sz.x*(n-1)),0,n-1),z1=Mathf.Clamp(Mathf.CeilToInt((b.max.z+border-o.z)/sz.z*(n-1)),0,n-1);
        var h=data.GetHeights(x0,z0,x1-x0+1,z1-z0+1);float level=b.min.y+.075f;
        for(int z=0;z<h.GetLength(0);z++)for(int x=0;x<h.GetLength(1);x++)
        {
            float wx=o.x+(x+x0)*sz.x/(n-1),wz=o.z+(z+z0)*sz.z/(n-1);
            float d=Mathf.Max(Mathf.Max(b.min.x-wx,wx-b.max.x),Mathf.Max(b.min.z-wz,wz-b.max.z));
            float weight=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(d/border));h[z,x]=Mathf.Lerp(h[z,x],(level-o.y)/sz.y,weight);
        }
        data.SetHeights(x0,z0,h);EditorUtility.SetDirty(data);log.AppendLine("Levelled foundation at "+level.ToString("F2")+"m, 3m blended edge");
    }

    static string CaptureRecoveredLandmarks()
    {
        Directory.CreateDirectory(LandmarkReview);
        foreach(Transform landmark in GameObject.Find(LandmarkRoot).transform)
        {
            var b=RB(landmark.gameObject);float d=Mathf.Max(b.size.x,b.size.z)*1.15f;
            for(int side=0;side<4;side++)
            {
                var direction=Quaternion.Euler(0,side*90,0)*Vector3.forward;
                var eye=b.center+direction*d+Vector3.up*b.size.y*.4f;
                BridgeStill(landmark.name+"_"+side,eye,b.center,LandmarkReview);
            }
        }
        BridgeStill("map-overhead",new Vector3(7,190,-6),new Vector3(7,0,-6),LandmarkReview,true);
        return "Landmark views captured";
    }

    static string FinishRecoveredLandmarks()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.isDirty)throw new InvalidOperationException("Save the scene before finishing landmarks.");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var root=GameObject.Find(LandmarkRoot);if(root==null)throw new InvalidOperationException("Missing landmarks");
        var amph=root.transform.Find("Woodland_Stone_Amphitheatre");var inn=root.transform.Find("SouthCreek_Timber_Inn");
        if(root.transform.Find("Approach_Dressing")!=null)throw new InvalidOperationException("Already finished; preserve manual changes.");
        Undo.RecordObject(amph,"Face amphitheatre toward woodland path");
        Vector3 center=RB(amph.gameObject).center;amph.rotation=Quaternion.Euler(0,180,0);amph.position+=center-RB(amph.gameObject).center;
        var dressing=new GameObject("Approach_Dressing").transform;dressing.SetParent(root.transform,false);
        artWood=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/weathered_brown_planks_Material.asset");
        artIron=AssetDatabase.LoadAssetAtPath<Material>(ArtFolder+"/ForgedIron.asset");
        // Domestic lighting belongs at the porch. The woodland ruin remains moonlit.
        Lantern(dressing,new Vector3(-20.8f,Ground(new Vector2(-20.8f,-71.8f))+2.2f,-71.8f),500,9);
        PaintLandmarkPath(new[]{new Vector2(-18,-72.7f),new Vector2(-15,-69),new Vector2(-11,-65),new Vector2(-9,-61),new Vector2(0,-57),new Vector2(6.7f,-54)},1.5f);
        PaintLandmarkPath(new[]{new Vector2(79,70.6f),new Vector2(79,67),new Vector2(81,62),new Vector2(81,58)},1.25f);
        Physics.SyncTransforms();
        string bake=BakeAccessNavigation();
        var log=new StringBuilder(bake+"\n");
        ValidateLandmarks(log);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText(LandmarkReview+"/validation.txt",log.ToString());
        CaptureRecoveredLandmarks();
        EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    static string TestRecoveredLandmarks()
    {
        if(GameObject.Find(LandmarkRoot)==null||EditorSceneManager.GetActiveScene().name!="Elderboom_Unity_Port")throw new InvalidOperationException("Unexpected scene for landmark validation");
        // Capture cameras were temporary objects; their creation/destruction marks the scene dirty.
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        ElderboomMapValidation.Run();
        return "Saved landmark scene and started passage Play Mode validation";
    }

    static void PaintLandmarkPath(Vector2[] points,float radius)
    {
        int n=data.alphamapResolution;var o=terrain.transform.position;var size=data.size;
        float minX=points.Min(p=>p.x)-radius-1,maxX=points.Max(p=>p.x)+radius+1,minZ=points.Min(p=>p.y)-radius-1,maxZ=points.Max(p=>p.y)+radius+1;
        int x0=Mathf.Clamp(Mathf.FloorToInt((minX-o.x)/size.x*n),0,n-1),z0=Mathf.Clamp(Mathf.FloorToInt((minZ-o.z)/size.z*n),0,n-1);
        int x1=Mathf.Clamp(Mathf.CeilToInt((maxX-o.x)/size.x*n),0,n-1),z1=Mathf.Clamp(Mathf.CeilToInt((maxZ-o.z)/size.z*n),0,n-1);
        var alpha=data.GetAlphamaps(x0,z0,x1-x0+1,z1-z0+1);int layer=Mathf.Min(5,alpha.GetLength(2)-1);
        Undo.RegisterCompleteObjectUndo(data,"Landmark approach ground");
        for(int z=0;z<alpha.GetLength(0);z++)for(int x=0;x<alpha.GetLength(1);x++)
        {
            var p=new Vector2(o.x+(x+x0+.5f)*size.x/n,o.z+(z+z0+.5f)*size.z/n);float d=float.MaxValue;
            for(int k=1;k<points.Length;k++){var a=points[k-1];var v=points[k]-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,v)/v.sqrMagnitude);d=Mathf.Min(d,Vector2.Distance(p,a+v*t));}
            float edge=radius*(.90f+.15f*Mathf.PerlinNoise(p.x*.7f,p.y*.7f));float weight=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(edge*.45f,edge+1,d)))*.87f;
            for(int l=0;l<alpha.GetLength(2);l++)alpha[z,x,l]=Mathf.Lerp(alpha[z,x,l],l==layer?1:0,weight);
        }
        data.SetAlphamaps(x0,z0,alpha);EditorUtility.SetDirty(data);
    }

    static void ValidateLandmarks(StringBuilder log)
    {
        int failures=0;
        void Check(bool ok,string message){log.AppendLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
        var root=GameObject.Find(LandmarkRoot);
        var player=Object.FindAnyObjectByType<HDRPSlicePlayer>();
        bool Route(Vector3 target)
        {
            if(!NavMesh.SamplePosition(player.transform.position,out var start,3,NavMesh.AllAreas)||!NavMesh.SamplePosition(target,out var end,2,NavMesh.AllAreas))return false;
            var path=new NavMeshPath();return NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
        }
        foreach(Transform landmark in root.transform)
        {
            if(landmark.name=="Approach_Dressing")continue;
            var b=RB(landmark.gameObject);bool amph=landmark.name.Contains("Amphitheatre");
            Check(landmark.GetComponentsInChildren<MeshCollider>().Length>0,landmark.name+" physical mesh collision");
            Check(Mathf.Abs(b.min.y-(Ground(new Vector2(b.center.x,b.center.z))-.075f))<.12f,landmark.name+" foundation seated on terrain");
            var approach=new Vector3(b.center.x,Ground(new Vector2(b.center.x,amph?b.min.z-2:b.max.z+2)),amph?b.min.z-2:b.max.z+2);
            Check(Route(approach),landmark.name+" entrance approach reachable on baked navigation");
            foreach(var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None))Check(!b.Intersects(gate.GetComponent<Collider>().bounds),landmark.name+" clear of "+gate.name);
            foreach(var mf in landmark.GetComponentsInChildren<MeshFilter>())if(mf.GetComponent<Renderer>()!=null)
                Check(mf.GetComponent<Renderer>().sharedMaterials.All(m=>m!=null&&m.shader!=null&&!m.shader.name.Contains("Error")),landmark.name+" PBR material valid");
        }
        var quest=Object.FindAnyObjectByType<ElderboomHorseAssembly>();
        foreach(var part in quest.parts)Check(Route(part.anchor.position),"horse component remains reachable: "+part.label);
        foreach(var spawn in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None))Check(Route(spawn.transform.position),"spawn remains connected: "+spawn.name);
        Check(Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).Length==7,"seven passages preserved");
        log.AppendLine("Failures: "+failures);
        File.WriteAllText(LandmarkReview+"/validation.txt",log.ToString());
        if(failures>0)throw new InvalidOperationException("Landmark validation failures: "+failures+"; inspect report before saving.");
    }
}
