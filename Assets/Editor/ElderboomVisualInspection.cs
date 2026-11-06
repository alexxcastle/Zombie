using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomVisualInspection
{
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var report=new StringBuilder();
        foreach(var d in ElderboomWeapons.All)
        {
            GameObject root;
            bool created=!d.IsOperatorPrimary;
            if(created)root=Object.Instantiate(Resources.Load<GameObject>(d.resource));
            else root=Object.FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include).First(v=>v.operatorId==d.operatorId).gameObject;
            var v=root.GetComponent<ElderboomViewmodel>();v.ResetToIdle();
            report.AppendLine("WEAPON "+d.id+" root "+root.transform.localPosition+" rot "+root.transform.localEulerAngles+" model "+v.model.transform.localEulerAngles+" scale "+v.model.transform.localScale);
            var rear=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Rear","Soph_ADS_Rear"); var front=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Front","Soph_ADS_Front");
            if(rear!=null && front!=null)report.AppendLine("AXIS "+d.id+" "+root.transform.InverseTransformDirection((front.position-rear.position).normalized).ToString("F4"));
            foreach(var t in v.model.GetComponentsInChildren<Transform>(true))if(t.name.Contains("Camera")||t.name=="Eyes")report.AppendLine("CAM "+t.name+" pos="+root.transform.InverseTransformPoint(t.position)+" rot="+t.localEulerAngles);
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
            {
                report.AppendLine("  "+r.name+" enabled="+r.enabled+" center="+root.transform.InverseTransformPoint(r.bounds.center).ToString("F3")+" size="+r.bounds.size.ToString("F3"));
            }
            if(created)Object.DestroyImmediate(root);
        }
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if(light.name.Contains("Lamp")||light.type==LightType.Directional)report.AppendLine("LIGHT "+light.name+" pos="+light.transform.position+" intensity="+light.intensity+" unit="+light.lightUnit+" parent="+light.transform.parent?.name);
        foreach(var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            if(v.sharedProfile!=null && v.sharedProfile.TryGet<Exposure>(out var exposure))report.AppendLine("EXPOSURE "+v.name+" priority="+v.priority+" EV="+exposure.fixedExposure.value);
        foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))if(PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).EndsWith("StreetLamp.glb")){foreach(var r in t.GetComponentsInChildren<MeshRenderer>())report.AppendLine("LAMP_MESH "+r.name+" root="+t.position+" scale="+t.localScale+" bounds="+r.bounds+" local="+t.InverseTransformPoint(r.bounds.center));}
        Directory.CreateDirectory("Logs/visual_fix");File.WriteAllText("Logs/visual_fix/inspection.txt",report.ToString());
    }
}
