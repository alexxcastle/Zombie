// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in validation exercises the same compiled Player and assets shipped to the user.
public sealed class ElderboomVisualValidation : MonoBehaviour
{
    const string Flag="-elderboom-visual-validation";
    string dir;int errors,checks;readonly StringBuilder report=new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if(!Environment.GetCommandLineArgs().Contains(Flag))return;
        var host=new GameObject("VisualValidation");DontDestroyOnLoad(host);
        host.AddComponent<ElderboomVisualValidation>();
    }
    public static void StartEditor() {new GameObject("VisualValidation").AddComponent<ElderboomVisualValidation>();}
    IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-elderboom-validation-output");
        dir=index>=0 && index+1<args.Length ? args[index+1] : Path.Combine(Application.dataPath,"../Logs/visual_fix/player");
        Directory.CreateDirectory(dir);Application.logMessageReceived+=OnLog;
        report.AppendLine("compiled="+!Application.isEditor+" graphics="+SystemInfo.graphicsDeviceType);
        var run=Verify();
        while(true)
        {
            object current=null;bool more=false;
            try {more=run.MoveNext();if(more)current=run.Current;}
            catch(Exception ex){Check(false,ex.ToString());break;}
            if(!more)break;yield return current;
        }
        report.AppendLine("checks="+checks+" errors="+errors);File.WriteAllText(Path.Combine(dir,"verification.txt"),report.ToString());
        Application.logMessageReceived-=OnLog;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors==0?0:1);
#else
        Application.Quit(errors==0?0:1);
