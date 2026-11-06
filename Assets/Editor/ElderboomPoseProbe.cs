using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ElderboomPoseProbe
{
 public static void Run()
 {
  EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");var s=new StringBuilder();
  foreach(var d in ElderboomWeapons.All.Where(d=>new[]{"sig550","striker","rhino","m1911"}.Contains(d.id)))
  {
   bool asset=!d.IsOperatorPrimary;var root=asset?PrefabUtility.LoadPrefabContents("Assets/Resources/"+d.resource+".prefab"):Object.FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include).First(v=>v.operatorId==d.operatorId).gameObject;
   var v=root.GetComponent<ElderboomViewmodel>();var m=v.model.transform;
   s.AppendLine(d.id+" rootcurves="+string.Join(",",AnimationUtility.GetCurveBindings(v.idle).Where(c=>c.path=="").Select(c=>c.propertyName)));
   s.AppendLine("Before "+m.localPosition+" "+m.localEulerAngles);
   ElderboomAnimationSampling.Sample(v.idle,v.model,0);s.AppendLine("after0 "+m.localPosition+" "+m.localEulerAngles);
   ElderboomAnimationSampling.Sample(v.idle,v.model,1f/60f);s.AppendLine("after1 "+m.localPosition+" "+m.localEulerAngles);
   var cal=new ElderboomWeaponFraming.Calibration();ElderboomWeaponFraming.TryCalibration(d.name,out cal);
   var mesh=ElderboomWeaponFraming.FindNamed(root.transform,cal.mesh);
   var r=mesh.GetComponent<Renderer>();
   s.AppendLine("mesh type="+r.GetType()+" localPos="+mesh.localPosition+" localRot="+mesh.localEulerAngles+" localScale="+mesh.localScale+" worldscale="+mesh.lossyScale);
   s.AppendLine("RendererBounds "+r.bounds);
   s.AppendLine("rear="+root.transform.InverseTransformPoint(mesh.TransformPoint(cal.rear))+" front="+root.transform.InverseTransformPoint(mesh.TransformPoint(cal.front)));
   if(r is SkinnedMeshRenderer sk){var b=new Mesh();sk.BakeMesh(b);s.AppendLine("bakedBounds="+b.bounds);Object.DestroyImmediate(b);}else s.AppendLine("meshbounds="+mesh.GetComponent<MeshFilter>().sharedMesh.bounds);
   if(asset)PrefabUtility.UnloadPrefabContents(root);
  }
  File.WriteAllText("Logs/visual_fix/pose_probe.txt",s.ToString());
 }
}
