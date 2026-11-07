using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

public static class ElderboomArchitectureImport
{
    const string Root="Assets/GameAssets/Environment/ArchitectureKit";
    [Serializable] class GateSpec {public string scene_name,area,other_area;public int cost;public float clear_width_m,open_angle;}
    [Serializable] class Spec {public string name;public float[] dimensions_xyz_m;public GateSpec gate;}
    [Serializable] class MaterialSpec {public string name;public int index;public float[] base_color;public float roughness,metallic,transmission;}
    [Serializable] class Validation {public MaterialSpec[] materials;}
    static double nextCheck;
    [InitializeOnLoadMethod] static void Schedule(){EditorApplication.update-=CheckRequest;EditorApplication.update+=CheckRequest;}
    static void CheckRequest(){if(EditorApplication.timeSinceStartup<nextCheck)return;nextCheck=EditorApplication.timeSinceStartup+2;Requested();}
    static void Requested()
    {
        const string request="Logs/ArchitectureKit/import.request";
        if(!File.Exists(request))return;
        if(EditorApplication.isUpdating||EditorApplication.isCompiling||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try{Run();File.WriteAllText("Logs/ArchitectureKit/import.complete",DateTime.UtcNow.ToString("O"));}
        catch(Exception e){File.WriteAllText("Logs/ArchitectureKit/import-error.txt",e.ToString());Debug.LogException(e);}
    }
    [MenuItem("Tools/Elderboom/Architecture Kit/Import Verified Packages")]
    public static void Run()
    {
        var checks=new List<string>();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        // Remove only abandoned temporary roots from the initial failed import.
        foreach(var obj in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t=>t.parent==null&&t.name=="bakers_house").ToArray())
            if(obj.Cast<Transform>().Any(c=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(c.gameObject)==Root+"/bakers_house/model.fbx"))Object.DestroyImmediate(obj.gameObject);
        foreach(var folder in Directory.GetDirectories(Root).OrderBy(p=>p))
        {
            if(!File.Exists(folder+"/model.fbx"))continue;
            var spec=JsonUtility.FromJson<Spec>(File.ReadAllText(folder+"/spec.json"));
            AssetDatabase.ImportAsset(folder+"/model.fbx",ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(folder+"/model.fbx");
            if(importer==null)throw new Exception("Model importer unavailable: "+folder);
            importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.globalScale=1;importer.SaveAndReimport();
            var materials=new Dictionary<int,Material>();
            var validation=JsonUtility.FromJson<Validation>(File.ReadAllText(folder+"/validation.json"));
            foreach(var diffuse in Directory.GetFiles(folder,"*_diffuse.png"))
            {
                string prefix=Path.GetFileName(diffuse).Split('_')[0];int index=int.Parse(prefix);
                foreach(string suffix in new[]{"diffuse","normal","mask"})
                {
                    string path=folder+"/"+prefix+"_"+suffix+".png";if(!File.Exists(path))continue;
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);if(ti==null)throw new Exception("Texture importer unavailable: "+path);ti.maxTextureSize=2048;ti.sRGBTexture=suffix=="diffuse";ti.textureType=suffix=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;ti.SaveAndReimport();
                }
                string mp=folder+"/PBR_"+index+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);
                if(mat==null){mat=new Material(Shader.Find("HDRP/Lit"));AssetDatabase.CreateAsset(mat,mp);}
                mat.SetColor("_BaseColor",Color.white);mat.SetTexture("_BaseColorMap",AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse));
                mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+prefix+"_normal.png"));mat.SetTexture("_MaskMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+prefix+"_mask.png"));
                HDMaterial.ValidateMaterial(mat);EditorUtility.SetDirty(mat);materials[index]=mat;
            }
            foreach(var ms in validation.materials)
            {
                if(materials.ContainsKey(ms.index))continue;
                string mp=folder+"/PBR_"+ms.index+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);
                if(mat==null){mat=new Material(Shader.Find("HDRP/Lit"));AssetDatabase.CreateAsset(mat,mp);}
                var c=ms.base_color??new[]{.12f,.12f,.12f,1f};
                mat.SetColor("_BaseColor",new Color(c[0],c[1],c[2],ms.transmission>.5f?.22f:1));mat.SetFloat("_Metallic",ms.metallic);mat.SetFloat("_Smoothness",1-ms.roughness);
                if(ms.transmission>.5f){mat.SetFloat("_SurfaceType",1);mat.SetFloat("_BlendMode",0);mat.SetFloat("_ZWrite",0);mat.SetFloat("_DoubleSidedEnable",1);mat.renderQueue=(int)RenderQueue.Transparent;}
                HDMaterial.ValidateMaterial(mat);EditorUtility.SetDirty(mat);materials[ms.index]=mat;
            }
            var root=new GameObject(spec.name);var source=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/model.fbx");
            var previewScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,previewScene);
            try{
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source);model.transform.SetParent(root.transform,false);
            var renderers=model.GetComponentsInChildren<MeshRenderer>();
            foreach(var r in renderers){var match=System.Text.RegularExpressions.Regex.Match(r.name,@"_MAT(\d+)");int mi=match.Success?int.Parse(match.Groups[1].Value):0;r.sharedMaterials=r.sharedMaterials.Select((_,i)=>materials.ContainsKey(mi)?materials[mi]:materials.Values.First()).ToArray();}
            AddLODs(model,renderers);
            if(spec.gate!=null&&!string.IsNullOrEmpty(spec.gate.scene_name))BuildGate(root,model,spec);
            else foreach(var mf in model.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.Contains("LOD0")))mf.gameObject.AddComponent<MeshCollider>().sharedMesh=mf.sharedMesh;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())foreach(var mat in renderer.sharedMaterials)
                foreach(var property in new[]{"_BaseColorMap","_NormalMap","_MaskMap"})
                {var texture=mat.GetTexture(property);if(texture!=null&&(texture.width>2048||texture.height>2048))throw new Exception(spec.name+" texture exceeds 2K");}
            var motion=root.GetComponent<ElderboomGateLeaves>();
            if(motion!=null)
            {
                var closedLeft=motion.leftHinge.localRotation;var closedRight=motion.rightHinge.localRotation;
                motion.leftHinge.localRotation=closedLeft*Quaternion.Euler(0,-motion.openingDegrees,0);motion.rightHinge.localRotation=closedRight*Quaternion.Euler(0,motion.openingDegrees,0);
                float leftMax=motion.leftHinge.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.max.x),rightMin=motion.rightHinge.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.x);
                float clearance=rightMin-leftMax;
                if(clearance<spec.gate.clear_width_m-.3f)throw new Exception(spec.name+" open passage obstructed: "+clearance);
                checks.Add(spec.name+" cost="+spec.gate.cost+" clearWidth="+spec.gate.clear_width_m+" openedClearance="+clearance+" twoHinges=true texturesMax=2048");
                motion.leftHinge.localRotation=closedLeft;motion.rightHinge.localRotation=closedRight;
            }
            else checks.Add(spec.name+" texturesMax=2048 LODs=3");
            PrefabUtility.SaveAsPrefabAsset(root,folder+"/"+spec.name+".prefab");Object.DestroyImmediate(root);
            }finally{UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);}
        }
        AssetDatabase.SaveAssets();File.WriteAllLines("Logs/ArchitectureKit/prefab-validation.txt",checks);Debug.Log("ARCHITECTURE_KIT_IMPORTED");
    }
    static void AddLODs(GameObject root,MeshRenderer[] renderers)
    {
        var group=root.GetComponent<LODGroup>();if(group==null)group=root.AddComponent<LODGroup>();var thresholds=new[]{.35f,.13f,.025f};
        group.SetLODs(Enumerable.Range(0,3).Select(i=>new LOD(thresholds[i],renderers.Where(r=>r.name.Contains("LOD"+i+"_")).Cast<Renderer>().ToArray())).ToArray());group.RecalculateBounds();
    }
    static void BuildGate(GameObject root,GameObject model,Spec spec)
    {
        float width=spec.gate.clear_width_m,height=spec.dimensions_xyz_m[1],leaf=spec.dimensions_xyz_m[0],depth=spec.dimensions_xyz_m[2];
        Bounds LeafBounds(){var rs=model.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("LOD0")).ToArray();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        // FBX axis conversion can reverse X. Align the imported hinge edge once,
        // before attaching it to either pivot, with the panel extending along +X.
        if(LeafBounds().center.x<0)model.transform.localRotation=Quaternion.Euler(0,180,0)*model.transform.localRotation;
        var lb=LeafBounds();model.transform.localPosition+=new Vector3(-lb.min.x,-lb.min.y,-lb.center.z);
        var left=new GameObject("LeftHinge");left.transform.SetParent(root.transform,false);left.transform.localPosition=new Vector3(-width*.5f,-1.35f,0);model.transform.SetParent(left.transform,false);
        var right=new GameObject("RightHinge");right.transform.SetParent(root.transform,false);right.transform.localPosition=new Vector3(width*.5f,-1.35f,0);right.transform.localRotation=Quaternion.Euler(0,180,0);
        var copy=Object.Instantiate(model,right.transform,false);copy.name="RightLeaf";
        var leafCols=new List<Collider>();foreach(var h in new[]{left,right}){var c=h.AddComponent<BoxCollider>();c.center=new Vector3(leaf*.5f,height*.5f,0);c.size=new Vector3(leaf,height,depth);leafCols.Add(c);}
        var block=root.AddComponent<BoxCollider>();block.size=new Vector3(width,Mathf.Max(3.1f,height),Mathf.Max(.5f,depth));
        var logic=root.AddComponent<ElderboomGate>();logic.areaName=spec.gate.area;logic.otherAreaName=spec.gate.other_area;logic.cost=spec.gate.cost;
        var motion=root.AddComponent<ElderboomGateLeaves>();motion.leftHinge=left.transform;motion.rightHinge=right.transform;motion.openingDegrees=spec.gate.open_angle;motion.leafColliders=leafCols.ToArray();
        // Stationary supports lie outside the preserved clear passage.
        string supportPath=Root+"/GateSupports.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(supportPath);
        if(material==null){material=new Material(Shader.Find("HDRP/Lit"));material.SetColor("_BaseColor",new Color(.09f,.065f,.045f));material.SetFloat("_Smoothness",.22f);AssetDatabase.CreateAsset(material,supportPath);}
        foreach(float side in new[]{-1f,1f})
        {
            var post=GameObject.CreatePrimitive(PrimitiveType.Cube);post.name="StationaryPost";post.transform.SetParent(root.transform,false);post.transform.localPosition=new Vector3(side*(width*.5f+.13f),-1.35f+height*.5f,0);post.transform.localScale=new Vector3(.24f,height+.12f,.24f);post.GetComponent<Renderer>().sharedMaterial=material;
        }
    }
}
