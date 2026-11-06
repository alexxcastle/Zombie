using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using TMPro;
using Object = UnityEngine.Object;

static partial class ElderboomClaudeBlocks
{
    static void AddCombatDressing(Transform root, GameObject[] rocks, GameObject pile, GameObject wall, StringBuilder log)
    {
        var parent = new GameObject("Combat_Islands").transform; parent.SetParent(root);
        // Separated islands leave wide alternate routes on both sides.
        var sites = new[] { W(-73,-27), W(-79,8), W(-26,59), W(8,74), W(72,-21), W(68,53), W(84,72), W(-25,-81), W(31,-85) };
        int count = 0;
        foreach (var site in sites)
        {
            for (int i=0;i<3;i++)
            {
                var p = site + new Vector2((i-1)*3.6f, i==1?2.8f:0);
                if (PassDist(p)<12) continue;
                var src = site.x<C.x && pile!=null ? pile : rocks.Length>0 ? rocks[(count+i)%rocks.Length] : wall;
                if(src==null)continue;
                var go=Object.Instantiate(src,parent);go.name="Cover_Island_"+count++;
                foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                go.transform.rotation=Quaternion.Euler(0,25+i*37,0);
                var b=RB(go);go.transform.localScale*=1.8f/Mathf.Max(.1f,b.size.y);
                b=RB(go);SetCenter(go,new Vector3(p.x,Ground(p)+b.size.y*.5f-.12f,p.y));
                foreach(var mf in go.GetComponentsInChildren<MeshFilter>())
                    if(mf.sharedMesh!=null)mf.gameObject.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;
            }
        }
        log.AppendLine("combat cover pieces "+count);
    }

    static void AddWayfinding(Transform root, Transform passages)
    {
        var cues=new GameObject("Route_Lights").transform;cues.SetParent(root);
        foreach(var gate in passages.GetComponentsInChildren<ElderboomGate>())
        {
            var pos=gate.transform.position+Vector3.up*2.55f;
            var wood=WoodMaterial();
            foreach(float side in new[]{-1f,1f})
            {
                var post=GameObject.CreatePrimitive(PrimitiveType.Cylinder);post.name="Passage_Lantern_Post";post.transform.SetParent(cues);
                post.transform.position=gate.transform.position+gate.transform.right*(side*3.4f)+Vector3.up*.4f;post.transform.localScale=new Vector3(.16f,1.8f,.16f);
                if(wood!=null)post.GetComponent<Renderer>().sharedMaterial=wood;
            }
            var beam=GameObject.CreatePrimitive(PrimitiveType.Cube);beam.name="Passage_Sign_Beam";beam.transform.SetParent(cues);
            beam.transform.SetPositionAndRotation(gate.transform.position+Vector3.up*2.2f,gate.transform.rotation);beam.transform.localScale=new Vector3(7,.16f,.16f);
            if(wood!=null)beam.GetComponent<Renderer>().sharedMaterial=wood;
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            AddCueLight(cues,pos,1100,9);
            var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="Destination_Board";board.transform.SetParent(cues);
            board.transform.SetPositionAndRotation(pos-Vector3.up*.55f,gate.transform.rotation);board.transform.localScale=new Vector3(3.1f,.48f,.14f);
            if(wood!=null)board.GetComponent<Renderer>().sharedMaterial=wood;
            Object.DestroyImmediate(board.GetComponent<Collider>());
            var sign=new GameObject("Destination_"+gate.areaName);sign.transform.SetParent(cues);
            sign.transform.position=board.transform.position-gate.transform.forward*.08f;sign.transform.rotation=gate.transform.rotation;
            var text=sign.AddComponent<TextMeshPro>();
            text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TerrainDemoScene_HDRP/TextMeshPro/Fonts/InterBold.asset");
            const string signMatPath="Assets/Materials/Elderboom_Route_Text.mat";
            var signMat=AssetDatabase.LoadAssetAtPath<Material>(signMatPath);
            if(signMat==null){signMat=new Material(Shader.Find("Elderboom/RouteText"));AssetDatabase.CreateAsset(signMat,signMatPath);}
            signMat.shader=Shader.Find("Elderboom/RouteText");signMat.SetTexture("_MainTex",text.font.atlasTexture);signMat.SetColor("_FaceColor",Color.white);EditorUtility.SetDirty(signMat);
            text.fontSharedMaterial=signMat;text.text=gate.areaName.ToUpperInvariant();text.fontSize=3;text.enableAutoSizing=true;text.fontSizeMin=1;text.fontSizeMax=3;
            text.rectTransform.sizeDelta=new Vector2(2.9f,.4f);text.alignment=TextAlignmentOptions.Center;text.color=new Color(1,.8f,.45f);text.ForceMeshUpdate();
            var reverse=Object.Instantiate(sign,cues);reverse.name=sign.name+"_Reverse";reverse.transform.rotation*=Quaternion.Euler(0,180,0);reverse.transform.position=board.transform.position+gate.transform.forward*.08f;
            BakeSignMesh(text);
            BakeSignMesh(reverse.GetComponent<TextMeshPro>());
        }
        foreach(var station in Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None))
            AddCueLight(cues,station.transform.position+Vector3.up*2.6f,500,6);
        foreach(var lamp in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(lamp.name=="Warm_Lamp_Light")
            {
                lamp.shadows=LightShadows.Soft;
                var hd=lamp.GetComponent<HDAdditionalLightData>();
                if(hd!=null){hd.shadowResolution.level=0;hd.shadowFadeDistance=24f;EditorUtility.SetDirty(hd);}
                EditorUtility.SetDirty(lamp);
            }
    }

