using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class ElderboomReviewPreparation
{
    public static void EnsureMuzzle(Transform root,ElderboomViewmodel view)
    {
        string[] known={"Soph_Rifle_Muzzle","HK53_Muzzle","MainWeapon_Muzzle"};
        if(root.GetComponentsInChildren<Transform>(true).Any(t=>known.Contains(t.name)))return;
        var front=ElderboomWeaponFraming.FindNamed(root,"MainWeapon_ADS_Front","Soph_ADS_Front");
        var rear=ElderboomWeaponFraming.FindNamed(root,"MainWeapon_ADS_Rear","Soph_ADS_Rear");
        Vector3 axis=front!=null && rear!=null?(front.position-rear.position).normalized:root.forward;
        Vector3 up=root.up;Vector3 side=Vector3.Cross(up,axis).normalized;
        var renderers=view.model.GetComponentsInChildren<Renderer>(true).Where(r=>
        {
            string name=r.name.ToLowerInvariant();return !name.Contains("arm")&&!name.Contains("hand")&&!name.Contains("body")&&!name.Contains("head")&&!name.Contains("feet");
        }).ToArray();
        if(renderers.Length==0)renderers=view.model.GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0)throw new InvalidOperationException("No muzzle geometry: "+root.name);
        float far=float.NegativeInfinity;Renderer tip=renderers[0];
        foreach(var renderer in renderers)
        {
            var b=renderer.bounds;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var point=b.center+Vector3.Scale(b.extents,new Vector3(x,y,z));float distance=Vector3.Dot(point,axis);
                if(distance>far){far=distance;tip=renderer;}
            }
        }
        Vector3 line=front!=null?front.position-up*.025f:tip.bounds.center;
        Vector3 position=axis*far+side*Vector3.Dot(line,side)+up*Vector3.Dot(line,up);
        var parent=front!=null?front.parent:tip.transform;
        ElderboomWeaponFraming.EnsureMarker(parent,"MainWeapon_Muzzle",parent.InverseTransformPoint(position));
    }
    public static void Apply()
    {
        AssetDatabase.Refresh();
        ElderboomArsenalBuilder.BuildKnife();
        var report=new StringBuilder();
        foreach(var definition in ElderboomWeapons.All.Where(d=>!d.IsOperatorPrimary))
        {
            string path="Assets/Resources/"+definition.resource+".prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<ElderboomViewmodel>();
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(definition.asset);
                int count=ElderboomMaterialRepair.Restore(source,view.model.transform,definition.id);
                if(count==0)throw new InvalidOperationException("No original material bindings found: "+definition.id);
                EnsureMuzzle(root.transform,view);
                report.AppendLine(definition.id+" restored renderers="+count);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity",OpenSceneMode.Single);
        foreach(var view in UnityEngine.Object.FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var definition=ElderboomWeapons.All.FirstOrDefault(d=>d.IsOperatorPrimary && d.operatorId==view.operatorId);
            if(definition==null || view.model==null)continue;
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(definition.asset);
            int count=ElderboomMaterialRepair.Restore(source,view.model.transform,definition.id);
            if(count==0)throw new InvalidOperationException("Operator source binding missing: "+definition.id);
            EnsureMuzzle(view.transform,view);
            report.AppendLine(definition.id+" restored renderers="+count);
        }
        if(!UnityEngine.Object.FindObjectsByType<ElderboomStation>(FindObjectsSortMode.None).Any(s=>s.category=="slot"))
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Station_THIRD_WEAPON_SLOT";
            go.transform.position=new Vector3(7,1,-22);go.transform.localScale=new Vector3(1.5f,2,1.5f);
            var station=go.AddComponent<ElderboomStation>();station.category="slot";station.label="THIRD WEAPON SLOT + SPEED";station.cost=500;
            var obstacle=go.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=Vector3.one;obstacle.carving=true;
            string path="Assets/Materials/Station_ThirdSlot.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("HDRP/Lit"));material.SetColor("_BaseColor",new Color(.12f,.26f,.18f));AssetDatabase.CreateAsset(material,path);}
            go.GetComponent<Renderer>().sharedMaterial=material;
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs/review");File.WriteAllText("Logs/review/material_restore.txt",report.ToString());
        ElderboomPortValidation.ValidateSaved();
        Debug.Log("ELDERBOOM_REVIEW_PREPARATION_PASSED");
    }
}
