using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomVisualRepair
{
    public static void Apply()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var report=new StringBuilder();
        foreach(var d in ElderboomWeapons.All)
        {
            if(d.id=="akmn" || d.id=="hk53") { report.AppendLine(d.id+": preserved");continue; }
            bool asset=!d.IsOperatorPrimary;
            var root=asset ? PrefabUtility.LoadPrefabContents("Assets/Resources/"+d.resource+".prefab")
                : UnityEngine.Object.FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include).First(v=>v.operatorId==d.operatorId).gameObject;
            var view=root.GetComponent<ElderboomViewmodel>();
            view.ResetToIdle();
            if(view.operatorId=="TACTICAL" && view.idle!=null)ElderboomAnimationSampling.Sample(view.idle,view.model,1f/60f);
            var model=view.model.transform;
            ElderboomWeaponFraming.HideNonViewmodelBodies(root.transform);
            if(d.id=="striker")foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                if(r.name=="output_unwrapped.002" || r.name=="output_unwrapped_002")r.enabled=false;
            // glTFast mirrors local X while converting glTF to Unity. Rebuild the
            // source sight coordinates; old audit markers had also been swapped.
            if(ElderboomWeaponFraming.TryCalibration(d.name,out var calibration) && !string.IsNullOrEmpty(calibration.mesh))
            {
                var mesh=ElderboomWeaponFraming.FindNamed(root.transform,calibration.mesh);
                if(mesh==null)throw new InvalidOperationException("Sight mesh missing: "+d.id);
                foreach(var old in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="MainWeapon_ADS_Rear"||t.name=="MainWeapon_ADS_Front").ToArray())UnityEngine.Object.DestroyImmediate(old.gameObject);
                foreach(bool isFront in new[]{false,true})
                {
                    var p=isFront?calibration.front:calibration.rear;p.x=-p.x;
                    var marker=ElderboomWeaponFraming.EnsureMarker(mesh,isFront?"MainWeapon_ADS_Front":"MainWeapon_ADS_Rear",p);
                    marker.SetParent(mesh,false);marker.localPosition=p;
                }
            }
            var rear=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Rear");
            var front=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Front");
            if(rear!=null && front!=null)
            {
                var axis=root.transform.InverseTransformDirection(front.position-rear.position);axis.y=0;
                float yaw=Vector3.SignedAngle(axis,Vector3.forward,Vector3.up);
                model.localRotation=Quaternion.AngleAxis(yaw,Vector3.up)*model.localRotation;
                var target=new Vector3(d.category=="handgun"?.10f:.12f,-.12f,d.category=="handgun"?.36f:.27f);
                model.localPosition+=target-root.transform.InverseTransformPoint(rear.position);
            }
            else
            {
                // Unity FBX conversion already reverses the Godot forward convention.
                // Importing Godot's extra half-turn placed the muzzle toward the player.
                model.localRotation=d.id=="wrench"?Quaternion.Euler(8,0,-12):Quaternion.identity;
                var guns=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled && !IsArms(r.name)).ToArray();
                if(guns.Length>0)
                {
                    var b=guns[0].bounds;foreach(var r in guns.Skip(1))b.Encapsulate(r.bounds);
                    var center=root.transform.InverseTransformPoint(b.center);
                    float depth=d.category=="melee"?.48f:Mathf.Max(.48f,b.size.z*.5f+.24f);
                    model.localPosition+=new Vector3(d.category=="melee"?.17f:.11f,-.15f,depth)-center;
                }
            }
            var pose=ElderboomWeaponFraming.FromViewmodel(view,root.transform);
            pose.hipPosition=Vector3.zero;pose.hipRotation=Quaternion.identity;
            if(rear!=null && front!=null)pose.hasAds=ElderboomWeaponFraming.SolveSightPose(root.transform,rear,front,new Vector3(0,0,pose.adsRelief),out pose.adsPosition,out pose.adsRotation);
            else {pose.hasAds=false;pose.adsPosition=pose.hipPosition;pose.adsRotation=pose.hipRotation;}
            ElderboomWeaponFraming.Store(view,pose);
            root.transform.localPosition=pose.hipPosition;root.transform.localRotation=pose.hipRotation;
            UpdateMuzzle(root.transform,rear,front);
            report.AppendLine(d.id+" modelRot="+model.localEulerAngles+" modelPos="+model.localPosition);
            if(asset){PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/"+d.resource+".prefab");PrefabUtility.UnloadPrefabContents(root);}
            else EditorUtility.SetDirty(view);
        }
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        if(profile.TryGet<Exposure>(out var exposure))exposure.fixedExposure.Override(7.2f);
        EditorUtility.SetDirty(profile);
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if(light.type==LightType.Directional){light.color=new Color(.30f,.40f,.55f);light.intensity=2200;}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        ElderboomStreetLamps.Ensure();
        Directory.CreateDirectory("Logs/visual_fix");File.WriteAllText("Logs/visual_fix/repair.txt",report.ToString());
    }
    static bool IsArms(string n)
    {n=n.ToLowerInvariant();return n.Contains("arm")||n.Contains("hand")||n.Contains("body")||n.Contains("feet")||n.Contains("head");}
    static void UpdateMuzzle(Transform root,Transform rear,Transform front)
    {
        var muzzle=ElderboomWeaponFraming.FindNamed(root,"MainWeapon_Muzzle");
        if(muzzle==null)muzzle=new GameObject("MainWeapon_Muzzle").transform;
        // Reparent to the animated gun, not to the stationary viewmodel pivot.
        if(front!=null && rear!=null)
        {
            var axis=(front.position-rear.position).normalized;
            muzzle.SetParent(front.parent,true);muzzle.position=front.position+axis*.035f;
            muzzle.rotation=Quaternion.LookRotation(axis,Vector3.up);
        }
        else
        {
            var guns=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&!IsArms(r.name)).ToArray();
            if(guns.Length==0)return;
            var main=guns.OrderByDescending(r=>r.bounds.size.z).First();
            var b=main.bounds;
            muzzle.SetParent(main.transform,true);
            muzzle.position=new Vector3(b.center.x,b.center.y,b.max.z+.015f);
            muzzle.rotation=root.rotation;
        }
    }
}