    static void BakeSignMesh(TextMeshPro text)
    {
        // These labels are static scenery. Persist their geometry instead of relying
        // on an editor canvas update to regenerate TMP meshes after reopening a scene.
        text.ForceMeshUpdate(true,true);
        var mesh=Object.Instantiate(text.mesh);mesh.name=text.name;NormalizeSignLod(mesh);
        string folder="Assets/Generated/MapSigns";Directory.CreateDirectory(folder);
        string path=folder+"/"+text.name.Replace(' ','_')+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
        else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
        var go=text.gameObject;var material=text.fontSharedMaterial;
        Object.DestroyImmediate(text);
        var mf=go.GetComponent<MeshFilter>();if(mf==null)mf=go.AddComponent<MeshFilter>();mf.hideFlags=HideFlags.None;mf.sharedMesh=saved;
        var renderer=go.GetComponent<MeshRenderer>();if(renderer==null)renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void NormalizeSignLod(Mesh mesh)
    {
        mesh.lodCount=2;
        for(int i=0;i<mesh.subMeshCount;i++)
        {
            var sub=mesh.GetSubMesh(i);
            var range=new MeshLodRange{indexStart=(uint)sub.indexStart,indexCount=(uint)sub.indexCount};
            mesh.SetLod(i,0,range);mesh.SetLod(i,1,range);
        }
        mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    }
    static void AddCueLight(Transform parent,Vector3 at,float intensity,float range)
    {
        var go=new GameObject("Warm_Destination_Light");go.transform.SetParent(parent);go.transform.position=at;
        var l=go.AddComponent<Light>();l.type=LightType.Point;go.AddComponent<HDAdditionalLightData>();l.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;
        l.intensity=intensity;l.range=range;l.color=new Color(1,.67f,.36f);l.shadows=LightShadows.Soft;
        go.GetComponent<HDAdditionalLightData>().shadowResolution.level=0;
        go.GetComponent<HDAdditionalLightData>().shadowFadeDistance=24f;
        var bulb=GameObject.CreatePrimitive(PrimitiveType.Sphere);bulb.name="Lantern_Glass";bulb.transform.SetParent(go.transform,false);bulb.transform.localScale=Vector3.one*.16f;
        Object.DestroyImmediate(bulb.GetComponent<Collider>());
        const string path="Assets/Materials/Elderboom_Route_Lantern.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null)
        {
            mat=new Material(Shader.Find("HDRP/Lit"));mat.SetColor("_BaseColor",new Color(1,.67f,.36f));mat.SetColor("_EmissiveColor",new Color(12,6,2));
            AssetDatabase.CreateAsset(mat,path);
        }
        var renderer=bulb.GetComponent<Renderer>();renderer.sharedMaterial=mat;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void ClearGeneratedApproaches(Transform root,StringBuilder log)
    {
        Physics.SyncTransforms();var borders=root.Find("Borders");
        foreach(var gate in root.GetComponentsInChildren<ElderboomGate>())
        {
            for(float along=-4.2f;along<=4.2f;along+=.4f)
            {
                var p=gate.transform.position+gate.transform.forward*along;p.y=Ground(new Vector2(p.x,p.z));
                foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.65f,p+Vector3.up*1.3f,.7f,~0,QueryTriggerInteraction.Ignore))
                {
                    if(c==null || !c.transform.IsChildOf(borders))continue;
                    var piece=c.transform;
                    while(piece.parent!=null && piece.parent.parent!=borders)piece=piece.parent;
                    if(piece==borders || piece.parent==null)continue;
                    log.AppendLine("removed generated border piece from approach: "+piece.name+" at "+piece.position);
                    Object.DestroyImmediate(piece.gameObject);Physics.SyncTransforms();
                }
            }
        }
    }

