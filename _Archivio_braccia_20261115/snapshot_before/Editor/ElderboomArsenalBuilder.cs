using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ElderboomArsenalBuilder
{
    public static void Build()
    {
        if(!AssetDatabase.IsValidFolder("Assets/Resources/Elderboom/Weapons"))AssetDatabase.CreateFolder("Assets/Resources/Elderboom","Weapons");
        foreach(var definition in ElderboomWeapons.All)if(!definition.IsOperatorPrimary)BuildWeapon(definition);
    }
    public static void BuildKnife(){BuildWeapon(ElderboomWeapons.Find("knife"));}
    static void BuildWeapon(ElderboomWeaponDefinition definition)
    {
        if(definition.id=="hk53"){ElderboomHK53Builder.Build();return;}
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(definition.asset);
            if(source==null)throw new InvalidOperationException("Missing weapon model: "+definition.asset);
            var root=new GameObject("Weapon_"+definition.id);
            var model=UnityEngine.Object.Instantiate(source,root.transform);
            model.name="Viewmodel";
            var clips=AssetDatabase.LoadAllAssetsAtPath(definition.asset).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")).ToArray();
            var idle=clips.FirstOrDefault(c=>c.name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)>=0)??clips.FirstOrDefault();
            if(idle!=null)idle.SampleAnimation(model,0);
            foreach(var animator in model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            var driver=root.AddComponent<ElderboomViewmodel>();driver.model=model;driver.operatorId=definition.category;driver.idle=idle;
            driver.reload=clips.FirstOrDefault(c=>c.name.IndexOf("reload",StringComparison.OrdinalIgnoreCase)>=0);
            var anchor=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Camera" || t.name=="Eyes" || t.name=="P_head");
            if(anchor!=null)
            {
                if(anchor.name=="Camera" || anchor.name=="Eyes")model.transform.rotation=Quaternion.Inverse(anchor.rotation);
                model.transform.position-=anchor.position;
            }
            else
            {
                var renderers=model.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0)
                {
                    var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    float scale=Mathf.Clamp(.65f/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z)),.01f,1f);
                    model.transform.localScale*=scale;
                    bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    model.transform.position+=new Vector3(.14f,-.19f,.48f)-bounds.center;
                }
            }
            foreach(var camera in model.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(camera);
            foreach(var listener in model.GetComponentsInChildren<AudioListener>(true))UnityEngine.Object.DestroyImmediate(listener);
            foreach(var node in model.GetComponentsInChildren<Transform>(true))node.gameObject.layer=19;
            ConvertMaterials(model,definition.id);
            if(definition.id=="rhino" || definition.id=="m1911")
            {
                string name=definition.id=="rhino"?"Rhino 60DS":"1911_Rail";
                var gun=model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
                if(gun!=null)
                {
                    var rear=new GameObject("MainWeapon_ADS_Rear").transform;rear.SetParent(gun,false);rear.localPosition=definition.id=="rhino"?new Vector3(-.041f,.15f,-.0003f):new Vector3(-.076f,.046f,-.00015f);
                    var front=new GameObject("MainWeapon_ADS_Front").transform;front.SetParent(gun,false);front.localPosition=definition.id=="rhino"?new Vector3(.105f,.14781f,-.0003f):new Vector3(.088f,.04119f,-.000225f);
                }
            }
            ElderboomReviewPreparation.EnsureMuzzle(root.transform,driver);
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Elderboom/Weapons/"+definition.id+".prefab");UnityEngine.Object.DestroyImmediate(root);
    }

    public static void ConvertMaterials(GameObject root,string id)
    {
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterials=renderer.sharedMaterials.Select(material=>ElderboomMaterialRepair.Convert(material,id)).ToArray();
    }

    public static void Stations()
    {
        Station("PISTOLS","weapons",new Vector3(-7,2,-7),new[]{"SERVICE PISTOL","HEAVY REVOLVER"});
        Station("ASSAULT RIFLES","weapons",new Vector3(0,2,43),new[]{"AR-9","V CARBINE"});
        Station("SHOTGUNS","weapons",new Vector3(-43,2,0),new[]{"SHOTGUN","SAWNOFF"});
        Station("MELEE","weapons",new Vector3(43,2,0),new[]{"PIPE WRENCH","CROWBAR"});
        Station("MEDICAL","medical",new Vector3(6,2,-8),new string[0]);
        Station("THIRD WEAPON SLOT + SPEED","slot",new Vector3(7,1,-22),new string[0]);
    }
    static void Station(string label,string kind,Vector3 position,string[] options)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Station_"+label;go.transform.position=position;go.transform.localScale=new Vector3(.9f,1.8f,.7f);
        var station=go.AddComponent<ElderboomStation>();station.label=label;station.category=kind;station.weapons=options;station.cost=kind=="medical"?350:kind=="slot"?500:300;
        string path="Assets/Materials/Station_"+label+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("HDRP/Lit"));material.SetColor("_BaseColor",kind=="medical"?new Color(.12f,.3f,.23f):new Color(.15f,.22f,.28f));AssetDatabase.CreateAsset(material,path);}go.GetComponent<Renderer>().sharedMaterial=material;
    }
}
