// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Explicit opt-in test of the actual compiled Player, including a real weapon shot.
public sealed class ElderboomHitFeedbackValidation : MonoBehaviour
{
    string dir,exports;int checks,errors;bool finished;float deadline;
    readonly StringBuilder report=new StringBuilder();
    HDRPSlicePlayer player;ElderboomGame game;Camera camera;Vector3 playerStart;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Launch()
    {
        if(!Environment.GetCommandLineArgs().Contains("-elderboom-hit-validation"))return;
        Application.runInBackground=true;Debug.Log("HIT_VALIDATION_LAUNCH");
        var go=new GameObject("Hit feedback validation");DontDestroyOnLoad(go);go.AddComponent<ElderboomHitFeedbackValidation>();
    }
    static string Argument(string key,string fallback)
    {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:fallback;}
    IEnumerator Start()
    {
        Debug.Log("HIT_VALIDATION_START");
        dir=Argument("-elderboom-validation-output",Path.Combine(Application.persistentDataPath,"hit_feedback"));
        exports=Argument("-elderboom-map-output",Path.Combine(dir,"maps"));Directory.CreateDirectory(dir);
        deadline=Time.realtimeSinceStartup+300;Application.logMessageReceived+=OnLog;
        report.AppendLine("compiled="+!Application.isEditor+" graphics="+SystemInfo.graphicsDeviceType);
        var stack=new Stack<IEnumerator>();stack.Push(Verify());
        while(stack.Count>0&&!finished)
        {
            object next=null;
            try{var current=stack.Peek();if(!current.MoveNext()){stack.Pop();continue;}next=current.Current;}
            catch(Exception ex){Check(false,ex.ToString());break;}
            if(next is IEnumerator nested){stack.Push(nested);continue;}
            yield return next;
        }
        Finish();
    }
    void Update(){if(!finished && deadline>0 && Time.realtimeSinceStartup>deadline){Check(false,"validation timeout");Finish();}}
    void Check(bool value,string message){checks++;report.AppendLine((value?"PASS ":"FAIL ")+message);if(!value)errors++;}
    void OnLog(string message,string trace,LogType type)
    {if(type==LogType.Exception||type==LogType.Error){errors++;report.AppendLine("RUNTIME_ERROR "+message);}}
    void Finish()
    {
        if(finished)return;finished=true;Application.logMessageReceived-=OnLog;
        report.AppendLine("checks="+checks+" errors="+errors);File.WriteAllText(Path.Combine(dir,"verification.txt"),report.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors==0?0:1);
#else
        Application.Quit(errors==0?0:1);
#endif
    }
    IEnumerator Verify()
    {
        Debug.Log("HIT_VALIDATION_LOADING_SCENE");
        if(SceneManager.GetActiveScene().name!="Elderboom_Unity_Port")yield return SceneManager.LoadSceneAsync("Elderboom_Unity_Port");
        yield return null;
        Debug.Log("HIT_VALIDATION_SCENE_READY");
        game=FindAnyObjectByType<ElderboomGame>();player=FindAnyObjectByType<HDRPSlicePlayer>();
        game.BeginSession("SOPH","NORMAL");yield return null;game.StopAllCoroutines();player.enabled=false;
        playerStart=player.transform.position;camera=player.ViewCamera;
        // Render requests can take longer than an animation frame on this machine.
        // Keep functional timing deterministic, and still use normal Player animation updates.
        Time.maximumDeltaTime=.05f;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
        player.GetComponent<ElderboomArsenal>().Equip("hk53");
        foreach(var zombie in FindObjectsByType<ElderboomZombie>())zombie.gameObject.SetActive(false);
        foreach(var view in FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include))view.gameObject.SetActive(false);
        camera.GetComponent<HDAdditionalCameraData>().antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
        var still=new GameObject("Hit test stills").AddComponent<Volume>();still.isGlobal=true;still.priority=100;
        still.sharedProfile=ScriptableObject.CreateInstance<VolumeProfile>();
        still.sharedProfile.Add<MotionBlur>(true).intensity.Override(0);
        still.sharedProfile.Add<DepthOfField>(true).focusMode.Override(DepthOfFieldMode.Off);
        yield return TestZombie("Zombie","male");
        yield return TestZombie("ZombieFemale","female");
        yield return TestHitMarkers();
        if(!Environment.GetCommandLineArgs().Contains("-elderboom-skip-map-export"))yield return ElderboomMapCapture.Capture(exports);
        Check(File.Exists(Path.Combine(exports,"Elderboom_mappa_completa_ortografica_4096.png")),"full terrain orthographic export saved");
        Check(File.Exists(Path.Combine(exports,"Elderboom_villaggio_ortografica_4096.png")),"full village orthographic export saved");
    }
    IEnumerator TestHitMarkers()
    {
        player.transform.position=playerStart;camera.transform.localRotation=Quaternion.identity;
        player.HitMarker.Reset();player.ConfirmHit();
        Check(player.HitMarkerVisible,"confirmed hit displays the red marker");
        yield return new WaitForSeconds(.035f);
        player.HitMarker.Segment(0,Time.time,out var a,out var b,out var tint);
        float startY=(a.y+b.y)*.5f;
        Check(tint.r>.99f && tint.g<.04f && tint.b<.03f,"hitmarker shards use Godot red");
        yield return CaptureHud("hitmarker_start.png");
        yield return new WaitForSeconds(.3f);
        player.HitMarker.Segment(0,Time.time,out a,out b,out tint);
        Check((a.y+b.y)*.5f>startY+15 && tint.a<.7f,"hitmarker shards fall down and fade");
        yield return CaptureHud("hitmarker_falling.png");
        yield return new WaitForSeconds(.3f);
        Check(!player.HitMarkerVisible,"hitmarker expires after .55 seconds");
        player.ConfirmHit();yield return new WaitForSeconds(.025f);
        float age=player.HitMarker.Age(Time.time);player.ConfirmHit();
        Check(player.HitMarker.HasPending && player.HitMarker.Age(Time.time)>=age,"rapid hits queue rather than restarting immediately");
        yield return new WaitForSeconds(.105f);
        Check(player.HitMarkerVisible && !player.HitMarker.HasPending && player.HitMarker.Age(Time.time)<ElderboomHitMarker.RepeatInterval,"queued hitmarker starts after .12 seconds");
        player.CancelGameplayActions();Check(!player.HitMarkerVisible && !player.HitMarker.HasPending,"session/death cleanup clears hitmarkers");
    }
    IEnumerator CaptureHud(string name)
    {
        Time.timeScale=0;string path=Path.Combine(dir,name);ScreenCapture.CaptureScreenshot(path);
        for(int i=0;i<8;i++)yield return null;
        Time.timeScale=1;Check(File.Exists(path),"HUD screenshot saved: "+name);
        if(File.Exists(path))
        {
            var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(path));
            var pixels=image.GetPixels32();int count=0;float sumY=0,maxRadius=0;
            float cx=image.width*.5f,cy=image.height*.5f;
            float scale=Mathf.Max(.65f,image.width/1920f)*Mathf.Clamp(PlayerPrefs.GetFloat("hdrp.settings.hud_scale",1),.75f,1.25f);
            for(int y=(int)(cy-200*scale);y<cy+200*scale;y++)for(int x=(int)(cx-200*scale);x<cx+200*scale;x++)
            {
                if(x<0||x>=image.width||y<0||y>=image.height)continue;
                var c=pixels[y*image.width+x];
                if(c.r>110 && c.r>c.g*3 && c.r>c.b*3)
                {count++;sumY+=image.height-1-y;maxRadius=Mathf.Max(maxRadius,new Vector2(x-cx,y-cy).magnitude);}
            }
            Check(count>60 && maxRadius<110*scale,"HUD pixels keep all red shards near reticle: "+name+", pixels="+count+", radius="+maxRadius.ToString("F1"));
            if(name.Contains("falling"))Check(count>0 && sumY/count>cy+12*scale,"actual HUD shards fall below screen centre");
            Destroy(image);
        }
    }
    IEnumerator TestZombie(string resource,string label)
    {
        Debug.Log("HIT_VALIDATION_ZOMBIE_"+label);
        player.transform.position=playerStart;
        camera.transform.localRotation=Quaternion.identity;
        var point=playerStart+Vector3.forward*4;
        if(NavMesh.SamplePosition(point,out var sample,5,NavMesh.AllAreas))point=sample.position;
        var root=Instantiate(Resources.Load<GameObject>("Elderboom/"+resource),point,Quaternion.Euler(0,180,0));
        var zombie=root.GetComponent<ElderboomZombie>();zombie.CountsForWave=false;zombie.Initialize(game,player,100,5,1.2f,2);
        var agent=root.GetComponent<NavMeshAgent>();var animator=root.GetComponentInChildren<Animator>();
        Check(agent.isOnNavMesh,label+" on navigable ground");Check(animator.HasState(0,Animator.StringToHash("ShotHit")),label+" authored ShotHit state available");
        var clip=animator.runtimeAnimatorController.animationClips.First(c=>c.name=="ShotHit");report.AppendLine(label+" hit clip "+clip.name+" seconds="+clip.length);
        camera.transform.LookAt(root.transform.position+Vector3.up*1.1f);
        yield return new WaitForSeconds(.15f);
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,label+"_before.png")));
        var bones=animator.GetComponentsInChildren<Transform>();var pose=bones.Select(t=>t.rotation).ToArray();
        Physics.SyncTransforms();
        typeof(HDRPSlicePlayer).GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null);
        Check(zombie.Health>0 && zombie.Health<100,label+" real HK53 shot is non-lethal and damages target");
        yield return new WaitForSeconds(.22f);yield return null;
        report.AppendLine(label+" current="+animator.GetCurrentAnimatorStateInfo(0).shortNameHash+" next="+animator.GetNextAnimatorStateInfo(0).shortNameHash+" phase="+animator.GetCurrentAnimatorStateInfo(0).normalizedTime+" speed="+animator.speed+" culling="+animator.cullingMode);
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("ShotHit"),label+" real shot starts ShotHit animation");
        Check(zombie.IsReactingToHit && agent.isStopped,label+" reaction holds navigation rather than sliding");
        float motion=0;for(int i=0;i<bones.Length;i++)motion=Mathf.Max(motion,Quaternion.Angle(pose[i],bones[i].rotation));
        Check(motion>1,label+" reaction visibly moves skeleton, degrees="+motion.ToString("F2"));
        var impacts=FindObjectsByType<ParticleSystem>().Where(p=>p.name=="BloodImpact").ToArray();
        Check(impacts.Any(p=>p.particleCount>=48),label+" non-lethal shot emits larger blood burst");
        report.AppendLine(ElderboomAbilityShots.Capture(camera,Path.Combine(dir,label+"_reaction_blood.png")));
        float progress=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        zombie.TakeDamage(1,camera.transform.position,root.transform.position+Vector3.up*1.1f,Vector3.back);
        yield return new WaitForSeconds(.12f);
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("ShotHit") && animator.GetCurrentAnimatorStateInfo(0).normalizedTime>progress,label+" repeated hits do not restart reaction at frame zero");
        zombie.IgnorePlayerFor(10);
        yield return new WaitForSeconds(clip.length+.2f);
        var locomotion=animator.GetCurrentAnimatorStateInfo(0);
        Check(!zombie.IsReactingToHit && (locomotion.IsName("WalkA")||locomotion.IsName("WalkB")),label+" returns to locomotion after the full reaction");
        zombie.FreezeFor(.3f);zombie.TakeDamage(1,camera.transform.position,root.transform.position+Vector3.up*1.1f,Vector3.back);
        yield return new WaitForSeconds(.1f);
        Check(!zombie.IsReactingToHit && animator.speed==0,label+" hit does not override a frozen pose");
        yield return new WaitForSeconds(.25f);
        zombie.BeginLift(1,.35f,.25f,.15f,1,.25f,Vector3.forward);
        yield return new WaitForSeconds(.1f);
        zombie.TakeDamage(1,camera.transform.position,root.transform.position+Vector3.up*1.1f,Vector3.back);
        yield return new WaitForSeconds(.12f);
        Check(!zombie.IsReactingToHit && animator.GetCurrentAnimatorStateInfo(0).IsName("LiftFloat"),label+" hit does not override lift animation");
        zombie.CancelAbilityEffects();
        // Interrupt a pending melee windup; its delayed damage must be invalidated.
        player.transform.position=root.transform.position+Vector3.back*.9f;
        float health=player.Health;
        var strike=(IEnumerator)typeof(ElderboomZombie).GetMethod("Strike",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(zombie,null);
        zombie.StartCoroutine(strike);
        zombie.TakeDamage(1,camera.transform.position,root.transform.position+Vector3.up*1.1f,Vector3.back);
        yield return new WaitForSeconds(.5f);
        Check(player.Health==health && animator.GetCurrentAnimatorStateInfo(0).IsName("ShotHit"),label+" hit cancels melee windup and delayed contact damage");
        zombie.ApplyShockwave(player.transform.position,2,.3f,1);
        yield return new WaitForSeconds(.5f);
        Check(!zombie.IsReactingToHit && animator.GetCurrentAnimatorStateInfo(0).IsName("ShockwaveHit"),label+" hit feedback preserves shockwave priority");
        zombie.TakeDamage(999,camera.transform.position,root.transform.position+Vector3.up*1.1f,Vector3.back);
        yield return new WaitForSeconds(.6f);
        Check(!zombie.IsAlive && !zombie.IsReactingToHit && animator.GetCurrentAnimatorStateInfo(0).IsName("DeathA"),label+" lethal damage transitions to death instead of hit reaction");
        Destroy(root);yield return null;
    }
}
#endif