    public static void FinalizeRevision()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var root=GameObject.Find(RootName).transform;var log=new StringBuilder();
        foreach(var pass in Passes)
        {
            var gate=root.GetComponentsInChildren<ElderboomGate>().First(g=>g.areaName==pass.area&&g.cost==pass.cost);
            pass.at=new Vector2(gate.transform.position.x,gate.transform.position.z);
        }
        ClearGeneratedApproaches(root,log);log.AppendLine(Bake());
        foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            if(renderer.name=="Lantern_Glass")renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach(var text in root.GetComponentsInChildren<TextMeshPro>().ToArray())BakeSignMesh(text);
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>().ToArray())
        {
            if(!renderer.name.StartsWith("Destination_") || renderer.name=="Destination_Board")continue;
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Generated/MapSigns/"+renderer.name.Replace(' ','_')+".asset");
            if(mesh==null || mesh.vertexCount==0)throw new InvalidOperationException("Missing sign geometry: "+renderer.name);
            NormalizeSignLod(mesh);
            var mf=renderer.GetComponent<MeshFilter>();if(mf==null)mf=renderer.gameObject.AddComponent<MeshFilter>();
            mf.hideFlags=HideFlags.None;mf.sharedMesh=mesh;
            if(renderer.transform is RectTransform rect)
            {
                var plain=new GameObject(renderer.name);plain.transform.SetParent(rect.parent,false);
                plain.transform.localPosition=new Vector3(rect.anchoredPosition.x,rect.anchoredPosition.y,rect.localPosition.z);
                plain.transform.localRotation=rect.localRotation;plain.transform.localScale=rect.localScale;
                plain.AddComponent<MeshFilter>().sharedMesh=mesh;
                var replacement=plain.AddComponent<MeshRenderer>();replacement.sharedMaterial=renderer.sharedMaterial;replacement.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                Object.DestroyImmediate(renderer.gameObject);
            }
        }
        var textMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Elderboom_Route_Text.mat");
        textMaterial.shader=Shader.Find("Elderboom/RouteText");textMaterial.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TerrainDemoScene_HDRP/TextMeshPro/Fonts/InterBold.asset").atlasTexture);
        textMaterial.SetColor("_FaceColor",Color.white);textMaterial.renderQueue=3000;EditorUtility.SetDirty(textMaterial);
        var boards=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Destination_Board").ToArray();
        foreach(var label in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Destination_")&&r.name!="Destination_Board"))
        {
            var board=boards.OrderBy(b=>(b.position-label.transform.position).sqrMagnitude).First();
            bool reverse=label.name.EndsWith("_Reverse");
            label.transform.SetPositionAndRotation(board.position+board.forward*(reverse?.12f:-.12f),board.rotation*Quaternion.Euler(0,reverse?180:0,0));
            label.sharedMaterial=textMaterial;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        foreach(var renderer in GameObject.Find(RootName).GetComponentsInChildren<MeshRenderer>())
            if(renderer.name.StartsWith("Destination_") && renderer.GetComponent<MeshFilter>()?.sharedMesh==null)
                throw new InvalidOperationException("Sign mesh was not saved: "+renderer.name);
        File.WriteAllText("Logs/MapRevision/finalize.txt",log.ToString());File.WriteAllText("Logs/MapRevision/captures.txt",RunBuild("shots"));
    }

    public static void FinalizeAndBuild()
    {
        FinalizeRevision();
        // Capture cameras are temporary and never belong in the standalone scene.
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        HDRPSliceStandaloneBuild.BuildPort();
    }

    static void FitCemeteryApproach(StringBuilder log)
    {
        var pass=Passes.First(p=>p.area=="East Cemetery");
        var options=Enumerable.Range(-40,80).Select(i=>i*.5f).OrderBy(z=>Mathf.Abs(z));
        foreach(float z in options)
        {
            var at=W(28.5f,z);var blockers=new System.Collections.Generic.HashSet<Collider>();
            for(float x=-4;x<=4;x+=.5f)
            {
                var p=at+Vector2.right*x;var ground=new Vector3(p.x,Ground(p),p.y);
                var hits=Physics.OverlapCapsule(ground+Vector3.up*.65f,ground+Vector3.up*1.3f,.65f,~0,QueryTriggerInteraction.Ignore);
                foreach(var hit in hits)if(!(hit is TerrainCollider))blockers.Add(hit);
            }
            bool movable=blockers.All(c=>c.name.Contains("Old_Wooden_Bench") || c.name.Contains("Mossy_Stone_Wall") || c.name.Contains("Modular_Fence"));
            log.AppendLine("approach candidate z="+z+": "+string.Join(", ",blockers.Select(c=>c.name)));
            if(!movable || blockers.Count>3)continue;
            foreach(var c in blockers){log.AppendLine("disabled approach prop "+c.name);c.gameObject.SetActive(false);}
            pass.at=at;log.AppendLine("cemetery approach fits horse corridor at "+at);return;
        }
        Directory.CreateDirectory("Logs/MapRevision");File.WriteAllText("Logs/MapRevision/approach-diagnostics.txt",log.ToString());
        throw new InvalidOperationException("No unobstructed cemetery approach found; inspect original village colliders.");
    }

    public static void RevisionBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        Directory.CreateDirectory("Logs/MapRevision");
        File.WriteAllText("Logs/MapRevision/build.txt",RunBuild("build"));
        FinalizeRevision();
        File.WriteAllText("Logs/MapRevision/survey.txt",Survey());
    }
}
