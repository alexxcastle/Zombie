using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

static partial class ElderboomClaudeBlocks
{
    const string ExpansionAssets="Assets/GameAssets/Elderboom/VillageExpansion";
    const string ExpansionRoot="Elderboom_Village_Expansion";
    const string ExpansionReview="Logs/VillageExpansion";
    static readonly string[] ExpansionIds={"apothecary","granary","cooper_workshop","smokehouse","river_parapet"};
    static readonly Vector2[] ExpansionSites={new Vector2(-26,17),new Vector2(-84,-2),new Vector2(-33,75.5f),new Vector2(36,-81)};
    static readonly float[] ExpansionYaws={180,180,90,0};
    public static void InstallVillageExpansion()
    {
        Directory.CreateDirectory(ExpansionReview);var log=new StringBuilder();
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
            if(GameObject.Find(ExpansionRoot))throw new InvalidOperationException("Expansion already installed; hand edits preserved");
            terrain=Object.FindObjectsByType<Terrain>().OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
            var root=new GameObject(ExpansionRoot).transform;
            for(int i=0;i<ExpansionIds.Length;i++)BuildExpansionPrefab(ExpansionIds[i]);
            for(int i=0;i<4;i++)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ExpansionAssets+"/"+ExpansionIds[i]+"/"+ExpansionIds[i]+".prefab");
                var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(root,true);
                go.name=ExpansionIds[i];go.transform.SetPositionAndRotation(At(ExpansionSites[i]),Quaternion.Euler(0,ExpansionYaws[i],0));
                Seat(go,ExpansionSites[i],.075f);var b=RB(go);
                ProtectExpansionFootprint(go,b,log);
                ClearLandmarkSite(b,go,log);LevelLandmarkSite(b,log);
                var door=go.transform.Find("Entrance_Approach");
                var from=new Vector2(door.position.x,door.position.z);
                Vector2 to=i==0?new Vector2(-20,19):i==1?new Vector2(-88,-27):i==2?new Vector2(-28,76):new Vector2(38,-76);
                var approachPath=i==0?new[]{from,new Vector2(-20,from.y),to}:i==1?new[]{from,new Vector2(-85,-12),new Vector2(-87,-22),to}:new[]{from,to};
                PaintLandmarkPath(approachPath,1.25f);
                ExpansionEntranceLantern(go,door);
                log.AppendLine($"{go.name} // {go.transform.position:F2} // bounds {b:F2}");
            }
            var rails=new GameObject("Riverbank_Stone_And_Spearhead_Iron").transform;rails.SetParent(root,false);
            BuildRiverParapets(rails,log);
            Physics.SyncTransforms();log.AppendLine(BakeAccessNavigation());
            ValidateExpansion(root,log);
            AssetDatabase.SaveAssets();EditorUtility.SetDirty(data);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText(ExpansionReview+"/installation.txt",log.ToString()+"\nSAVED");
            Debug.Log("[VillageExpansion] Four buildings and river parapets installed and validated.");
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(ExpansionReview+"/installation.txt",log+"\nFAIL "+e);Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void RefreshExpansionApproaches()
    {
        terrain=Object.FindObjectsByType<Terrain>().OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var root=GameObject.Find(ExpansionRoot).transform;
        var door=root.Find("apothecary/Entrance_Approach").position;
        var from=new Vector2(door.x,door.z);
        PaintLandmarkPath(new[]{from,new Vector2(-20,from.y),new Vector2(-20,19)},1.25f);
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();
    }
    static void BuildExpansionPrefab(string id)
    {
        string folder=ExpansionAssets+"/"+id;string prefabPath=folder+"/"+id+".prefab";
        if(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)!=null)return;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Source/"+id+".glb");
        if(!source)throw new InvalidOperationException("Missing optimized model "+id);
        var go=Object.Instantiate(source);go.name=id;
        try
        {
            string runtime=folder+"/Runtime";Directory.CreateDirectory(runtime);AssetDatabase.Refresh();
            ElderboomAltarIntegration.DetachRuntimeAssets(go,runtime);
            var all=go.GetComponentsInChildren<MeshRenderer>(true);
            var lod=go.AddComponent<LODGroup>();
            Renderer[] Level(string n)=>all.Where(r=>r.name==n).Cast<Renderer>().ToArray();
            if(Level("LOD0").Length!=1||Level("LOD1").Length!=1||Level("LOD2").Length!=1)throw new InvalidOperationException("LOD names invalid: "+id);
            lod.SetLODs(new[]{new LOD(.10f,Level("LOD0")),new LOD(.035f,Level("LOD1")),new LOD(.003f,Level("LOD2"))});
            lod.fadeMode=LODFadeMode.None;lod.RecalculateBounds();
            var collision=new GameObject("Visible_Geometry_Collision");collision.transform.SetParent(go.transform,false);
            var mesh=all.First(r=>r.name==(id=="river_parapet"?"LOD1":"LOD2")).GetComponent<MeshFilter>().sharedMesh;
            collision.AddComponent<MeshCollider>().sharedMesh=mesh;
            // No opaque collision slab over the gaps between iron bars.
            if(id!="river_parapet")
            {
                var b=RB(go);var approach=new GameObject("Entrance_Approach").transform;approach.SetParent(go.transform,false);
                approach.localPosition=new Vector3(0,0,b.max.z+2f);
            }
            foreach(var mat in all.SelectMany(r=>r.sharedMaterials).Distinct())if(mat){mat.enableInstancing=true;EditorUtility.SetDirty(mat);}
            PrefabUtility.SaveAsPrefabAsset(go,prefabPath);
        }
        finally{Object.DestroyImmediate(go);}
    }
    static void ProtectExpansionFootprint(GameObject go,Bounds bounds,StringBuilder log)
    {
        // Move the existing farm cart to the open loading apron if its decorations
        // overlap the new grain store; no building or gameplay objective is removed.
        if(go.name=="granary")
        {
            var cart=GameObject.Find("Elderboom_Environment_Art")?.transform.Find("Farm_Working_Yard/Abandoned_Farm_Cart");
            if(cart!=null&&bounds.Intersects(RB(cart.gameObject)))
            {
                var before=RB(cart.gameObject);var p=new Vector2(-61,-8);
                cart.position+=new Vector3(p.x-before.center.x,Ground(p)-before.min.y,p.y-before.center.z);
                log.AppendLine("Existing farm cart moved to grain-store loading apron (-61,-8)");
            }
        }
        foreach(var gate in Object.FindObjectsByType<ElderboomGate>())if(bounds.Intersects(gate.GetComponent<Collider>().bounds))throw new InvalidOperationException(go.name+" intersects "+gate.name);
        foreach(var other in Object.FindObjectsByType<LODGroup>())
        {
            if(other.transform.IsChildOf(go.transform)||other.transform==go.transform)continue;
            if(other.name.Contains("Bush")||other.name.Contains("Tree")||other.name.Contains("Alder")||other.name.Contains("Cypress"))continue;
            var rs=other.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
            var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            if(b.size.y>2.7f&&b.size.x>2.5f&&b.size.z>2.5f&&bounds.Intersects(b))throw new InvalidOperationException(go.name+" overlaps existing structure "+other.name);
        }
    }
    static void ExpansionEntranceLantern(GameObject building,Transform door)
    {
        var source=Object.FindObjectsByType<Transform>().FirstOrDefault(t=>t.name=="Forged_Lantern"
            &&t.GetComponentInChildren<Light>()!=null
            &&t.GetComponentInChildren<Light>().transform.position.y-Ground(new Vector2(t.GetComponentInChildren<Light>().transform.position.x,t.GetComponentInChildren<Light>().transform.position.z))>2.2f
            &&t.GetComponentInChildren<Light>().transform.position.y-Ground(new Vector2(t.GetComponentInChildren<Light>().transform.position.x,t.GetComponentInChildren<Light>().transform.position.z))<2.8f);
        if(source==null)return;
        var copy=Object.Instantiate(source.gameObject,building.transform,true);copy.name="Entrance_Lantern";
        copy.transform.rotation=building.transform.rotation;
        var light=copy.GetComponentInChildren<Light>();
        var target=door.position-building.transform.forward*1.7f+building.transform.right*1.25f+Vector3.up*2.45f;
        copy.transform.position+=target-light.transform.position;
        light.intensity=500;light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.range=7;
        var hd=light.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();if(hd!=null)hd.volumetricDimmer=0;
    }
    static void BuildRiverParapets(Transform root,StringBuilder log)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ExpansionAssets+"/river_parapet/river_parapet.prefab");
        var curve=CreekSamples();var bridge=GameObject.Find("SouthCreek_OldStoneBridge");
        var bridgeBounds=bridge?RB(bridge):new Bounds(new Vector3(7,1,-48),new Vector3(6,8,12));
        bridgeBounds.Expand(new Vector3(1.8f,0,1.8f));
        int count=0,bridgeGaps=0,landingGaps=0;
        // Follow the full existing watercourse, including the visible bends outside
        // the central village. One shared mesh/PBR set is reused for all panels.
        foreach(int side in new[]{-1,1})
        {
            var points=new List<Vector2>();
            for(int i=0;i<curve.Count;i++)
            {
                var p=curve[i];var tangent=(curve[Math.Min(i+1,curve.Count-1)]-curve[Math.Max(0,i-1)]).normalized;
                var normal=new Vector2(-tangent.y,tangent.x);
                float bank=4.45f+.25f*Mathf.Sin(p.x*.19f)+.16f*Mathf.Sin(p.y*.41f);
                points.Add(p+normal*bank*side);
            }
            float carry=0;Vector2 previous=points[0], panelStart=points[0];
            for(int i=1;i<points.Count;i++)
            {
                var target=points[i];var delta=target-previous;float length=delta.magnitude;
                while(carry+length>=2.25f)
                {
                    float step=2.25f-carry;var end=previous+delta.normalized*step;
                    var start=panelStart;panelStart=end;
                    var center=(start+end)*.5f;
                    bool nearBridge=center.x>bridgeBounds.min.x&&center.x<bridgeBounds.max.x&&center.y>bridgeBounds.min.z&&center.y<bridgeBounds.max.z;
                    bool nearLanding=side<0&&Vector2.Distance(center,new Vector2(25,-54))<3.6f;
                    if(nearBridge)bridgeGaps++;else if(nearLanding)landingGaps++;
                    else
                    {
                        var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.transform.SetParent(root,true);go.name="Bank_"+(side>0?"North_":"South_")+count++;
                        var a=At(start);var b=At(end);var direction=b-a;
                        go.transform.position=(a+b)*.5f-Vector3.up*.06f;
                        go.transform.rotation=Quaternion.FromToRotation(Vector3.right,direction.normalized);
                        go.transform.localScale=new Vector3((direction.magnitude+.1f)/2.4f,1,1);
                        // The local up tilts along slopes; the visible stone base is
                        // seated slightly below the bank rather than floating.
                    }
                    previous=end;delta=target-previous;length=delta.magnitude;carry=0;
                }
                carry+=length;previous=target;
            }
        }
        log.AppendLine($"River parapets: {count} panels; bridge gap panels omitted={bridgeGaps}; dock access panels omitted={landingGaps}");
        if(count<100||bridgeGaps<2||landingGaps<1)throw new InvalidOperationException("Parapet extent or crossing gap validation failed");
    }
    static void ValidateExpansion(Transform root,StringBuilder log)
    {
        int fails=0;void Check(bool ok,string text){log.AppendLine((ok?"PASS ":"FAIL ")+text);if(!ok)fails++;}
        for(int i=0;i<4;i++)
        {
            var building=root.Find(ExpansionIds[i]);var b=RB(building.gameObject);
            Check(Mathf.Abs(b.min.y-Ground(ExpansionSites[i]))<.2f,building.name+" grounded foundation");
            Check(building.GetComponent<LODGroup>().lodCount==3,building.name+" three LODs");
            Check(building.GetComponentsInChildren<MeshCollider>().Length>0,building.name+" physical geometry collision");
            var approach=building.Find("Entrance_Approach").position;approach.y=Ground(new Vector2(approach.x,approach.z));
            Check(NavMesh.SamplePosition(approach,out var hit,2.5f,NavMesh.AllAreas),building.name+" walkable doorway approach");
            foreach(var s in Object.FindObjectsByType<ElderboomStation>())Check(!b.Contains(s.transform.position),building.name+" leaves station "+s.label+" accessible");
        }
        Check(Object.FindObjectsByType<ElderboomGate>().Length==7,"seven existing access gates preserved");
        foreach(var part in Object.FindAnyObjectByType<ElderboomHorseAssembly>().parts)
            Check(NavMesh.SamplePosition(part.anchor.position,out _,2,NavMesh.AllAreas),"horse component navigation preserved: "+part.label);
        File.WriteAllText(ExpansionReview+"/validation.txt",log.ToString());
        if(fails>0)throw new InvalidOperationException("Expansion validation failures: "+fails);
    }
}
