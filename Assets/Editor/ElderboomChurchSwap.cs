using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

static partial class ElderboomClaudeBlocks
{
    const string ChurchModelPath = "Assets/GameAssets/Ported/FallenCityChurch/church.gltf";
    const string ChurchRootName = "FallenCity_Church_HumanScale";
    const string ChurchReview = "Logs/ArtReview/Church";

    // The original Godot scene used 4x on the portable GLB. Keep this uniformly:
    // the building must fit the people, rather than shrinking the people-sized doors.
    static GameObject InstantiateHumanScaleChurch()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ChurchModelPath);
        if (!source) throw new InvalidOperationException("Missing Fallen City church GLB");
        var church = Object.Instantiate(source);
        church.name = ChurchRootName;
        // Its rendered centre aligns with the retired chapel's former centre.
        church.transform.SetPositionAndRotation(new Vector3(-21.58f, 1.18f, -12.70f), Quaternion.Euler(0,180,0));
        church.transform.localScale = Vector3.one * 4;
        return church;
    }

    static bool ChurchSite(Vector3 p,float pad=0) =>
        p.x>-27-pad && p.x<54+pad && p.z>-38-pad && p.z<13+pad;
    static bool ChurchSite(Bounds b) =>
        b.Intersects(new Bounds(new Vector3(13.5f,18,-12.7f),new Vector3(81,44,51)));
    static float ChurchBoundaryShift(float z) => 18*Smooth(-36,-27,z)*(1-Smooth(5,12,z));

    public static void ChurchBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        RestoreFromProvisionalChurchPlacement();
        // Rebuild the authored pass from its terrain baseline so the old westward
        // test leaves neither a flat patch nor disabled village remnants behind.
        ArtBuild();
    }

    // The church forecourt moved the cemetery boundary east. The gate must live
    // in that same boundary opening or the old position becomes an unlocked
    // route around its collider.
    [MenuItem("Elderboom HDRP/Repair Church Cemetery Gate")]
    public static void RepairChurchCemeteryGate()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before moving the cemetery gate.");
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Elderboom_Unity_Port.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();
        data=terrain.terrainData;
        var gate=Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .FirstOrDefault(g=>g.areaName=="East Cemetery"&&g.cost==1000);
        if(!gate)throw new InvalidOperationException("East Cemetery gate not found.");
        var p=gate.transform.position;
        if(!gate.name.EndsWith("_ChurchSiteShift"))
        {
            p.x+=ChurchBoundaryShift(p.z);
            p.y=Ground(new Vector2(p.x,p.z))+1.4f;
            gate.transform.position=p;gate.name+="_ChurchSiteShift";
        }
        Physics.SyncTransforms();
        File.WriteAllText(ChurchReview+"/cemetery-gate.txt","Gate moved to "+gate.transform.position+".\n"+Bake());
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject=gate.gameObject;
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("East Cemetery gate moved with church boundary and navigation rebuilt.");
    }

    static void RestoreFromProvisionalChurchPlacement()
    {
        var root=GameObject.Find(ChurchRootName);if(root)Object.DestroyImmediate(root);
        var port=GameObject.Find("Elderboom_Village_Port");
        if(port)foreach(Transform item in port.transform)
            if(!item.name.StartsWith("S_Grass_Clumps"))item.gameObject.SetActive(true);
        var chapel=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(t=>t.name=="CentralChapel_Scale_1_25");
        if(chapel)chapel.gameObject.SetActive(true);
        var artRoot=GameObject.Find("Elderboom_Environment_Art");
        var shifted=artRoot?artRoot.transform.Find("Physical_Boundary_Banks"):null;
        if(shifted)foreach(Transform item in shifted)
            if(item.name.EndsWith("_ChurchSiteShift")){item.position-=Vector3.right*ChurchBoundaryShift(item.position.z);item.name=item.name.Replace("_ChurchSiteShift","");}
        var blocks=GameObject.Find(RootName);
        if(blocks)foreach(var item in blocks.GetComponentsInChildren<Transform>(true))
            if(item.name.EndsWith("_ChurchSiteShift")){item.position-=Vector3.right*ChurchBoundaryShift(item.position.z);item.name=item.name.Replace("_ChurchSiteShift","");}
        foreach(var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if(!mf.name.StartsWith("Road_"))continue;
            var original=AssetDatabase.LoadAssetAtPath<Mesh>(ArtFolder+"/ChurchRoadOriginal_"+mf.name+".asset");
            if(!original)continue;mf.sharedMesh=original;var collider=mf.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=original;
        }
        foreach(var area in Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None))
        {var source=Blocks.FirstOrDefault(b=>b.name==area.areaName);if(source!=null)area.polygon=source.poly.ToArray();}
    }

    static void ApplyChurchReplacement()
    {
        if(!AssetDatabase.LoadAssetAtPath<GameObject>(ChurchModelPath))return;
        Directory.CreateDirectory(ChurchReview);
        var report=new StringBuilder("Fallen City church: unchanged uniform scale 4 (human proportions).\n");
        var old=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(t=>t.name=="CentralChapel_Scale_1_25");
        if(old)old.gameObject.SetActive(false);
        var previous=GameObject.Find(ChurchRootName);if(previous)Object.DestroyImmediate(previous);
        var church=InstantiateHumanScaleChurch();
        SolidMeshes(church);
        GradeChurchMaterials(church);
        report.AppendLine("Bounds: "+RB(church));
        foreach(var r in church.GetComponentsInChildren<MeshRenderer>())GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic);

        var port=GameObject.Find("Elderboom_Village_Port").transform;
        var pieces=port.Cast<Transform>().Where(t=>t.gameObject.activeSelf&&t!=old&&t.GetComponentInChildren<Renderer>()).ToArray();
        bool Structure(string n)=>n.StartsWith("S_Medieval")||n.StartsWith("Wall_")||n.StartsWith("duvar")||n.StartsWith("çatı")||n.StartsWith("Pillar_")||n.StartsWith("S_Modular_Building");
        // Connected architectural pieces are cleared as complete buildings, so no
        // half-house, suspended eave or isolated window survives at the site edge.
        var architectural=pieces.Where(t=>Structure(t.name)).ToArray();
        var used=new HashSet<Transform>();var removedBuildings=new List<Bounds>();
        foreach(var first in architectural)
        {
            if(used.Contains(first))continue;
            var cluster=new List<Transform>{first};used.Add(first);var total=RB(first.gameObject);
            for(int q=0;q<cluster.Count;q++)
            {
                var b=RB(cluster[q].gameObject);b.Expand(.55f);
                foreach(var other in architectural)if(!used.Contains(other)&&b.Intersects(RB(other.gameObject)))
                {used.Add(other);cluster.Add(other);total.Encapsulate(RB(other.gameObject));}
            }
            if(!ChurchSite(total))continue;
            total.Expand(.7f);removedBuildings.Add(total);
        }
        foreach(var piece in pieces)
        {
            var b=RB(piece.gameObject);
            if(!ChurchSite(b)&&!removedBuildings.Any(x=>x.Intersects(b)))continue;
            piece.gameObject.SetActive(false);report.AppendLine("Cleared source object: "+piece.name);
        }
        report.AppendLine("Whole architectural clusters cleared: "+removedBuildings.Count);
        // Generated dressing is rebuilt independently of the original village.
        foreach(Transform group in art)
        {
            if(group.name=="Physical_Boundary_Banks")continue;
            foreach(Transform piece in group)
            {
                if(!piece.gameObject.activeSelf||!piece.GetComponentInChildren<Renderer>())continue;
                if(piece.name=="Creek_Continuous_Surface")continue;
                if(ChurchSite(RB(piece.gameObject))) {piece.gameObject.SetActive(false);report.AppendLine("Cleared dressing: "+group.name+"/"+piece.name);}
            }
        }
        foreach(var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if(go.name.Contains("Lamp")&&go.GetComponentInChildren<Renderer>()&&ChurchSite(RB(go))) {go.SetActive(false);report.AppendLine("Cleared lamp: "+go.name);}

        int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var origin=terrain.transform.position;var size=data.size;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            var p=new Vector3(origin.x+x*size.x/(n-1),0,origin.z+z*size.z/(n-1));
            if(ChurchSite(p))heights[z,x]=(.91f-origin.y)/size.y;
        }
        data.SetHeights(0,0,heights);
        var trees=data.treeInstances.Where(t=>!ChurchSite(origin+Vector3.Scale(t.position,size),3)).ToArray();
        report.AppendLine("Terrain trees cleared: "+(data.treeInstanceCount-trees.Length));data.SetTreeInstances(trees,true);

        // Move the rendered and collidable western boundary together, forming
        // a forecourt recess. The existing farm gate remains in its tested location.
        var borders=GameObject.Find(RootName).transform.Find("Borders");
        var boundaryPieces=borders.Cast<Transform>().SelectMany(g=>g.Cast<Transform>()).Concat(art.Find("Physical_Boundary_Banks").Cast<Transform>());
        foreach(var piece in boundaryPieces)
        {
            if(piece.name.EndsWith("_ChurchSiteShift")||!piece.GetComponentInChildren<Renderer>())continue;
            var b=RB(piece.gameObject);float dx=ChurchBoundaryShift(b.center.z);
            if(Mathf.Abs(b.center.x-34.2f)>3.4f||Mathf.Abs(dx)<.01f)continue;
            piece.position+=Vector3.right*dx;piece.name+="_ChurchSiteShift";
        }
        foreach(var area in Object.FindObjectsByType<ElderboomArea>(FindObjectsSortMode.None))
        {
            if(area.polygon==null)continue;var poly=new List<Vector2>();
            for(int i=0;i<area.polygon.Length;i++)
            {
                var a=area.polygon[i];var b=area.polygon[(i+1)%area.polygon.Length];poly.Add(a);
                if(Mathf.Abs(a.x-34.2f)>.8f||Mathf.Abs(b.x-34.2f)>.8f)continue;
                var zs=new[]{-36f,-27f,5f,12f}.Where(z=>z>Mathf.Min(a.y,b.y)&&z<Mathf.Max(a.y,b.y));
                zs=b.y>a.y?zs.OrderBy(z=>z):zs.OrderByDescending(z=>z);
                foreach(float z in zs)poly.Add(new Vector2(34.2f+ChurchBoundaryShift(z),z));
            }
            area.polygon=poly.ToArray();
        }
        // Remove street surfaces beneath the nave while retaining their outside parts.
        foreach(var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if(!mf.sharedMesh||!mf.name.StartsWith("Road"))continue;
            var path=ArtFolder+"/ChurchRoadOriginal_"+mf.name+".asset";
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!source)source=SaveArt(Object.Instantiate(mf.sharedMesh),"ChurchRoadOriginal_"+mf.name);
            var mesh=Object.Instantiate(source);var verts=mesh.vertices;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var indices=mesh.GetTriangles(sub);var keep=new List<int>();
                for(int k=0;k<indices.Length;k+=3)
                {var p=(verts[indices[k]]+verts[indices[k+1]]+verts[indices[k+2]])/3;
                    if(!ChurchSite(mf.transform.TransformPoint(p)))keep.AddRange(new[]{indices[k],indices[k+1],indices[k+2]});}
                mesh.SetTriangles(keep,sub);
            }
            mf.sharedMesh=SaveArt(mesh,"ChurchRoadCut_"+mf.name);var col=mf.GetComponent<MeshCollider>();if(col)col.sharedMesh=mf.sharedMesh;
        }
        foreach(var station in Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None))
        {
            if(!ChurchSite(station.transform.position))continue;
            var pos=station.transform.position;pos.z=15.5f;pos.y=Ground(new Vector2(pos.x,pos.z))+1;station.transform.position=pos;
            report.AppendLine("Moved station to north square: "+station.name+" "+pos);
        }
        var player=Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if(player&&ChurchSite(player.transform.position,2))
        {player.transform.position=new Vector3(5.7f,Ground(new Vector2(5.7f,16))+.15f,16);report.AppendLine("Moved player start to north square");}
        // Local lighting is tied to actual fixtures in the source church.  It
        // gives the nave readable night-time depth without making the stone
        // itself emissive or flooding the surrounding village with fake light.
        foreach(var t in church.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("ampoules.")).ToArray())
        {
            var lamp=new GameObject("Church_chandelier_light");lamp.transform.SetParent(church.transform,true);lamp.transform.position=t.GetComponent<Renderer>().bounds.center+Vector3.down;
            var light=lamp.AddComponent<Light>();light.type=LightType.Point;lamp.AddComponent<HDAdditionalLightData>();
            light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=12000;light.range=18;light.color=new Color(1,.76f,.48f);light.shadows=LightShadows.Soft;
        }
        foreach(float x in new[]{-12f,1f,14f,26f})
        {
            var lamp=new GameObject("Church_nave_fill_light");lamp.transform.SetParent(church.transform,true);lamp.transform.position=new Vector3(x,6.2f,-12.7f);
            var light=lamp.AddComponent<Light>();light.type=LightType.Point;lamp.AddComponent<HDAdditionalLightData>();
            light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=20000;light.range=18;light.color=new Color(1,.66f,.38f);light.shadows=LightShadows.Soft;
        }
        var churchLanterns=new[]{new Vector2(-24f,-17f),new Vector2(-24f,-8f),new Vector2(-8f,-34f),new Vector2(17f,-34f),new Vector2(-8f,9f),new Vector2(17f,9f)};
        foreach(var p in churchLanterns)
            Lantern(art,new Vector3(p.x,Ground(p)+2.15f,p.y),850,9);
        File.WriteAllText(ChurchReview+"/integration.txt",report.ToString());
    }

    static void GradeChurchMaterials(GameObject church)
    {
        // The GLB's atlas materials feed their albedo into emission as well.
        // In HDRP that produced a binary black/white church rather than weathered
        // masonry.  Use tileable, physically-based CC0 maps for all structural
        // stone while retaining the model's authored stained glass and fittings.
        var masonry=ChurchPolyMaterial("stone_wall_05","Church_Masonry_StoneWall05",.34f,Vector2.one,new Color(.73f,.76f,.72f),2.1f);
        var floor=ChurchPolyMaterial("stone_tiles","Church_Floor_StoneTiles",.19f,Vector2.one,new Color(.72f,.73f,.69f),3.2f);
        var wood=ChurchPolyMaterial("weathered_brown_planks","Church_Benches_WeatheredPlanks",.30f,new Vector2(3.3f,3.3f),new Color(.52f,.44f,.34f));
        var cache=new Dictionary<Material,Material>();
        foreach(var renderer in church.GetComponentsInChildren<Renderer>())
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var source=materials[i];if(!source)continue;
                var n=source.name.ToLowerInvariant();
                bool structural=n.StartsWith("mur")||n.StartsWith("colonne")||n.StartsWith("arche")||n=="pied-colonne"||n=="pied-autel"||n=="autel"||n=="estrade-centre"||n=="estrade-contour";
                if(structural){materials[i]=masonry;continue;}
                if(n=="sol"){materials[i]=floor;continue;}
                if(n=="banc"){materials[i]=wood;continue;}
                // Keep the source's authored glass/metal/fabric detail, but
                // remove accidental self-illumination from every non-glass map.
                bool glass=n.Contains("vitrail")||n=="verre-or";
                if(glass)continue;
                if(!cache.TryGetValue(source,out var material))
                {
                    material=new Material(source);
                    if(material.HasProperty("emissiveTexture"))material.SetTexture("emissiveTexture",null);
                    if(material.HasProperty("emissiveFactor"))material.SetColor("emissiveFactor",Color.black);
                    cache[source]=SaveArt(material,"ChurchDetail_"+source.name.Replace('.','_'));
                }
                materials[i]=cache[source];
            }
            renderer.sharedMaterials=materials;
        }
    }

    static Material ChurchPolyMaterial(string sourceName,string outputName,float smoothCap,Vector2 tiling,Color tint,float worldTileMeters=0)
    {
        var material=new Material(PolyMaterial(sourceName,smoothCap)){name=outputName};
        material.SetColor("_BaseColor",tint);
        foreach(var property in new[]{"_BaseColorMap","_NormalMap","_MaskMap"})
            if(material.HasProperty(property))material.SetTextureScale(property,tiling);
        // The imported UVs are packed atlases, with different density and
        // orientation on each piece. Physical world mapping keeps mortar
        // courses horizontal and stone size consistent across that geometry.
        if(worldTileMeters>0)
        {
            material.SetFloat("_UVBase",5);
            material.SetFloat("_ObjectSpaceUVMapping",0);
            material.SetFloat("_TexWorldScale",1/worldTileMeters);
        }
        // Preserve the source GLB's double-sided architectural shell.
        material.SetFloat("_DoubleSidedEnable",1);
        material.SetFloat("_DoubleSidedNormalMode",0);
        HDMaterial.ValidateMaterial(material);
        return SaveArt(material,outputName);
    }

    public static void ChurchRefreshSurfaces()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        ChurchPolyMaterial("stone_wall_05","Church_Masonry_StoneWall05",.34f,Vector2.one,new Color(.73f,.76f,.72f),2.1f);
        ChurchPolyMaterial("stone_tiles","Church_Floor_StoneTiles",.19f,Vector2.one,new Color(.72f,.73f,.69f),3.2f);
        AssetDatabase.SaveAssets();
        ChurchCapture();
    }

    public static void ChurchCapture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");Directory.CreateDirectory(ChurchReview);
        ChurchStill("01-human-scale-exterior",new Vector3(-43,4,4),new Vector3(1,15,-12.7f));
        ChurchStill("02-north-square",new Vector3(7,2.8f,20),new Vector3(13,14,-12.7f));
        ChurchStill("03-inside-nave",new Vector3(-11,2.85f,-12.7f),new Vector3(39,7,-12.7f));
        ChurchStill("04-overhead",new Vector3(13,112,-12.7f),new Vector3(13,0,-12.7f),true);
        ChurchStill("05-east-apse",new Vector3(60,3,0),new Vector3(37,14,-12.7f));
    }

    public static void ChurchMaterialAudit()
    {
        AssetDatabase.ImportAsset(ChurchModelPath,ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(ChurchReview);var s=new StringBuilder();
        foreach(var material in AssetDatabase.LoadAllAssetsAtPath(ChurchModelPath).OfType<Material>())
        {
            s.AppendLine("MATERIAL "+material.name+" shader="+material.shader.name);
            for(int i=0;i<material.shader.GetPropertyCount();i++)
            {
                var p=material.shader.GetPropertyName(i);var type=material.shader.GetPropertyType(i);
                if(type==UnityEngine.Rendering.ShaderPropertyType.Texture)s.AppendLine("  TEX "+p+"="+(material.GetTexture(p)?material.GetTexture(p).name:"NULL"));
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Color)s.AppendLine("  COL "+p+"="+material.GetColor(p));
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Float||type==UnityEngine.Rendering.ShaderPropertyType.Range)s.AppendLine("  VAL "+p+"="+material.GetFloat(p));
            }
        }
        File.WriteAllText(ChurchReview+"/material-audit.txt",s.ToString());
    }

    public static void ChurchSurvey()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        AssetDatabase.ImportAsset(ChurchModelPath,ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(ChurchReview);
        var church=InstantiateHumanScaleChurch();
        var b=RB(church);var report=new StringBuilder("Church at original Godot scale 4\n"+b+"\n");
        foreach(var r in church.GetComponentsInChildren<Renderer>())
            report.AppendLine("CHURCH "+r.name+" "+r.bounds+" "+string.Join(",",r.sharedMaterials.Select(m=>m.name+"/"+m.shader.name)));
        var records=new List<string>();
        foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if(r.transform.IsChildOf(church.transform))continue;
            var x=r.bounds;
            if(x.max.x < -50 || x.min.x > 55 || x.max.z < -48 || x.min.z > 15 || x.size.x>200)continue;
            records.Add(PathOf(r.transform)+"|"+string.Join(",",new[]{x.min.x,x.min.y,x.min.z,x.max.x,x.max.y,x.max.z}.Select(v=>v.ToString("F3",System.Globalization.CultureInfo.InvariantCulture))));
        }
        File.WriteAllLines(ChurchReview+"/surroundings.txt",records);
        File.WriteAllText(ChurchReview+"/survey.txt",report.ToString());
        // Inspection only: this scene is not saved by the survey.
        var old=GameObject.Find("CentralChapel_Scale_1_25");if(old)old.SetActive(false);
        ChurchStill("survey-exterior",new Vector3(-48,8,-2),new Vector3(1,12,-18));
        ChurchStill("survey-interior",new Vector3(-25,4.2f,-18),new Vector3(27,7,-18));
        ChurchStill("survey-overhead",new Vector3(4,100,-18),new Vector3(4,0,-18),true);
    }

    static void ChurchStill(string name,Vector3 eye,Vector3 target,bool ortho=false)
    {
        var go=new GameObject("Church_Review_Camera");var camera=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();
        camera.fieldOfView=65;camera.orthographic=ortho;camera.orthographicSize=55;camera.nearClipPlane=.1f;camera.farClipPlane=1000;
        go.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye,ortho?Vector3.forward:Vector3.up));
        var rt=new RenderTexture(1600,1000,24);var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;
        for(int i=0;i<12;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();
        File.WriteAllBytes(ChurchReview+"/"+name+".jpg",tex.EncodeToJPG(92));
        RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);
    }
}