#endif
    }
    void Check(bool value,string text){checks++;report.AppendLine((value?"PASS ":"FAIL ")+text);if(!value)errors++;}
    void OnLog(string message,string trace,LogType type)
    {
        if(message.Contains("Non-Legacy animations cannot") || type==LogType.Exception || type==LogType.Error)
        {errors++;report.AppendLine("RUNTIME_ERROR "+message);}
    }
    IEnumerator Verify()
    {
        if(SceneManager.GetActiveScene().name!="Elderboom_Unity_Port")
        {yield return SceneManager.LoadSceneAsync("Elderboom_Unity_Port");yield return null;}
        var game=FindAnyObjectByType<ElderboomGame>();var player=FindAnyObjectByType<HDRPSlicePlayer>();
        yield return null;game.BeginSession("SOPH","NORMAL");yield return null;
        game.StopAllCoroutines();player.enabled=false;
        var camera=player.ViewCamera;camera.fieldOfView=72;
        camera.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>().antialiasing=UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
        var still=new GameObject("Validation still capture").AddComponent<UnityEngine.Rendering.Volume>();still.isGlobal=true;still.priority=100;
        still.sharedProfile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        still.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.MotionBlur>(true).intensity.Override(0);
        still.sharedProfile.Add<UnityEngine.Rendering.HighDefinition.DepthOfField>(true).focusMode.Override(UnityEngine.Rendering.HighDefinition.DepthOfFieldMode.Off);
        foreach(var z in FindObjectsByType<ElderboomZombie>())z.gameObject.SetActive(false);
        foreach(var v in FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include))v.gameObject.SetActive(false);
        var operators=player.GetComponentsInChildren<ElderboomViewmodel>(true).Where(v=>v.operatorId=="SOPH"||v.operatorId=="ELI"||v.operatorId=="TACTICAL").ToArray();
        foreach(var d in ElderboomWeapons.All)
        {
            GameObject root=d.IsOperatorPrimary?operators.First(v=>v.operatorId==d.operatorId).gameObject:Instantiate(Resources.Load<GameObject>(d.resource),camera.transform,false);
            var v=root.GetComponent<ElderboomViewmodel>();v.enabled=false;root.SetActive(true);
            root.transform.localPosition=v.hipPosition;root.transform.localRotation=v.hipRotation;
            v.ResetToIdle();if(v.operatorId=="TACTICAL" && v.idle!=null)ElderboomAnimationSampling.Sample(v.idle,v.model,1f/60f);
            var rear=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Rear","Soph_ADS_Rear");
            var front=ElderboomWeaponFraming.FindNamed(root.transform,"MainWeapon_ADS_Front","Soph_ADS_Front");
            if(rear!=null && front!=null)
            {var axis=camera.transform.InverseTransformDirection((front.position-rear.position).normalized);Check(axis.z>.95f,d.id+" barrel forward "+axis.ToString("F3"));}
            // Physical front/rear parts independently verify FBX direction, without generated markers.
            string tip=d.id=="minigun"?"barrels":d.id=="service"?"Slide":d.id=="revolver"?"pistol":d.id=="ar9"||d.id=="carbine"||d.id=="shotgun"||d.id=="flame"?"base":"front";
            var trigger=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="trigger");
            var barrel=root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(t=>t.name==tip);
            if(trigger!=null && barrel!=null && d.id!="rhino" && d.id!="m1911")Check(camera.transform.InverseTransformPoint(barrel.bounds.center).z>camera.transform.InverseTransformPoint(trigger.position).z,d.id+" muzzle side beyond trigger");
            yield return null;yield return null;yield return null;
            if(new[]{"sig550","striker","rhino","m1911"}.Contains(d.id))
            {
                Check(root.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="MainWeapon_ADS_Rear")==1,d.id+" has one authoritative sight pair");
                var arms=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled && (r.name.Contains("Arm")||r.name=="output_unwrapped"||r.name=="output_unwrapped.001") && r.name!=(d.id=="sig550"?"output_unwrapped.001":"")).ToArray();
                if(ElderboomWeaponFraming.TryCalibration(d.name,out var cal))
                {
                    var gun=ElderboomWeaponFraming.FindNamed(root.transform,cal.mesh).GetComponent<Renderer>();
                    if(arms.Length>0)Check(camera.transform.InverseTransformPoint(gun.bounds.center).z>arms.Average(r=>camera.transform.InverseTransformPoint(r.bounds.center).z)+.02f,d.id+" gun is ahead of forearms");
                }
            }
            if(d.asset.EndsWith(".fbx") && v.idle!=null && v.idle.name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)<0)
            {
                var nodes=v.model.GetComponentsInChildren<Transform>(true);var poses=nodes.Select(t=>t.position).ToArray();
                typeof(ElderboomViewmodel).GetField("elapsed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(v,10f);
                v.enabled=true;yield return null;yield return null;v.enabled=false;
                float drift=0;for(int i=0;i<nodes.Length;i++)drift=Mathf.Max(drift,Vector3.Distance(poses[i],nodes[i].position));
                Check(drift<.0001f,d.id+" holds idle pose after 10s, drift="+drift.ToString("F5"));
            }
            report.AppendLine(d.id+" idle="+(v.idle!=null?v.idle.name:"none")+" reload="+(v.reload!=null?v.reload.name:"none"));
            report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,d.id+"_hip.png"),960,540));
            if(v.reload!=null)
            {
                var nodes=v.model.GetComponentsInChildren<Transform>(true);
                var start=nodes.Select(t=>t.localToWorldMatrix).ToArray();
                ElderboomAnimationSampling.Sample(v.reload,v.model,v.reload.length*.45f);
                float motion=0;for(int i=0;i<nodes.Length;i++)motion=Mathf.Max(motion,Vector3.Distance(start[i].GetColumn(3),nodes[i].position));
                Check(motion>.001f,d.id+" compiled reload moves geometry, max="+motion.ToString("F4"));
                if(d.id=="carbine" || d.id=="hk53" || d.id=="rhino")
                {yield return null;yield return null;report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,d.id+"_reload.png"),960,540));}
            }
            if(v.hasAds && new[]{"sig550","striker","rhino","m1911","hk53"}.Contains(d.id))
            {
                v.ResetToIdle();if(v.operatorId=="TACTICAL")ElderboomAnimationSampling.Sample(v.idle,v.model,1f/60f);
                root.transform.localPosition=v.adsPosition;root.transform.localRotation=v.adsRotation;
                yield return null;yield return null;
                if(d.id=="hk53")
                {
                    var rearMesh=ElderboomWeaponFraming.FindNamed(root.transform,"Rear_Sight");
                    var rearCollider=rearMesh.gameObject.AddComponent<MeshCollider>();rearCollider.sharedMesh=rearMesh.GetComponent<MeshFilter>().sharedMesh;
                    var frontMesh=ElderboomWeaponFraming.FindNamed(root.transform,"Front_Sight_Block");
                    var frontCollider=frontMesh.gameObject.AddComponent<MeshCollider>();frontCollider.sharedMesh=frontMesh.GetComponent<MeshFilter>().sharedMesh;
                    Physics.SyncTransforms();
                    var sightRay=new Ray(camera.transform.position,(front.position-camera.transform.position).normalized);
                    float distance=Vector3.Distance(camera.transform.position,front.position);
                    Check(!rearCollider.Raycast(sightRay,out _,distance),"HK53 compiled ADS passes through rear aperture");
                    var postRay=new Ray(camera.transform.position,(front.position-camera.transform.up*.0005f-camera.transform.position).normalized);
                    Check(frontCollider.Raycast(postRay,out _,distance+.01f),"HK53 front post is visible on the sight ray");
                    Destroy(rearCollider);Destroy(frontCollider);
                }
                report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,d.id+"_ads.png"),960,540));
            }
            root.SetActive(false);if(!d.IsOperatorPrimary)Destroy(root);
        }
        // World lighting and street fixtures are captured with the normal scene profile.
        foreach(var vol in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include))if(vol.profile!=null && vol.profile.TryGet<UnityEngine.Rendering.HighDefinition.Exposure>(out var ev))report.AppendLine("EXPOSURE "+vol.name+" "+ev.fixedExposure.value+" brightness="+PlayerPrefs.GetFloat("hdrp.settings.brightness",1));
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,"world.png"),1280,720));
        camera.transform.position=new Vector3(0,85,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.farClipPlane=180;
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,"lamp_layout.png"),1280,720));
        camera.transform.position=new Vector3(0,2.7f,-8);camera.transform.rotation=Quaternion.identity;
        
        // Use a real imported zombie silhouette for blood readability instead of a plain backdrop.
        var source=Resources.Load<GameObject>("Elderboom/Zombie");
        GameObject zombie=source!=null?Instantiate(source,new Vector3(0,1,-4),Quaternion.Euler(0,180,0)):null;
        if(zombie!=null){foreach(var a in zombie.GetComponentsInChildren<Behaviour>())if(!(a is Animator))a.enabled=false;}
        yield return null;yield return null;
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,"blood_before.png"),1280,720));
        var blood=ElderboomImportedVfx.Blood(new Vector3(0,2.4f,-5.1f),Vector3.back,false);
        foreach(var layer in blood.GetComponentsInChildren<ParticleSystem>()){layer.useAutoRandomSeed=false;layer.randomSeed=1357;}
        var ps=blood.GetComponent<ParticleSystem>();ps.Simulate(.12f,true,true,false);ps.Pause();
        yield return null;yield return null;yield return null;
        Check(ps.particleCount>=18,"blood burst emitted at impact");
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,"blood.png"),1280,720));
        var before=new Texture2D(2,2);before.LoadImage(File.ReadAllBytes(Path.Combine(dir,"blood_before.png")));
        var after=new Texture2D(2,2);after.LoadImage(File.ReadAllBytes(Path.Combine(dir,"blood.png")));
        Func<Color32[],int> red=pixels=>pixels.Count(c=>c.r>40 && c.r>c.g*1.7f && c.r>c.b*1.4f);
        int added=red(after.GetPixels32())-red(before.GetPixels32());
        Check(added>20,"blood visible at 3m: additional red pixels="+added);
        Destroy(before);Destroy(after);
    }
}
#endif
