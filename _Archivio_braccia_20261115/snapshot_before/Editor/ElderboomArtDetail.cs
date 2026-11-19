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
using Random = UnityEngine.Random;

static partial class ElderboomClaudeBlocks
{
    const string ModelFolder="Assets/ThirdParty/PolyHaven/Models/";
    static readonly Vector2[] PuddleSites={
        new Vector2(-17.8f,1.5f),new Vector2(-30.4f,-14.1f),new Vector2(-5.1f,2.1f),
        new Vector2(42.8f,21.4f),new Vector2(-58.2f,-13.2f),new Vector2(-62.0f,-18.1f),
        new Vector2(-64.1f,-21.0f),new Vector2(-23.3f,64.9f),new Vector2(-18.8f,60.5f),
        new Vector2(-22.4f,70.9f),new Vector2(2.8f,42.1f),new Vector2(52.7f,-2.1f),
        new Vector2(62.5f,-19.8f),new Vector2(9.3f,-65.3f),new Vector2(-17.5f,-61.3f)
    };
    static void BuildPuddles()
    {
        var group=ArtGroup("Rain_Puddles");var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainDemoScene_HDRP/Prefabs/Water/Materials/WaterDepthBased.mat");
        var mat=new Material(source);mat.SetFloat("_NormalStrength",.012f);mat.SetFloat("_Smoothness",.92f);mat.SetFloat("_Speed",.007f);mat.SetVector("_Tiling",new Vector4(2,2,0,0));mat.SetFloat("_DepthBiasAlpha",8);mat.SetFloat("_DepthContrast",.7f);mat.SetFloat("_Rim_Falloff",.25f);mat.SetFloat("_Distortion",0);mat.SetFloat("_Rim_Smoothness",.8f);
        mat.SetColor("_Deep_Water_Color",new Color(.055f,.058f,.043f,0));mat.SetColor("_Shallow_Water_Color",new Color(.11f,.115f,.08f,0));mat=SaveArt(mat,"RainPuddleWater");
        int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var o=terrain.transform.position;var sz=data.size;
        for(int index=0;index<PuddleSites.Length;index++)
        {
            var p=PuddleSites[index];float rx=index%3==0?1.65f:1.15f,rz=index%3==0?.75f:1.4f;
            float yaw=(index*41+17)*Mathf.Deg2Rad,co=Mathf.Cos(yaw),si=Mathf.Sin(yaw);float level=Ground(p)+.008f;
            Vector2 Local(Vector2 v)=>new Vector2((v.x*co-v.y*si)/rx,(v.x*si+v.y*co)/rz);
            float Shape(Vector2 v){float a=Mathf.Atan2(v.y,v.x);return 1+.13f*Mathf.Sin(a*3+index)+.075f*Mathf.Sin(a*7+index*.7f);}
            for(int z=Mathf.Max(0,Mathf.FloorToInt((p.y-rz*2-o.z)/sz.z*(n-1)));z<=Mathf.Min(n-1,Mathf.CeilToInt((p.y+rz*2-o.z)/sz.z*(n-1)));z++)
            for(int x=Mathf.Max(0,Mathf.FloorToInt((p.x-rx*2-o.x)/sz.x*(n-1)));x<=Mathf.Min(n-1,Mathf.CeilToInt((p.x+rx*2-o.x)/sz.x*(n-1)));x++)
            {
                var v=Local(new Vector2(o.x+x*sz.x/(n-1)-p.x,o.z+z*sz.z/(n-1)-p.y));float dist=v.magnitude/Shape(v);if(dist>1.35f)continue;
                float target=(level-.14f-o.y)/sz.y;heights[z,x]=Mathf.Lerp(heights[z,x],target,1-Smooth(.55f,1.3f,dist));
            }
            // The uneven shore is cut by the surrounding terrain, with no collider on water.
            var vertices=new List<Vector3>{new Vector3(p.x,level,p.y)};var uv=new List<Vector2>{Vector2.one*.5f};var tris=new List<int>();
            for(int k=0;k<=40;k++)
            {float a=k*Mathf.PI*2/40;var v=new Vector2(Mathf.Cos(a),Mathf.Sin(a));v*=Shape(v)*1.18f;
                var offset=new Vector2(v.x*rx*co+v.y*rz*si,-v.x*rx*si+v.y*rz*co);vertices.Add(new Vector3(p.x+offset.x,level,p.y+offset.y));uv.Add(v*.5f+Vector2.one*.5f);
                if(k>0)tris.AddRange(new[]{0,k+1,k});}
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            MeshArt("Rain_puddle_"+index,group,SaveArt(mesh,"RainPuddle_"+index),mat);
            groundPatches.Add((p,Mathf.Max(rx,rz)*1.65f,5));
        }
        data.SetHeights(0,0,heights);
    }
    static GameObject LicensedModel(string id,Transform parent,Vector2 p,float height,float yaw=0,bool solid=false)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder+id+"/"+id+"_2k.gltf");
        if(!prefab)throw new InvalidOperationException("Missing licensed model: "+id);
        var go=Object.Instantiate(prefab,parent);go.name=id;go.transform.rotation=Quaternion.Euler(0,yaw,0);
        var b=RB(go);go.transform.localScale*=height/Mathf.Max(.01f,b.size.y);SetCenter(go,At(p));Seat(go,p,.025f);
        if(solid)SolidMeshes(go);return go;
    }

    static void LicensedWorkyardDetails()
    {
        var yard=ArtGroup("Sawmill_Receiving_And_Cutting",art);
        // Receiving stack, a log at the cutting trestles, and waste are separate operations.
        for(int row=0;row<3;row++)for(int j=0;j<3-row;j++)
        {
            var p=W(-23.5f+j*.76f+row*.35f,58+Random.Range(-.35f,.35f));
            var log=LicensedModel("dead_tree_trunk_02",yard,p,.66f,88+Random.Range(-4,4),true);
            log.transform.position+=Vector3.up*row*.51f;groundPatches.Add((p,3.6f,5));
        }
        var bench=At(W(-28,69));
        foreach(float x in new[]{-1.1f,1.1f})foreach(float z in new[]{-.36f,.36f})
            ArtBox("Sawbench_leg",yard,bench+new Vector3(x,.45f,z),new Vector3(.11f,.9f,.11f),artWood,Quaternion.Euler(0,0,x<0?-6:6),true);
        for(int i=0;i<4;i++)ArtBox("Sawbench_top",yard,bench+new Vector3(0,.94f,(i-1.5f)*.22f),new Vector3(2.7f,.13f,.21f),artWood,default,true);
        var saw=LicensedModel("handsaw_wood",yard,W(-28.4f,69),.16f,27);saw.transform.position+=Vector3.up*1.02f;
        LicensedModel("tree_stump_02",yard,W(-25,65.5f),.55f,27,true);
        LicensedModel("wooden_bucket_01",yard,W(-26.4f,70.2f),.49f,19);
        for(int i=0;i<11;i++)
        {
            var p=W(-24.4f+Random.Range(-1.4f,1.4f),65+Random.Range(-1.5f,1.5f));
            ArtBox("Offcut",yard,At(p,.06f),new Vector3(Random.Range(.3f,.8f),.08f,Random.Range(.055f,.12f)),artWood,Quaternion.Euler(0,Random.Range(0,180),Random.Range(-8,8)));
        }
        // Stored boards are supported, not laid in identical rows on the lawn.
        for(int layer=0;layer<4;layer++)for(int j=0;j<3;j++)
            ArtBox("Seasoning_board",yard,At(W(-20.5f+j*.33f,63),.2f+layer*.15f),new Vector3(.3f,.09f,3.6f+Random.Range(-.2f,.2f)),artWood,Quaternion.Euler(0,-8,0));
        foreach(float z in new[]{61.8f,64.2f})ArtBox("Stack_bearer",yard,At(W(-20.2f,z),.1f),new Vector3(1.7f,.2f,.17f),artWood);
        groundPatches.Add((W(-25,65),5,7));

        var cemetery=art.Find("Cemetery_Consecrated_Ground");
        LicensedModel("tree_stump_02",cemetery,W(80,-21),.65f,112,true);
        LicensedModel("dead_tree_trunk_02",cemetery,W(78,-27.6f),.8f,12,true);
        var farm=art.Find("Farm_Working_Yard");
        LicensedModel("wooden_bucket_01",farm,W(-63.5f,-11),.5f,41);
        // Domestic ladders belong against house walls, not through the farm cart.
    }

    static void DressDomesticPlots()
    {
        var plots=ArtGroup("Village_Household_Compounds");
        // Surveyed wall faces, with the normal pointing out into their own courtyard.
        AttachedWorkshop(plots,"North_baker_back_kitchen",new Vector2(-5.72f,4.85f),180,3.5f,1.8f,false);
        AttachedWorkshop(plots,"West_carpenter_workroom",new Vector2(-27.32f,-13.9f),270,4.3f,2.3f,true);
        AttachedWorkshop(plots,"East_wash_house",new Vector2(43.8f,18.28f),0,3.3f,2.1f,false);
        // Short return fences meet the facade and define lots, with an actual entrance gap.
        FenceArt(plots,new Vector2(-7.8f,4.8f),new Vector2(-7.8f,1));
        FenceArt(plots,new Vector2(-7.8f,1),new Vector2(-5.5f,1));
        FenceArt(plots,new Vector2(-27.4f,-18.1f),new Vector2(-32.8f,-18.1f));
        FenceArt(plots,new Vector2(-32.8f,-18.1f),new Vector2(-32.8f,-10.2f));
        FenceArt(plots,new Vector2(-32.8f,-10.2f),new Vector2(-30.5f,-10.2f));
        FenceArt(plots,new Vector2(40.8f,18.3f),new Vector2(40.8f,23.2f));
        FenceArt(plots,new Vector2(40.8f,23.2f),new Vector2(44,23.2f));
        Prop("S_Chopped_Wood_Pile",plots,new Vector2(-30,-17),92,.48f);
        Prop("S_Old_Wooden_Bench",plots,new Vector2(-3.6f,3.1f),180,.9f);
        Prop("S_Old_Wooden_Trough",plots,new Vector2(42.4f,22.5f),90,.7f);
        foreach(var p in new[]{new Vector2(-6,2.5f),new Vector2(-30.4f,-14),new Vector2(43,20.5f)})groundPatches.Add((p,3,5));
        RoofRepairs(plots);
        InteriorGlimpse(plots,"Bakers_storage_window","S_Medieval_Modular_Door_ueoqceudw_lod3_Var1",180);
        InteriorGlimpse(plots,"Washhouse_storage_window","S_Medieval_Modular_Door_ueoqceudw_lod3_Var81",0);
    }

    static void InteriorGlimpse(Transform parent,string name,string sourceName,float yaw)
    {
        var port=GameObject.Find("Elderboom_Village_Port");var source=port.transform.Find(sourceName);if(!source)return;
        var renderers=source.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return;var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        // This particular panel is replaced, leaving the original asset recoverable.
        foreach(var r in renderers)r.enabled=false;foreach(var c in source.GetComponentsInChildren<Collider>(true))c.enabled=false;
        var group=ArtGroup(name,parent);var q=Quaternion.Euler(0,yaw,0);var origin=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        Vector3 P(float x,float y,float z)=>origin+q*new Vector3(x,y,z);
        var plaster=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Cottage_Worn_Plaster.mat");
        const float opening=.92f, sill=.86f, top=1.78f;
        foreach(float side in new[]{-1f,1f})ArtBox("Window_wall",group,P(side*.98f,1,0),new Vector3(1.04f,2,.18f),plaster,q,true);
        ArtBox("Window_sill_wall",group,P(0,sill*.5f,0),new Vector3(opening,sill,.18f),plaster,q,true);
        ArtBox("Window_header_wall",group,P(0,(2+top)*.5f,0),new Vector3(opening,2-top,.18f),plaster,q,true);
        foreach(float x in new[]{-.49f,.49f})ArtBox("Window_jamb",group,P(x,(sill+top)*.5f,.12f),new Vector3(.08f,top-sill+.14f,.16f),artWood,q);
        foreach(float y in new[]{sill,top})ArtBox("Window_wood_frame",group,P(0,y,.12f),new Vector3(1.1f,.085f,.2f),artWood,q);
        var roomMat=ArtMaterial("Interior_Dark_Plaster",new Color(.24f,.21f,.16f),.06f,plaster);
        ArtBox("Visible_room_back",group,P(0,1,-1.05f),new Vector3(2.9f,2,.12f),roomMat,q,true);
        ArtBox("Visible_room_floor",group,P(0,.035f,-.55f),new Vector3(2.9f,.07f,1.1f),artWood,q,true);
        ArtBox("Visible_room_ceiling",group,P(0,1.98f,-.55f),new Vector3(2.9f,.06f,1.1f),artWood,q);
        ArtBox("Storage_shelf",group,P(0,1.04f,-.66f),new Vector3(1.6f,.065f,.48f),artWood,q);
        foreach(float x in new[]{-.68f,.68f})ArtBox("Shelf_support",group,P(x,.51f,-.66f),new Vector3(.09f,1.02f,.09f),artWood,q);
        var at=P(.2f,0,-.65f);var bucket=LicensedModel("wooden_bucket_01",group,new Vector2(at.x,at.z),.37f,yaw+12);var b=RB(bucket);bucket.transform.position+=Vector3.up*(origin.y+1.08f-b.min.y);
        // One open shutter and one partially closed shutter create a framed glimpse.
        foreach(float side in new[]{-1f,1f})
        {
            var hinge=P(side*.52f,(sill+top)*.5f,.17f);var rot=q*Quaternion.Euler(0,side*(side<0?112:37),0);
            var center=hinge+rot*Vector3.right*(-side*.24f);
            for(int i=0;i<3;i++)ArtBox("Old_shutter_plank",group,center+rot*Vector3.right*((i-1)*.155f),new Vector3(.145f,.94f,.045f),artWood,rot);
            foreach(float y in new[]{-.31f,.31f})ArtBox("Shutter_strap",group,center+Vector3.up*y+rot*Vector3.forward*.028f,new Vector3(.44f,.035f,.018f),artIron,rot);
        }
        var lamp=new GameObject("Interior_oil_lamp_light");lamp.transform.SetParent(group);lamp.transform.position=P(-.55f,1.65f,-.65f);
        var light=lamp.AddComponent<Light>();light.type=LightType.Point;lamp.AddComponent<HDAdditionalLightData>();light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=32;light.range=2.1f;light.color=new Color(1,.66f,.35f);
        ArtBox("Shelf_oil_lamp",group,P(-.55f,1.17f,-.65f),new Vector3(.1f,.18f,.1f),artIron,q);
    }

    static void AttachedWorkshop(Transform parent,string name,Vector2 face,float yaw,float width,float depth,bool workshop)
    {
        var group=ArtGroup(name,parent);var q=Quaternion.Euler(0,yaw,0);var origin=At(face);
        Vector3 P(float x,float y,float z)=>origin+q*new Vector3(x,y,z);
        Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        // Rear edge overlaps the surveyed facade by 8 cm. New roof is below its eaves.
        for(int i=0;i<=Mathf.CeilToInt(width/.26f);i++)
            ArtBox("Patched_shed_roof",group,P(-width*.5f+i*.26f,2.15f,depth*.5f),new Vector3(.275f,.06f,depth+.3f),artWood,q*Quaternion.Euler(8,0,0));
        foreach(float x in new[]{-width*.5f,width*.5f})
        {
            ArtBox("Annex_front_post",group,P(x,1.01f,depth),new Vector3(.15f,2.02f,.15f),artWood,q,true);
            ArtBox("Annex_brace",group,P(x>0?x-.3f:x+.3f,1.7f,depth),new Vector3(.1f,.75f,.1f),artWood,q*Quaternion.Euler(0,0,x>0?-42:42));
        }
        ArtBox("Annex_header",group,P(0,2,depth),new Vector3(width+.25f,.17f,.17f),artWood,q);
        // One wall, open front and visible interior storage; the source doorway stays intact.
        for(int i=0;i<7;i++)ArtBox("Weatherboard_side",group,P(-width*.5f,.16f+i*.28f,depth*.5f),new Vector3(.075f,.25f,depth),artWood,q,true);
        ArtBox("Interior_shelf",group,P(-width*.28f,.95f,depth*.32f),new Vector3(width*.38f,.07f,.5f),artWood,q);
        foreach(float x in new[]{-width*.46f,-width*.10f})ArtBox("Shelf_leg",group,P(x,.46f,depth*.32f),new Vector3(.07f,.92f,.07f),artWood,q);
        var bucket=LicensedModel("wooden_bucket_01",group,XZ(P(width*.26f,0,depth*.6f)),.44f,yaw+23);
        var pot=Prop("S_Old_Metal_Pot",group,XZ(P(-width*.25f,0,depth*.32f)),yaw,.55f,false);
        if(pot)pot.transform.position+=Vector3.up*.99f;
        var ladder=LicensedModel("wooden_ladder",group,XZ(P(width*.3f,0,.35f)),1.55f,yaw+12);
        ladder.transform.rotation=q*Quaternion.Euler(-12,12,0);Seat(ladder,XZ(P(width*.3f,0,.35f)),.02f);
        if(workshop){var saw=LicensedModel("handsaw_wood",group,XZ(P(-width*.16f,0,depth*.42f)),.14f,yaw+54);saw.transform.position+=Vector3.up*1.01f;}
        // One sheltered domestic source, lower than the entrance lanterns.
        var lamp=new GameObject("Sheltered_warm_light");lamp.transform.SetParent(group);lamp.transform.position=P(-width*.25f,1.7f,.22f);
        var light=lamp.AddComponent<Light>();light.type=LightType.Point;lamp.AddComponent<HDAdditionalLightData>();light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=75;light.range=3;light.color=new Color(1,.62f,.3f);
        ArtBox("Oil_lamp_body",group,P(-width*.25f,1.65f,.22f),new Vector3(.09f,.16f,.09f),artIron);
    }

    static void RoofRepairs(Transform parent)
    {
        var port=GameObject.Find("Elderboom_Village_Port");
        foreach(var point in new[]{new Vector2(-11.5f,-16),new Vector2(20.8f,12.9f),new Vector2(39,15.2f)})
        {
            var candidates=port.GetComponentsInChildren<MeshFilter>().Where(m=>m.sharedMesh&&m.GetComponent<Renderer>()&&m.GetComponent<Renderer>().bounds.max.y>3.2f).ToArray();
            var hits=new List<(RaycastHit hit,MeshFilter mf)>();
            foreach(var mf in candidates)
            {
                var bounds=mf.GetComponent<Renderer>().bounds;if(point.x<bounds.min.x||point.x>bounds.max.x||point.y<bounds.min.z||point.y>bounds.max.z)continue;
                var probe=new GameObject("Roof_probe");probe.transform.SetPositionAndRotation(mf.transform.position,mf.transform.rotation);probe.transform.localScale=mf.transform.lossyScale;
                var col=probe.AddComponent<MeshCollider>();col.sharedMesh=mf.sharedMesh;
                if(col.Raycast(new Ray(new Vector3(point.x,15,point.y),Vector3.down),out var hit,13)&&hit.normal.y>.4f)hits.Add((hit,mf));
                Object.DestroyImmediate(probe);
            }
            if(hits.Count==0)continue;var surface=hits.OrderByDescending(h=>h.hit.point.y).First().hit;
            var q=Quaternion.FromToRotation(Vector3.up,surface.normal);var group=ArtGroup("Local_roof_repair",parent);
            for(int i=0;i<4;i++)ArtBox("Replacement_shingle_board",group,surface.point+surface.normal*.025f+q*new Vector3((i-1.5f)*.14f,0,0),new Vector3(.13f,.025f,.55f+Random.Range(-.06f,.06f)),artWood,q);
        }
    }

    static Material DetailFoliage(string texture,Color tint)
    {
        var m=new Material(Shader.Find("HDRP/Lit"));string prefix="Assets/TerrainDemoScene_HDRP/Prefabs/Details/Textures/"+texture;
        m.SetTexture("_BaseColorMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_BaseColor.tif"));
        m.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_Normal.tif"));
        m.SetColor("_BaseColor",tint);m.SetFloat("_AlphaCutoffEnable",1);m.SetFloat("_AlphaCutoff",.45f);m.SetFloat("_DoubleSidedEnable",1);m.SetFloat("_Smoothness",.08f);HDMaterial.ValidateMaterial(m);
        return SaveArt(m,"Foliage_Eco_"+texture);
    }
    static void EcoPlant(Transform parent,string prefabName,Material material,Vector2 p,float height,float bend=0)
    {
        var src=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainDemoScene_HDRP/Prefabs/Details/"+prefabName+".prefab");
        int oldCount=parent.childCount;if(!Place(src,parent,p,height,.035f)||parent.childCount==oldCount)return;
        var t=parent.GetChild(parent.childCount-1);t.Rotate(bend,0,0);
        foreach(var r in t.GetComponentsInChildren<Renderer>())r.sharedMaterial=material;
        foreach(var c in t.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
    }
    static void EcologicalDetails()
    {
        var group=ArtGroup("Ecological_Contact_Layers");
        var fern=DetailFoliage("Fern",new Color(.46f,.49f,.3f));var grass=DetailFoliage("Grass_A",new Color(.43f,.46f,.3f));
        var dry=DetailFoliage("Grass_Dry",new Color(.6f,.53f,.37f));
        var samples=CreekSamples();
        for(int i=2;i<samples.Count-2;i+=2)
        {
            var p=samples[i];if(PassDist(p)<6)continue;
            var d=(samples[i+1]-samples[i-1]).normalized;var n=new Vector2(-d.y,d.x);
            foreach(float side in new[]{-1f,1f})for(int j=0;j<2;j++)
            {var at=p+n*side*Random.Range(3,4.7f)+Random.insideUnitCircle*.5f;EcoPlant(group,j==0?"Fern_B":"Grass_A",j==0?fern:grass,at,Random.Range(.25f,.5f),Random.Range(7,20));}
        }
        var o=terrain.transform.position;var sz=data.size;int trees=0;
        foreach(var tree in data.treeInstances)
        {
            var p=new Vector2(o.x+tree.position.x*sz.x,o.z+tree.position.z*sz.z);
            if(Vector2.Distance(p,C)>120||PassDist(p)<7||trees++>120)continue;
            groundPatches.Add((p,Random.Range(2,3.5f),5));
            for(int k=0;k<3;k++)EcoPlant(group,k==0?"Fern_C":"GrassDry_A",k==0?fern:dry,p+Random.insideUnitCircle*2,Random.Range(.2f,.45f));
        }
        foreach(var r in GameObject.Find("Elderboom_Village_Port").GetComponentsInChildren<Renderer>())
        {
            var mats=r.sharedMaterials;bool changed=false;
            for(int i=0;i<mats.Length;i++)
            {
                var m=mats[i];if(!m||!m.name.Contains("BlackAlder")||!m.name.Contains("TwoSided"))continue;
                var toned=new Material(m);toned.SetFloat("_Smoothness",.08f);toned.SetFloat("_SmoothnessRemapMax",.12f);toned.SetFloat("_Metallic",0);toned.SetColor("_EmissiveColor",Color.black);toned.SetColor("_BaseColor",new Color(.52f,.55f,.42f));
                mats[i]=SaveArt(toned,"Foliage_Eco_"+m.name.Replace("Foliage_Eco_",""));changed=true;
            }
            if(changed)r.sharedMaterials=mats;
        }
        AddCreekFoam();
    }

    static void AddCreekFoam()
    {
        var tex=new Texture2D(128,256,TextureFormat.RGBA32,true);var pixels=new Color[128*256];
        for(int y=0;y<256;y++)for(int x=0;x<128;x++)
        {
            float u=x/127f,v=y/255f;float noise=Mathf.PerlinNoise(u*27,v*40);
            float threads=1-Smooth(.025f,.085f,Mathf.Abs(noise-.49f));
            float edge=Mathf.Pow(Mathf.Sin(u*Mathf.PI),1.8f)*Mathf.Sin(v*Mathf.PI)*(1-v*.6f);
            pixels[y*128+x]=new Color(.61f,.65f,.59f,threads*edge*.75f);
        }
        tex.SetPixels(pixels);tex.Apply();tex.wrapMode=TextureWrapMode.Clamp;tex.Compress(true);tex.Apply(false,true);
        var m=new Material(Shader.Find("HDRP/Lit"));m.SetTexture("_BaseColorMap",SaveArt(tex,"LocalFoamMask"));m.SetColor("_BaseColor",Color.white);m.SetFloat("_SurfaceType",1);m.SetFloat("_BlendMode",0);m.SetFloat("_TransparentZWrite",0);m.SetFloat("_DoubleSidedEnable",1);m.SetFloat("_Smoothness",.1f);HDMaterial.ValidateMaterial(m);m=SaveArt(m,"LocalCreekFoam");
        var group=ArtGroup("Obstacle_Local_Foam");var creek=art.Find("Creek_Wetland");int count=0;
        foreach(Transform t in creek)
        {
            var renderer=t.GetComponentInChildren<Renderer>();if(!renderer||!t.name.Contains("Rock"))continue;
            var b=renderer.bounds;var p=new Vector2(b.center.x,b.center.z);if(LineDist(p,ArtCreek)>1.6f||b.min.y>CreekWaterLevel)continue;
            var left=new List<Vector3>();var right=new List<Vector3>();float width=Mathf.Clamp(b.size.z*.45f,.22f,.65f);
            for(int k=0;k<6;k++){var center=new Vector3(p.x+b.extents.x*.45f+k*.25f,CreekWaterLevel+.014f,p.y+Mathf.Sin(k*.8f)*.08f);left.Add(center+Vector3.back*width);right.Add(center+Vector3.forward*width);}
            var mesh=Ribbon("FoamWake_"+count,left,right);var uv=mesh.uv;for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(i%2,(i/2)/5f);mesh.uv=uv;
            MeshArt("Foam_at_obstacle_"+count++,group,mesh,m);
        }
    }

    static void RefineContactAndFixtures()
    {
        var port=GameObject.Find("Elderboom_Village_Port");var plants=ArtGroup("Repaired_Garden_Grass");
        var foliage=DetailFoliage("Grass_A",new Color(.47f,.49f,.31f));var grassC=DetailFoliage("Grass_C",new Color(.43f,.45f,.29f));
        // The ported clump material has no atlas; its uncut cards form opaque
        // triangles. Replace these specific instances with the complete demo asset.
        foreach(Transform item in port.transform)
        {
            if(!item.name.StartsWith("S_Grass_Clumps"))continue;
            var renderers=item.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);var p=new Vector2(b.center.x,b.center.z);item.gameObject.SetActive(false);
            EcoPlant(plants,"Grass_A",foliage,p,.38f);
            EcoPlant(plants,"Grass_C",grassC,p+new Vector2(.3f,.18f),.3f);
        }
        var bankMat=new Material(Shader.Find("HDRP/Lit"));
        var rockMat=AssetDatabase.LoadAssetAtPath<Material>("Assets/TerrainDemoScene_HDRP/Prefabs/Rocks/Materials/Rock_A.mat");
        foreach(string prop in new[]{"_BaseColorMap","_NormalMap","_MaskMap"})if(rockMat.HasProperty(prop))bankMat.SetTexture(prop,rockMat.GetTexture(prop));
        bankMat.SetColor("_BaseColor",new Color(.65f,.61f,.53f));bankMat.SetFloat("_Smoothness",.16f);bankMat.SetFloat("_SmoothnessRemapMax",.2f);bankMat.SetFloat("_UVBase",5);bankMat.SetFloat("_TexWorldScale",.42f);HDMaterial.ValidateMaterial(bankMat);bankMat=SaveArt(bankMat,"ErodedBankTriplanar");
        var banks=ArtGroup("Creek_Eroded_Rock_Contact");var curve=CreekSamples();var vertices=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
        for(int i=0;i<curve.Count;i++)
        {
            var p=curve[i];var d=(curve[Mathf.Min(i+1,curve.Count-1)]-curve[Mathf.Max(i-1,0)]).normalized;var south=new Vector2(d.y,-d.x);
            float width=3.1f+.3f*Mathf.Sin(p.x*.19f)+.18f*Mathf.Sin(p.y*.41f);
            for(int j=0;j<6;j++)
            {var at=p+south*(width-1.4f+j*.42f);vertices.Add(At(at,.027f));uv.Add(at*.35f);
                if(i>0&&j>0){int k=i*6+j;tris.AddRange(new[]{k-7,k,k-6,k-7,k-1,k});}}
            // Rock outcrops are clusters anchored in the eroded bank, with bare
            // earth between them. They do not form another evenly spaced wall.
            if(i%8==0&&i>8&&PassDist(p)>7&&Vector2.Distance(p,C)<120)
            {
                var at=p+south*(width-.6f)+d*Random.Range(-1.5f,1.5f);
                var rock=LicensedModel("rock_face_02",banks,at,Random.Range(1.8f,2.6f),Mathf.Atan2(-south.x,-south.y)*Mathf.Rad2Deg+Random.Range(-18,18),true);
                rock.transform.position+=Vector3.down*.35f;
            }
        }
        var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        MeshArt("Cut_bank_surface",banks,SaveArt(mesh,"CutBankFacing"),bankMat);
        // Keep the segheria roof readable with its own sheltered work light.
        var yard=art.Find("Sawmill_Receiving_And_Cutting");if(yard)Lantern(yard,At(W(-28.4f,69.2f),1.65f),300,5);
    }

    public static void DetailSurvey()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var s=new StringBuilder();
        foreach(var guid in AssetDatabase.FindAssets("",new[]{ModelFolder}))
        {
            var path=AssetDatabase.GUIDToAssetPath(guid);if(!path.EndsWith(".gltf"))continue;
            var src=AssetDatabase.LoadAssetAtPath<GameObject>(path);s.AppendLine(path+" imported="+(src!=null));
            if(!src)continue;var go=Object.Instantiate(src);s.AppendLine("  "+RB(go));
            foreach(var r in go.GetComponentsInChildren<Renderer>())s.AppendLine("  "+r.name+" "+string.Join(",",r.sharedMaterials.Select(m=>m?m.name+" "+m.shader.name:"NULL")));
            Object.DestroyImmediate(go);
        }
        var port=GameObject.Find("Elderboom_Village_Port");
        foreach(Transform t in port.transform)
            if(t.name.Contains("Door")||t.name.Contains("Roof")||t.name.Contains("Gable"))s.AppendLine(t.name+" "+RB(t.gameObject)+" rotation="+t.eulerAngles);
        var old=GameObject.Find("Elderboom_Blocks").transform.Find("Invisible_Walls");
        foreach(var b in old.GetComponentsInChildren<BoxCollider>(true).Take(6))s.AppendLine("BOUNDARY "+b.transform.TransformPoint(b.center)+" size="+b.size+" bounds="+b.bounds);
        File.WriteAllText("Logs/ArtReview/After/detail-survey.txt",s.ToString());
    }
}
