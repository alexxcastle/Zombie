using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

static partial class ElderboomClaudeBlocks
{
    const string ArtFolder = "Assets/Generated/EnvironmentArt";
    const float CreekWaterLevel=-1.05f;
    static Transform art;
    static Material artWood, artIron, artSoil, artStone;
    const string PolyPath="Assets/ThirdParty/PolyHaven/Environment/";
    static Texture2D ImportMap(string file,bool normal=false,bool linear=false,bool readable=false)
    {
        string path=PolyPath+file+".jpg";AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=!normal&&!linear;importer.isReadable=readable;importer.maxTextureSize=2048;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Material PolyMaterial(string asset,float smoothCap)
    {
        var m=new Material(Shader.Find("HDRP/Lit")){name=asset};m.SetColor("_BaseColor",Color.white);
        m.SetTexture("_BaseColorMap",ImportMap(asset+"_diff_2k"));m.SetTexture("_NormalMap",ImportMap(asset+"_nor_gl_2k",true));
        var rough=ImportMap(asset+"_rough_2k",false,true,true);var px=rough.GetPixels32();
        for(int i=0;i<px.Length;i++)px[i]=new Color32(0,255,128,(byte)Mathf.Min(255-px[i].r,255*smoothCap));
        var mask=new Texture2D(rough.width,rough.height,TextureFormat.RGBA32,true,true){name=asset+"_HDRPMask"};mask.SetPixels32(px);mask.Apply();mask.Compress(true);mask.Apply(false,true);
        m.SetTexture("_MaskMap",SaveArt(mask,asset+"_HDRPMask"));m.SetFloat("_Smoothness",1);HDMaterial.ValidateMaterial(m);return SaveArt(m,asset+"_Material");
    }
    static readonly List<(Vector2 p, float radius, int layer)> groundPatches = new();
    static readonly Vector2[] ArtCreek = Ws(-132,-110,-98,-91,-64,-72,-62,-55,-45,-47,-25,-46,1,-47,25,-49,50,-47,70,-55,98,-70,138,-90);
    static T SaveArt<T>(T obj,string name) where T:Object
    {
        obj.name=name;
        string path=ArtFolder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<T>(path);
        if(saved==null){AssetDatabase.CreateAsset(obj,path);return obj;}
        EditorUtility.CopySerialized(obj,saved);Object.DestroyImmediate(obj);EditorUtility.SetDirty(saved);return saved;
    }
    static Material ArtMaterial(string name,Color color,float smoothness,Material source=null)
    {
        var m=source?new Material(source):new Material(Shader.Find("HDRP/Lit"));m.name=name;
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
        return SaveArt(m,name);
    }
    static GameObject ArtBox(string name,Transform parent,Vector3 at,Vector3 size,Material mat,Quaternion rotation=default,bool collision=false)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;
        go.transform.SetPositionAndRotation(at,rotation==default?Quaternion.identity:rotation);go.transform.localScale=size;
        go.transform.SetParent(parent,true);go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static Transform ArtGroup(string name,Transform parent=null)
    {var t=new GameObject(name).transform;t.SetParent(parent?parent:art,true);return t;}
    static void SolidMeshes(GameObject go)
    {foreach(var m in go.GetComponentsInChildren<MeshFilter>())if(m.sharedMesh && !m.GetComponent<Collider>())m.gameObject.AddComponent<MeshCollider>().sharedMesh=m.sharedMesh;}
    static GameObject Prop(string prefix,Transform parent,Vector2 p,float yaw=0,float scale=1,bool solid=true)
    {
        var source=FindPiece(prefix);if(!source)return null;
        var go=Clone(source,parent);go.SetActive(true);go.transform.rotation=Quaternion.Euler(0,yaw,0)*source.transform.rotation;
        go.transform.localScale*=scale;SetCenter(go,new Vector3(p.x,0,p.y));Seat(go,p,.035f);
        if(solid)SolidMeshes(go);return go;
    }
    static Vector3 At(Vector2 p,float up=0)=>new Vector3(p.x,Ground(p)+up,p.y);

    // One repeatable art pass. The source scene is saved in place; no project copies.
    public static void ArtBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        Directory.CreateDirectory(ArtFolder);Directory.CreateDirectory("Logs/ArtReview/After");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        var baseline=AssetDatabase.LoadAssetAtPath<TerrainData>(ArtFolder+"/TerrainBeforeArt.asset");
        if(!baseline){baseline=Object.Instantiate(data);AssetDatabase.CreateAsset(baseline,ArtFolder+"/TerrainBeforeArt.asset");}
        // Leave room below world zero for an actual cut river bed. All original
        // land heights are restored in world space before carving the channel.
        var restored=baseline.GetHeights(0,0,baseline.heightmapResolution,baseline.heightmapResolution);
        terrain.transform.position=new Vector3(terrain.transform.position.x,-6,terrain.transform.position.z);
        for(int z=0;z<restored.GetLength(0);z++)for(int x=0;x<restored.GetLength(1);x++)restored[z,x]+=6/data.size.y;
        data.SetHeights(0,0,restored);
        data.SetTreeInstances(baseline.treeInstances,false);
        var old=GameObject.Find("Elderboom_Environment_Art");if(old)Object.DestroyImmediate(old);
        art=new GameObject("Elderboom_Environment_Art").transform;Random.InitState(531042);groundPatches.Clear();
        artWood=PolyMaterial("weathered_brown_planks",.35f);
        artIron=ArtMaterial("ForgedIron",new Color(.075f,.067f,.059f),.28f);artIron.SetFloat("_Metallic",.72f);
        artSoil=PolyMaterial("mud_forest",.26f);
        var rock=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Rocks/Rock_A_01.prefab");
        artStone=ArtMaterial("RiverStone",new Color(.58f,.57f,.52f),.22f,rock.GetComponentInChildren<Renderer>().sharedMaterial);
        var blocks=GameObject.Find(RootName).transform;
        foreach(var ps in Passes){var g=blocks.GetComponentsInChildren<ElderboomGate>().First(x=>x.areaName==ps.area&&x.cost==ps.cost);ps.at=new Vector2(g.transform.position.x,g.transform.position.z);}
        ReshapeArtTerrain();
        BuildPuddles();
        DressStations();
        BuildArtCreek(rock);
        BuildArtBoundaries(blocks,rock);
        BuildCompounds();
        SeparateWorkyard();
        DressDomesticPlots();
        LicensedWorkyardDetails();
        EcologicalDetails();
        RefineContactAndFixtures();
        SoftenRoadEdges();
        ArtLighting(blocks);
        PaintArtGround(baseline);
        ArtForestAndBackdrop(baseline);
        ApplyChurchReplacement();
        ApplyStoneBridge();
        RefineChapelSkyline();
        foreach(var r in art.GetComponentsInChildren<MeshRenderer>())GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic);
        Physics.SyncTransforms();
        string nav=Bake();
        // Shift any spawn caught by a newly dressed enclosure onto reachable ground.
        var player=Object.FindAnyObjectByType<HDRPSlicePlayer>();var path=new UnityEngine.AI.NavMeshPath();
        foreach(var spawn in blocks.GetComponentsInChildren<ElderboomSpawnPoint>())
        {
            bool Reach(Vector3 p)=>UnityEngine.AI.NavMesh.CalculatePath(p,player.transform.position,UnityEngine.AI.NavMesh.AllAreas,path)&&path.status==UnityEngine.AI.NavMeshPathStatus.PathComplete;
            if(Reach(spawn.transform.position))continue;
            bool found=false;
            for(float radius=1;radius<=8&&!found;radius+=1)for(int angle=0;angle<360;angle+=30)
            {var p=spawn.transform.position+Quaternion.Euler(0,angle,0)*Vector3.forward*radius;
                if(!UnityEngine.AI.NavMesh.SamplePosition(p,out var hit,1,UnityEngine.AI.NavMesh.AllAreas)||!Reach(hit.position))continue;
                var area=Blocks.First(b=>b.name==spawn.area);if(!Inside(new Vector2(hit.position.x,hit.position.z),area.poly))continue;
                spawn.transform.position=hit.position+Vector3.up*.05f;found=true;break;}
            if(!found)throw new InvalidOperationException("Art dressing isolated spawn "+spawn.name);
        }
        EditorUtility.SetDirty(data);AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        var report=new StringBuilder(nav);
        report.AppendLine("\nArt pass saved in place. Station cabinets, continuous creek, physical boundaries, compounds, contact materials, lanterns, fog, forest backdrop.");
        report.AppendLine("Active legacy invisible wall boxes: "+(blocks.Find("Invisible_Walls")?blocks.Find("Invisible_Walls").GetComponentsInChildren<BoxCollider>(true).Count(c=>c.enabled&&c.gameObject.activeInHierarchy):0));
        report.AppendLine("Active old creek planes: "+Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r=>r.name=="Creek_Water"));
        report.AppendLine("Active station prototype renderers: "+Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None).Count(s=>s.GetComponent<Renderer>()&&s.GetComponent<Renderer>().enabled));
        File.WriteAllText("Logs/ArtReview/After/build.txt",report.ToString());
    }

    static void ReshapeArtTerrain()
    {
        int n=data.heightmapResolution;var h=data.GetHeights(0,0,n,n);var o=terrain.transform.position;var sz=data.size;
        var curve=CreekSamples();
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            var p=new Vector2(o.x+x*sz.x/(n-1),o.z+z*sz.z/(n-1));float d=LineDist(p,ArtCreek);
            float width=3.1f+.3f*Mathf.Sin(p.x*.19f)+.18f*Mathf.Sin(p.y*.41f);
            if(d<9)
            {
                float signed=0,best=float.MaxValue;
                for(int k=0;k<curve.Count-1;k+=3){var a=curve[k];var b=curve[Mathf.Min(k+3,curve.Count-1)];var ab=b-a;var v=p-(a+ab*Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude));if(v.sqrMagnitude<best){best=v.sqrMagnitude;signed=Vector2.Dot(v,new Vector2(-ab.y,ab.x).normalized);}}
                d=Mathf.Sqrt(best);
                if(d<width+2.8f){float bed=-1.64f+.15f*Mathf.PerlinNoise(p.x*.4f,p.y*.4f);
                    // Accessible northern gravel bank, eroded southern cut bank:
                    // visible terrain, not a vertical invisible gameplay barrier.
                    float bank=Mathf.Lerp(bed,1.16f,signed>=0?Smooth(width*.18f,width+1.8f,d):Smooth(width-.2f,width+.55f,d));
                    h[z,x]=Mathf.Lerp((bank-o.y)/sz.y,h[z,x],Smooth(width+1.7f,width+2.8f,d));}
            }
            float radial=Vector2.Distance(p,C);
            if(radial>150){float hills=7+14*Mathf.PerlinNoise(p.x*.013f+48,p.y*.013f+13)+6*Mathf.PerlinNoise(p.x*.035f,p.y*.035f);h[z,x]=Mathf.Lerp(h[z,x],Mathf.Max(h[z,x],(hills-o.y)/sz.y),Smooth(150,215,radial));}
        }
        data.SetHeights(0,0,h);
    }
    static void DressStations()
    {
        foreach(var st in Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None))
        {
            var old=st.transform.Find("SupplyCabinet");if(old)Object.DestroyImmediate(old.gameObject);
            var renderer=st.GetComponent<Renderer>();if(renderer)renderer.enabled=false;
            var cabinet=ArtGroup("SupplyCabinet",st.transform);var p=new Vector2(st.transform.position.x,st.transform.position.z);
            var q=Quaternion.Euler(0,st.transform.eulerAngles.y,0);var basePos=At(p);
            Vector3 P(float x,float y,float z)=>basePos+q*new Vector3(x,y,z);
            float width=st.category==ElderboomIds.Station.Slot?1.42f:.94f;
            ArtBox("Cabinet_back",cabinet,P(0,.94f,.30f),new Vector3(width,1.88f,.12f),artWood,q);
            foreach(float side in new[]{-1f,1f})ArtBox("Cabinet_side",cabinet,P(side*(width*.5f-.055f),.94f,0),new Vector3(.11f,1.88f,.7f),artWood,q);
            foreach(float y in new[]{.1f,.73f,1.36f,1.86f})ArtBox("Shelf",cabinet,P(0,y,0),new Vector3(width,.09f,.7f),artWood,q);
            for(int i=0;i<6;i++)ArtBox("Door_plank",cabinet,P((i-2.5f)*width/6,.48f,-.36f),new Vector3(width/6-.012f,.76f,.045f),artWood,q);
            foreach(float y in new[]{.24f,.71f})ArtBox("Forged_strap",cabinet,P(0,y,-.39f),new Vector3(width*.94f,.035f,.018f),artIron,q);
            for(int i=0;i<3;i++)ArtBox(st.category==ElderboomIds.Station.Medical?"Medical_supply_box":"Ammunition_case",cabinet,P((i-1)*width*.26f,.88f,-.04f),new Vector3(width*.22f,.22f,.34f),artWood,q);
            ArtBox("Cabinet_roof",cabinet,P(0,1.96f,0),new Vector3(width+.17f,.1f,.9f),artWood,q*Quaternion.Euler(-7,0,0));
            Lantern(cabinet,P(width*.65f,2.03f,0),260,5);
            groundPatches.Add((p,1.7f,5));
        }
    }
    static Mesh Ribbon(string name,IList<Vector3> left,IList<Vector3> right)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();float distance=0;
        for(int i=0;i<left.Count;i++){if(i>0)distance+=Vector3.Distance(left[i],left[i-1]);v.Add(left[i]);v.Add(right[i]);uv.Add(new Vector2(0,distance*.16f));uv.Add(new Vector2(1,distance*.16f));if(i>0){int k=i*2;t.AddRange(new[]{k-2,k,k-1,k-1,k,k+1});}}
        var m=new Mesh{name=name};m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();return SaveArt(m,name);
    }
    static GameObject MeshArt(string name,Transform parent,Mesh mesh,Material mat,bool solid=false)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;if(solid)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;}
    static List<Vector2> CreekSamples()
    {
        var samples=new List<Vector2>();
        // Catmull-Rom smoothing keeps one surface through all bends, without overlaps.
        for(int i=0;i<ArtCreek.Length-1;i++){var a=ArtCreek[Mathf.Max(0,i-1)];var b=ArtCreek[i];var c=ArtCreek[i+1];var d=ArtCreek[Mathf.Min(ArtCreek.Length-1,i+2)];int steps=Mathf.CeilToInt(Vector2.Distance(b,c)/1.1f);
            for(int k=0;k<steps;k++){float t=k/(float)steps; samples.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));}}
        samples.Add(ArtCreek.Last());return samples;
    }
    static void BuildArtCreek(GameObject rock)
    {
        var old=GameObject.Find(RootName).transform.Find("Borders/Creek_Border");if(old)old.gameObject.SetActive(false);
        var creek=ArtGroup("Creek_Wetland");var samples=CreekSamples();var l=new List<Vector3>();var r=new List<Vector3>();
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Details/GrassDry_B.prefab");
        for(int i=0;i<samples.Count;i++)
        {
            var p=samples[i];var tangent=(samples[Mathf.Min(i+1,samples.Count-1)]-samples[Mathf.Max(0,i-1)]).normalized;var normal=new Vector2(-tangent.y,tangent.x);
            float width=1.85f+.32f*Mathf.Sin(p.x*.19f)+.18f*Mathf.Sin(p.y*.41f);
            // Intersect the banks; the irregular terrain masks the water's edge.
            float coveredWidth=width+3;
            l.Add(new Vector3(p.x-normal.x*coveredWidth,CreekWaterLevel,p.y-normal.y*coveredWidth));r.Add(new Vector3(p.x+normal.x*coveredWidth,CreekWaterLevel,p.y+normal.y*coveredWidth));
            if(i%3==0)foreach(float side in new[]{-1f,1f})
            {
                var bank=p+normal*(width+Random.Range(.6f,1.5f))*side;
                if(PassDist(bank)<6)continue;
                if(i%9==0)Place(rock,creek,bank,Random.Range(.35f,.95f),.38f);
                for(int g=0;g<3;g++)Place(grass,creek,bank+Random.insideUnitCircle*.6f,Random.Range(.25f,.65f),.05f);
            }
            if(i%19==0&&PassDist(p)>7)Place(rock,creek,p+normal*Random.Range(-1,1),Random.Range(.6f,1.1f),.12f);
        }
        var src=AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainDemoScene_HDRP/Prefabs/Water/Materials/WaterDepthBased.mat");var water=new Material(src){name="Creek_Flow"};
        water.SetFloat("_NormalStrength",.15f);water.SetFloat("_Smoothness",.79f);water.SetFloat("_Speed",.035f);
        water.SetVector("_Tiling",new Vector4(5.5f,8,0,0));water.SetColor("_Deep_Water_Color",new Color(.027f,.069f,.065f,0));water.SetColor("_Shallow_Water_Color",new Color(.19f,.25f,.20f,0));
        MeshArt("Creek_Continuous_Surface",creek,Ribbon("CreekRibbon",l,r),SaveArt(water,"CreekFlow"));
        FogPatch("Creek_ground_mist",At(W(0,-49),1),new Vector3(105,2.2f,13),65);
    }
    static void BuildArtBoundaries(Transform blocks,GameObject rock)
    {
        var old=blocks.Find("Invisible_Walls");
        // Replace tall invisible boxes by visible, collidable, irregular stone/earth banks.
        // Preserve their ground footprints and gate gaps; collision follows the rendered mesh.
        var parent=ArtGroup("Physical_Boundary_Banks");int count=0;
        if(old)foreach(var box in old.GetComponentsInChildren<BoxCollider>(true))
        {
            // Inactive colliders have empty bounds. Read the saved local geometry.
            var c=box.transform.TransformPoint(box.center);var p=new Vector2(c.x,c.z);
            if(LineDist(p,Borders.First(b=>b.kind==Kind.Creek).pts)<1.2f)continue;
            bool masonry=Borders.Where(b=>b.kind==Kind.StoneWall||b.kind==Kind.Fence).Any(b=>LineDist(p,b.pts)<1.2f);
            // Only real fence/wall boundaries get built as geometry. The broad
            // rock banks made the village read as a fenced game board, so open
            // land now resolves into terrain, trees and brush instead.
            if(!masonry)continue;
            float height=1.65f;
            float half=masonry?.42f:2.8f;float len=box.size.z*.5f;
            var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            for(int i=0;i<5;i++)
            {
                float z=Mathf.Lerp(-len,len,i/4f);var world=box.transform.TransformPoint(new Vector3(0,0,z));float y=Ground(new Vector2(world.x,world.z))-.28f;
                float n=Mathf.PerlinNoise(world.x*.54f+83,world.z*.54f);
                float top=y+height+(masonry?0:(n-.5f)*1.3f);
                float offset=masonry?0:(Mathf.PerlinNoise(world.x*.32f,world.z*.32f+23)-.5f)*1.1f;
                float width=masonry?half:half*(.8f+n*.7f);
                foreach(var local in new[]{new Vector3(-width,y,z),new Vector3(offset-half*.22f,top,z),new Vector3(offset+half*.22f,top-(masonry?.12f:.35f),z),new Vector3(width,y,z)})
                {var v=box.transform.position+box.transform.rotation*new Vector3(local.x,0,local.z);v.y=local.y;verts.Add(v);uv.Add(new Vector2((world.x+world.z)*.22f,(local.y-y)*.28f));}
                if(i>0)for(int j=0;j<3;j++){int k=i*4+j;tris.AddRange(new[]{k-4,k,k-3,k-3,k,k+1});}
            }
            var mesh=new Mesh{name="BoundaryBank"};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();
            MeshArt("Grounded_Bank_"+count,parent,SaveArt(mesh,"Bank_"+count++),artStone,true);
        }
        if(old)old.gameObject.SetActive(false);
        var visible=blocks.Find("Borders");if(visible)foreach(var r in visible.GetComponentsInChildren<MeshRenderer>())
            if(r.name.Contains("Fence")||r.name.Contains("Stone_Wall"))SolidMeshes(r.gameObject);
    }

    [MenuItem("Elderboom HDRP/Remove Rock Boundary Barriers")]
    public static void RemoveRockBoundaryBarriers()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before simplifying map boundaries.");
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!="Assets/Scenes/Elderboom_Unity_Port.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();data=terrain.terrainData;
        int removedBuilt=0,removedLegacy=0;var blocks=GameObject.Find(RootName)?.transform;
        var oldBorders=blocks?.Find("Borders");
        if(oldBorders)foreach(Transform border in oldBorders)
            if(border.name.Contains("Rocks_Border")||border.name.Contains("Ring_Border")){border.gameObject.SetActive(false);removedLegacy++;}
        var physical=GameObject.Find("Elderboom_Environment_Art")?.transform.Find("Physical_Boundary_Banks");
        if(physical)foreach(Transform item in physical)
        {
            var renderer=item.GetComponentInChildren<Renderer>();
            if(!renderer){item.gameObject.SetActive(false);removedBuilt++;continue;}
            var p=new Vector2(renderer.bounds.center.x,renderer.bounds.center.z);
            bool architectural=Borders.Where(b=>b.kind==Kind.StoneWall||b.kind==Kind.Fence).Any(b=>LineDist(p,b.pts)<2.2f);
            if(architectural)continue;
            item.gameObject.SetActive(false);removedBuilt++;
        }
        Physics.SyncTransforms();
        string nav=Bake();
        Directory.CreateDirectory("Logs/ArtReview/After");
        File.WriteAllText("Logs/ArtReview/After/boundary-simplification.txt",
            $"Disabled generated rock/earth barrier pieces: {removedBuilt}.\nDisabled legacy rock-border groups: {removedLegacy}.\nRetained only architectural fence/stone walls, creek banks and vegetation/terrain backdrop.\n{nav}");
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        Debug.Log($"Simplified rock boundaries: {removedBuilt} generated pieces and {removedLegacy} legacy rock-border groups disabled.");
    }

    static void BuildCompounds()
    {
        var old=GameObject.Find(RootName).transform.Find("Combat_Islands");if(old)old.gameObject.SetActive(false);
        var farm=ArtGroup("Farm_Working_Yard");
        foreach(var p in new[]{W(-69,-5),W(-72,-8),W(-76,-6)})Prop("S_Round_Hay_Bale",farm,p,Random.Range(-15,20),.9f);
        Prop("S_Old_Wooden_Trough",farm,W(-64,-12),90);Prop("S_Wooden_Barrel",farm,W(-60,-9),12);
        BuildCart(farm,W(-66,-17),-18);
        // Soil rows define cultivated land; staggered dry stalks read as an abandoned field.
        var dry=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Details/GrassDry_C.prefab");
        for(int row=0;row<7;row++)for(int k=0;k<14;k++)
        {var p=W(-80+row*1.35f,-36+k*.9f);groundPatches.Add((p,.68f,5));if(Random.value<.72f)Place(dry,farm,p+Random.insideUnitCircle*.14f,Random.Range(.3f,.65f),.03f);}
        FenceArt(farm,W(-83,-39),W(-69,-39));FenceArt(farm,W(-83,-39),W(-83,-23));
        groundPatches.Add((W(-67,-12),8,5));
        var timber=ArtGroup("Timber_Sawmill_Yard");
        Prop("S_Chopped_Wood_Pile",timber,W(-16,60),102,.72f);
        Shed(timber,W(-10,61),new Vector2(7,4),-12);
        Prop("S_Wooden_Barrel",timber,W(-6,62),15);
        ArtBox("Loading_ramp",timber,At(W(-7,57),.45f),new Vector3(2.3f,.14f,4),artWood,Quaternion.Euler(-12,0,0),true);
        var hoist=At(W(-13,60));ArtBox("Hoist_mast",timber,hoist+Vector3.up*1.7f,new Vector3(.22f,3.4f,.22f),artWood);
        ArtBox("Hoist_boom",timber,hoist+new Vector3(1,3.3f,0),new Vector3(2.3f,.18f,.18f),artWood);
        ArtBox("Hoist_rope",timber,hoist+new Vector3(1.9f,2.3f,0),new Vector3(.025f,1.9f,.025f),artIron);
        Prop("S_Wooden_Wheel",timber,W(-11.1f,60),90,.35f,false)?.transform.Translate(Vector3.up*2.8f,Space.World);
        groundPatches.Add((W(-11,59),9,5));
        var cemetery=ArtGroup("Cemetery_Consecrated_Ground");var graves=GameObject.Find(RootName).transform.Find("Cemetery");if(graves)graves.gameObject.SetActive(false);
        var plots=new List<Vector2>();
        for(int i=0;i<25;i++)
        {Vector2 p=default;for(int attempt=0;attempt<100;attempt++){p=W(Random.Range(57.5f,76f),Random.Range(-19.5f,-3f));if(plots.All(other=>Vector2.Distance(other,p)>2.1f))break;}plots.Add(p);
            var g=Prop("S_Old_Gravestone",cemetery,p,85+Random.Range(-18,18),Random.Range(.82f,1.15f));if(g)g.transform.Rotate(0,0,Random.Range(-9f,9f));
            groundPatches.Add((p+new Vector2(1,0),1.35f,5));}
        FenceArt(cemetery,W(56,-23),W(79,-23),true);FenceArt(cemetery,W(79,-23),W(79,0),true);
        Prop("S_Old_Wooden_Bench",cemetery,W(55,1),90);FogPatch("Cemetery_low_fog",At(W(67,-12),1.1f),new Vector3(29,2.8f,30),48);
        var cypress=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Trees/Cypress/Cypress.prefab");
        foreach(var p in new[]{W(58,-25),W(66,-26.5f),W(78,-25),W(81,-10)})Place(cypress,cemetery,p,Random.Range(6,8.5f),.025f);
        var creek=ArtGroup("Creek_Ferryman_Landing");Shed(creek,W(-20,-61),new Vector2(3.5f,3),12);
        Prop("S_Wooden_Barrel",creek,W(-17,-61),13);Prop("S_Old_Wooden_Trough",creek,W(-19,-58),80);
        groundPatches.Add((W(-19,-60),4,5));
    }
    static void SeparateWorkyard()
    {
        // The north barn occupies x=-10..2,z=55..65. Move the new work assembly
        // into the western yard so roofs, ramp and hoist have their own footprint.
        var yard=art.Find("Timber_Sawmill_Yard");if(!yard)return;
        foreach(Transform piece in yard)
        {
            if(piece.name.StartsWith("S_Chopped")||piece.name.StartsWith("S_Worn"))continue;
            piece.position+=new Vector3(-16,Ground(W(-26,71))-Ground(W(-10,61)),10);
        }
        groundPatches.Add((W(-27,69),8,5));
    }
    static void FenceArt(Transform parent,Vector2 a,Vector2 b,bool stone=false)
    {var src=FindPiece(stone?"S_Mossy_Stone_Wall":"S_Modular_Fence");if(!src)return;var group=ArtGroup(stone?"Burial_wall":"Enclosure",parent);Tile(src,group,new[]{a,b},stone);foreach(Transform t in group)SolidMeshes(t.gameObject);}
    static void Shed(Transform parent,Vector2 p,Vector2 size,float yaw)
    {
        var q=Quaternion.Euler(0,yaw,0);var at=At(p);var group=ArtGroup("Working_LeanTo",parent);
        foreach(float x in new[]{-size.x*.5f,size.x*.5f})foreach(float z in new[]{-size.y*.5f,size.y*.5f})
        {var foot=at+q*new Vector3(x,0,z);foot.y=Ground(new Vector2(foot.x,foot.z));ArtBox("Timber_support",group,foot+Vector3.up*1.25f,new Vector3(.2f,2.5f,.2f),artWood,q,true);}
        for(int i=0;i<Mathf.CeilToInt(size.x/.3f)+1;i++)ArtBox("Overlapping_roof_board",group,at+q*new Vector3(-size.x*.5f+i*.3f,2.55f,0),new Vector3(.31f,.075f,size.y+.65f),artWood,q*Quaternion.Euler(9,0,0));
        ArtBox("Roof_crossbeam",group,at+q*new Vector3(0,2.35f,-size.y*.5f),new Vector3(size.x+.3f,.18f,.17f),artWood,q);
        groundPatches.Add((p,Mathf.Max(size.x,size.y)*.7f,5));Lantern(group,at+q*new Vector3(size.x*.45f,2,-size.y*.5f),380,7);
    }
    static void BuildCart(Transform parent,Vector2 p,float yaw)
    {
        var q=Quaternion.Euler(0,yaw,0);var at=At(p,.7f);var t=ArtGroup("Abandoned_Farm_Cart",parent);
        for(int i=0;i<7;i++)ArtBox("Bed_plank",t,at+q*new Vector3((i-3)*.22f,0,0),new Vector3(.21f,.13f,2.5f),artWood,q,true);
        foreach(float s in new[]{-1f,1f}){
            for(int y=0;y<3;y++)ArtBox("Cart_side",t,at+q*new Vector3(s*.8f,.25f+y*.2f,0),new Vector3(.09f,.15f,2.5f),artWood,q);
            ArtBox("Cart_shaft",t,at+q*new Vector3(s*.55f,-.05f,2),new Vector3(.12f,.12f,2.4f),artWood,q);
            var wp=p+new Vector2((q*new Vector3(s*.95f,0,0)).x,(q*new Vector3(s*.95f,0,0)).z);
            var wheel=Prop("S_Wooden_Wheel",t,wp,yaw,1,false);if(wheel){wheel.transform.localScale*=1.32f/RB(wheel).size.y;SetCenter(wheel,new Vector3(wp.x,at.y,wp.y));foreach(var r in wheel.GetComponentsInChildren<Renderer>())r.sharedMaterial=artWood;SolidMeshes(wheel);}}
        ArtBox("Cart_axle",t,at,new Vector3(2,.13f,.13f),artIron,q);
        Prop("S_Round_Hay_Bale",t,p,yaw,.55f)?.transform.Translate(Vector3.up*.8f,Space.World);
    }
    static void DeadTree(Transform parent,Vector2 p,float height)
    {
        var t=ArtGroup("Pollarded_dead_tree",parent);var at=At(p);
        // Rough imported timber supplies bark-like surface; branch silhouettes are irregular.
        ArtBox("Trunk",t,at+Vector3.up*height*.5f,new Vector3(.22f,height,.23f),artWood,Quaternion.Euler(0,25,5));
        for(int j=0;j<4;j++){float angle=j*91+Random.Range(-20,20);var dir=Quaternion.Euler(0,angle,0)*new Vector3(1,.9f,0);var a=at+Vector3.up*(height*.45f+j*.18f);var b=a+dir*Random.Range(.7f,1.3f);ArtBox("Broken_branch",t,(a+b)/2,new Vector3(.09f,.1f,(b-a).magnitude),artWood,Quaternion.LookRotation(b-a));}
    }
    static void DressVillage()
    {
        var t=ArtGroup("Village_Courtyards");
        // Small inhabited lots connect the original modular facades rather than moving them.
        var lots=new[]{W(-24,-19),W(-26,13),W(16,20),W(32,-62),W(-53,-16),W(15,49)};
        for(int i=0;i<lots.Length;i++)
        {var p=lots[i];var group=ArtGroup("Household_"+i,t);Prop("S_Wooden_Barrel",group,p,17+i*27,.85f);Prop("S_Old_Metal_Pot",group,p+new Vector2(1,0),i*31,.9f);
            Prop(i%2==0?"S_Chopped_Wood_Pile":"S_Old_Wooden_Bench",group,p+new Vector2(-1,1.5f),90+i*8,.65f);
            FenceArt(group,p+new Vector2(-3,3),p+new Vector2(3,3));groundPatches.Add((p,3.4f,5));
            if(i%2==0)Shed(group,p+new Vector2(0,1.5f),new Vector2(3,2),i*4-8);}
    }
    static void SoftenRoadEdges()
    {
        foreach(var mf in Object.FindObjectsByType<MeshFilter>())
        {
            if(!mf.name.StartsWith("Road_")||!mf.sharedMesh)continue;
            string baselinePath=ArtFolder+"/RoadSource_"+mf.name+".asset";
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(baselinePath);
            if(!source){source=Object.Instantiate(mf.sharedMesh);AssetDatabase.CreateAsset(source,baselinePath);}
            var m=Object.Instantiate(source);var verts=m.vertices;
            if(verts.Length%5!=0)continue; // Dense roads have five vertices across each row.
            for(int row=0;row<verts.Length/5;row++)
            {
                int start=row*5;var left=mf.transform.TransformPoint(verts[start]);var right=mf.transform.TransformPoint(verts[start+4]);var across=(right-left).normalized;
                for(int k=0;k<5;k++){var v=mf.transform.TransformPoint(verts[start+k]);v.y=Ground(new Vector2(v.x,v.z))+.02f;verts[start+k]=mf.transform.InverseTransformPoint(v);}
                foreach(int k in new[]{0,4})
                {
                    var p=mf.transform.TransformPoint(verts[start+k]);float offset=(Mathf.PerlinNoise(p.x*.9f+3,p.z*.9f)-.5f)*.5f;
                    p+=across*offset;p.y=Ground(new Vector2(p.x,p.z))+.008f;verts[start+k]=mf.transform.InverseTransformPoint(p);
                    if(row%5==0)groundPatches.Add((new Vector2(p.x,p.z),.7f,5));
                }
            }
            m.vertices=verts;m.RecalculateNormals();m.RecalculateBounds();mf.sharedMesh=SaveArt(m,"RoadFrayed_"+mf.name);
            var collider=mf.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=mf.sharedMesh;
        }
    }
    static void Lantern(Transform parent,Vector3 at,float lumens,float range)
    {
        var t=ArtGroup("Forged_Lantern",parent);var glow=ArtMaterial("LanternAmber",new Color(.6f,.28f,.065f),.25f);glow.SetColor("_EmissiveColor",new Color(4,1.8f,.4f));
        ArtBox("Glass_chamber",t,at,new Vector3(.16f,.25f,.16f),glow);
        foreach(float x in new[]{-.105f,.105f})foreach(float z in new[]{-.105f,.105f})ArtBox("Iron_frame",t,at+new Vector3(x,0,z),new Vector3(.025f,.31f,.025f),artIron);
        foreach(float y in new[]{-.17f,.17f})ArtBox("Lantern_cap",t,at+Vector3.up*y,new Vector3(.27f,.045f,.27f),artIron);
        ArtBox("Mounting_arm",t,at+new Vector3(0,.27f,.17f),new Vector3(.035f,.035f,.36f),artIron);
        ArtBox("Mounting_post",t,new Vector3(at.x,Ground(new Vector2(at.x,at.z))+(at.y-Ground(new Vector2(at.x,at.z)))*.5f,at.z+.34f),new Vector3(.09f,at.y-Ground(new Vector2(at.x,at.z))+.45f,.09f),artWood);
        var light=new GameObject("Lantern_warm_light");light.transform.SetParent(t);light.transform.position=at+Vector3.down*.08f;
        var l=light.AddComponent<Light>();l.type=LightType.Point;var hd=light.AddComponent<HDAdditionalLightData>();l.lightUnit=LightUnit.Lumen;l.intensity=lumens;l.range=range;l.color=new Color(1,.64f,.32f);l.shadows=LightShadows.Soft;hd.shadowResolution.level=0;hd.shadowFadeDistance=18;
    }
    static void FogPatch(string name,Vector3 p,Vector3 size,float path)
    {var go=new GameObject(name);go.transform.SetParent(art);go.transform.position=p;var fog=go.AddComponent<LocalVolumetricFog>();fog.parameters=new LocalVolumetricFogArtistParameters(new Color(.48f,.56f,.61f),path,.1f){size=size,positiveFade=Vector3.one*.35f,negativeFade=Vector3.one*.35f,distanceFadeStart=80,distanceFadeEnd=150};}
    static void ArtLighting(Transform blocks)
    {
        var old=blocks.Find("Route_Lights");if(old)old.gameObject.SetActive(false);
        var parent=ArtGroup("Motivated_Path_Lighting");
        foreach(var gate in blocks.GetComponentsInChildren<ElderboomGate>())
        {var p=gate.transform.position+gate.transform.right*3.6f;p.y=Ground(new Vector2(p.x,p.z))+2.3f;Lantern(parent,p,gate.areaName=="East Cemetery"?300:420,8);}
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(light.name=="Warm_Lamp_Light") {light.intensity=1600;light.range=12;light.color=new Color(1,.68f,.39f);}
        foreach(var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {if(!volume.sharedProfile)continue;if(volume.sharedProfile.TryGet<Fog>(out var fog)){fog.enabled.Override(true);fog.enableVolumetricFog.Override(true);fog.meanFreePath.Override(190);fog.baseHeight.Override(0);fog.maximumHeight.Override(35);EditorUtility.SetDirty(volume.sharedProfile);}}
    }
    static void PaintArtGround(TerrainData baseline)
    {
        int n=data.alphamapWidth;var alpha=baseline.GetAlphamaps(0,0,n,n);var sz=data.size;var o=terrain.transform.position;
        // Clone layers so this map's wet banks do not change other demo scenes.
        var layers=baseline.terrainLayers.Select((l,i)=>{
            var copy=Object.Instantiate(l);copy.smoothness=.08f;copy.maskMapRemapMin=new Vector4(0,0,0,0);copy.maskMapRemapMax=new Vector4(0,1,1,i==6?.28f:.17f);copy.normalScale=.45f;
            if(i==5){copy.diffuseTexture=(Texture2D)artSoil.GetTexture("_BaseColorMap");copy.normalMapTexture=(Texture2D)artSoil.GetTexture("_NormalMap");copy.maskMapTexture=(Texture2D)artSoil.GetTexture("_MaskMap");copy.tileSize=new Vector2(2.3f,2.3f);copy.maskMapRemapMax=new Vector4(0,1,1,1);}
            return SaveArt(copy,"TerrainLayer_"+i);}).ToArray();data.terrainLayers=layers;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {var p=new Vector2(o.x+x*sz.x/(n-1),o.z+z*sz.z/(n-1));float creek=LineDist(p,ArtCreek);int layer=-1;float w=0;
            if(creek<6){layer=creek<2.4f?7:5;w=(1-Smooth(3,6,creek))*.9f;}
            foreach(var patch in groundPatches){float a=(1-Smooth(patch.radius*.35f,patch.radius,Vector2.Distance(p,patch.p)))*.85f;if(a>w){w=a;layer=patch.layer;}}
            if(layer<0){layer=1;w=.25f*Mathf.PerlinNoise(p.x*.065f+12,p.y*.065f);}
            for(int k=0;k<layers.Length;k++)alpha[z,x,k]=alpha[z,x,k]*(1-w)+(k==layer?w:0);
        }data.SetAlphamaps(0,0,alpha);
        // Tone down saturated foliage on this scene's renderer instances only.
        var mats=new Dictionary<Material,Material>();
        foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if(!(r.name.Contains("Bush")||r.name.Contains("Grass")||r.name.Contains("Fern")))continue;
            var originals=r.sharedMaterials;for(int i=0;i<originals.Length;i++){var m=originals[i];if(!m||m.name.StartsWith("Foliage_"))continue;if(!mats.TryGetValue(m,out var subdued)){
                if(r.transform.IsChildOf(art)&&r.name.Contains("Grass")){
                    subdued=new Material(Shader.Find("HDRP/Lit"));subdued.SetTexture("_BaseColorMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TerrainDemoScene_HDRP/Prefabs/Details/Textures/Grass_Dry_BaseColor.tif"));
                    subdued.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/TerrainDemoScene_HDRP/Prefabs/Details/Textures/Grass_Dry_Normal.tif"));subdued.SetFloat("_AlphaCutoffEnable",1);subdued.SetFloat("_AlphaCutoff",.4f);subdued.SetFloat("_DoubleSidedEnable",1);subdued.SetFloat("_Smoothness",.1f);subdued.SetColor("_BaseColor",new Color(.55f,.51f,.35f));HDMaterial.ValidateMaterial(subdued);
                }else{subdued=new Material(m);if(subdued.HasProperty("_BaseColor"))subdued.SetColor("_BaseColor",new Color(.63f,.65f,.51f));}
                subdued=SaveArt(subdued,"Foliage_"+m.name.Replace('/','_'));mats[m]=subdued;}originals[i]=subdued;}r.sharedMaterials=originals;
        }
    }
    static void ArtForestAndBackdrop(TerrainData baseline)
    {
        var trees=data.treeInstances.ToList();var sz=data.size;var o=terrain.transform.position;int added=0;
        for(int k=0;k<5000&&added<850;k++)
        {var p=C+Random.insideUnitCircle*290;if(Inside(p,Outer)||LineDist(p,Outer)<6||LineDist(p,ArtCreek)<6)continue;
            if(trees.Any(t=>(new Vector2(o.x+t.position.x*sz.x,o.z+t.position.z*sz.z)-p).sqrMagnitude<16))continue;
            float scale=Random.Range(.8f,1.4f);trees.Add(new TreeInstance{position=new Vector3((p.x-o.x)/sz.x,(Ground(p)-o.y)/sz.y,(p.y-o.z)/sz.z),prototypeIndex=Random.Range(0,data.treePrototypes.Length),widthScale=scale,heightScale=scale*Random.Range(.9f,1.2f),rotation=Random.value*Mathf.PI*2,color=Color.white,lightmapColor=Color.white});added++;}
        data.SetTreeInstances(trees.ToArray(),true);terrain.treeDistance=380;
        FogPatch("Valley_atmospheric_depth",new Vector3(C.x,18,C.y+150),new Vector3(550,45,170),130);
    }
    static void RefineChapelSkyline()
    {
        var chapel=GameObject.Find("CentralChapel_Scale_1_25");if(!chapel)return;
        var secondary=chapel.transform.Find("Chapel_SecondaryTurret");
        if(!secondary)
        {
            secondary=new GameObject("Chapel_SecondaryTurret").transform;secondary.position=new Vector3(22.1f,1,-18.54f);secondary.SetParent(chapel.transform,true);
            foreach(var r in chapel.GetComponentsInChildren<MeshRenderer>().ToArray())
            {
                var b=r.bounds;
                if(b.center.x<20||b.center.z>-17.15f||b.size.x>6.1f||b.size.z>6.1f)continue;
                r.transform.SetParent(secondary,true);
            }
            secondary.localScale=new Vector3(secondary.localScale.x,secondary.localScale.y*.58f,secondary.localScale.z);
        }
        var north=chapel.transform.Find("Chapel_NorthTurret");
        if(!north)
        {
            north=new GameObject("Chapel_NorthTurret").transform;north.position=new Vector3(22.1f,1,-6.85f);north.SetParent(chapel.transform,true);
            foreach(var r in chapel.GetComponentsInChildren<MeshRenderer>().ToArray())
            {var b=r.bounds;if(b.center.x>20&&b.center.z>-8.3f&&b.size.x<6.1f&&b.size.z<6.1f)r.transform.SetParent(north,true);}
            north.localScale=new Vector3(north.localScale.x,north.localScale.y*.55f,north.localScale.z);
        }
        var slate=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Cottage_Charcoal_Roof.mat");
        foreach(var r in chapel.GetComponentsInChildren<MeshRenderer>())if(r.name.StartsWith("Cone")&&slate)r.sharedMaterial=slate;
    }
    public static void FinalizeArt()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        art=GameObject.Find("Elderboom_Environment_Art").transform;RefineChapelSkyline();
        foreach(var r in art.GetComponentsInChildren<MeshRenderer>())GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.BatchingStatic);
        foreach(var guid in AssetDatabase.FindAssets("",new[]{ArtFolder}))
        {var path=AssetDatabase.GUIDToAssetPath(guid);var asset=AssetDatabase.LoadMainAssetAtPath(path);if(asset&&!(asset is DefaultAsset)){asset.name=Path.GetFileNameWithoutExtension(path);EditorUtility.SetDirty(asset);}}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }
    public static void ArtAudit()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var s = new StringBuilder();
        foreach (var st in Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None))
            foreach(var r in st.GetComponentsInChildren<Renderer>(true)) s.AppendLine("STATION "+PathOf(r.transform)+" "+r.bounds+" "+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"NULL")));
        var port=GameObject.Find("Elderboom_Village_Port");
        if(port)foreach(Transform t in port.transform)
            if(t.GetComponentInChildren<Renderer>())s.AppendLine("PROP "+t.name+" "+RB(t.gameObject));
        foreach(var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            if(mf.sharedMesh && (mf.sharedMesh.name=="Cube" || mf.sharedMesh.name=="Plane"))s.AppendLine("PRIMITIVE "+PathOf(mf.transform)+" "+mf.GetComponent<Renderer>()?.bounds);
        foreach(var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))s.AppendLine("TERRAIN "+t.name+" at="+t.transform.position+" size="+t.terrainData.size+" layers="+string.Join(",",t.terrainData.terrainLayers.Select(l=>l.name)));
        foreach(var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))s.AppendLine("VOLUME "+v.name+" "+AssetDatabase.GetAssetPath(v.sharedProfile));
        Directory.CreateDirectory("Logs/ArtReview");File.WriteAllText("Logs/ArtReview/art-audit.txt",s.ToString());
    }
}
