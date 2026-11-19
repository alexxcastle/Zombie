using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Runs the actual scene in Play Mode. Never saves the temporary gate/player states.
[InitializeOnLoad]
static class ElderboomMapValidation
{
    const string Pending="elderboom.map.validation";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly StringBuilder report=new StringBuilder();
    static ElderboomGate[] gates;
    static Vector3[] gatePositions;
    static ElderboomGame game;
    static double next,started;
    static int stage,index,failures;
    static bool finishing;
    static ElderboomMapValidation(){EditorApplication.update+=Tick;}

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        SessionState.SetBool(Pending,true);
        EditorApplication.isPlaying=true;
    }
    static void Check(bool ok,string description)
    {
        report.AppendLine((ok?"PASS ":"FAIL ")+description);
        if(!ok)failures++;
    }
    static bool Path(Vector3 a,Vector3 b,out float length)
    {
        length=float.PositiveInfinity;
        if(!NavMesh.SamplePosition(a,out var x,2,NavMesh.AllAreas)||!NavMesh.SamplePosition(b,out var y,2,NavMesh.AllAreas))return false;
        var p=new NavMeshPath();
        if(!NavMesh.CalculatePath(x.position,y.position,NavMesh.AllAreas,p)||p.status!=NavMeshPathStatus.PathComplete)return false;
        length=0;for(int i=1;i<p.corners.Length;i++)length+=Vector3.Distance(p.corners[i-1],p.corners[i]);
        return true;
    }
    static void ResetGates()
    {
        for(int i=0;i<gates.Length;i++)
        {
            var g=gates[i];g.StopAllCoroutines();g.gameObject.SetActive(true);g.transform.position=gatePositions[i];
            typeof(ElderboomGate).GetField("opened",Private).SetValue(g,false);
            g.GetComponent<Collider>().enabled=true;g.GetComponent<NavMeshObstacle>().enabled=true;
            if(g.reveal!=null)g.reveal.SetActive(false);
        }
        foreach(var a in ElderboomArea.All.ToArray()){a.enabled=false;a.enabled=true;}
        Physics.SyncTransforms();
    }
    static void AllSpawnPaths(bool opened)
    {
        foreach(var group in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).GroupBy(s=>s.area))
        {
            int reached=group.Count(s=>Path(s.transform.position,game.Player.transform.position,out _));
            bool expect=opened || group.Key=="Village Square";
            Check(reached==(expect?group.Count():0),$"{(opened?"all open":"all closed")} {group.Key}: {reached}/{group.Count()} reach player");
        }
    }
    static void SpawnCoverage()
    {
        var select=typeof(ElderboomGame).GetMethod("SelectSpawn",Private);
        var fallback=typeof(ElderboomGame).GetMethod("FindHiddenFallbackSpawn",Private);
        var hidden=typeof(ElderboomGame).GetMethod("HiddenFromTeam",Private);
        var body=game.Player.GetComponent<CharacterController>();
        var original=game.Player.transform.position;var camera=Camera.main;var rotation=camera.transform.rotation;
        bool enabled=body.enabled;body.enabled=false;
        int trials=0,found=0,invalid=0;
        foreach(var area in ElderboomArea.All)
        {
            if(!area.Unlocked)continue;
            var marker=Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).FirstOrDefault(p=>p.area==area.areaName);
            if(marker==null)continue;
            game.Player.transform.position=marker.transform.position+Vector3.up*.1f;
            for(int yaw=0;yaw<360;yaw+=45)
            {
                camera.transform.rotation=Quaternion.Euler(0,yaw,0);Physics.SyncTransforms();trials++;
                var point=(Transform)select.Invoke(game,new object[]{"flank"});
                for(int attempt=0;point==null && attempt<4;attempt++)point=(Transform)fallback.Invoke(game,null);
                if(point==null)continue;
                found++;
                if(!(bool)hidden.Invoke(game,new object[]{point.position})||!Path(point.position,game.Player.transform.position,out _))invalid++;
            }
        }
        game.Player.transform.position=original;camera.transform.rotation=rotation;body.enabled=enabled;Physics.SyncTransforms();
        Check(invalid==0,"spawn candidates: all hidden and reachable, invalid="+invalid);
        Check(found==trials,$"spawn coverage: {found}/{trials} area/view directions");
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false))return;
        if(finishing)
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Pending,false);if(Application.isBatchMode)EditorApplication.Exit(failures==0?0:1);}
            return;
        }
        if(!EditorApplication.isPlaying)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0){started=now;next=now+4;}
            if(now-started>240)throw new TimeoutException("Map validation exceeded 240 seconds");
            if(now<next)return;
            if(stage==0)
            {
                game=ElderboomGame.Active;if(game==null || game.Player==null)throw new Exception("No game/player");
                HDRPSlicePlayer.DevInvulnerable=true;game.StopAllCoroutines();game.Player.enabled=false;
                foreach(var z in game.Zombies.ToArray())if(z!=null)Object.Destroy(z.gameObject);
                gates=Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None).OrderBy(g=>g.cost).ToArray();
                gatePositions=gates.Select(g=>g.transform.position).ToArray();
                Check(gates.Length==7,"seven active passages");ResetGates();stage=1;next=now+2;
                Check(ElderboomArea.All.Count==6,"six gameplay areas");
                foreach(var area in ElderboomArea.All)Check(Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Count(p=>p.area==area.areaName)>=5,"at least five spawn points in "+area.areaName);
            }
            else if(stage==1)
            {
                AllSpawnPaths(false);SpawnCoverage();SightRules();WaterBankTraversal();stage=2;index=0;
            }
            else if(stage==2)
            {
                var g=gates[index];var pos=gatePositions[index];
                bool cross=Path(pos-g.transform.forward*4,pos+g.transform.forward*4,out float length);
                Check(!cross || length>25,$"closed {g.name}: direct passage blocked (path {length:F1}m)");
                g.NetOpen();stage=3;next=now+2;
            }
            else if(stage==3)
            {
                var g=gates[index];var pos=gatePositions[index];
                Check(Path(pos-g.transform.forward*4,pos+g.transform.forward*4,out float length)&&length<25,$"open {g.name}: crosses passage ({length:F1}m)");
                Check(ElderboomArea.Find(g.areaName).Unlocked,"unlocks "+g.areaName);
                if(!string.IsNullOrEmpty(g.otherAreaName))Check(ElderboomArea.Find(g.otherAreaName).Unlocked,"shortcut unlocks reverse destination "+g.otherAreaName);
                if(g.reveal!=null)Check(g.reveal.activeSelf,"bridge deck appears after purchase");
                int blocked=0,sampled=0;
                for(int n=-6;n<=6;n++)
                {
                    var at=pos+g.transform.forward*(n*.65f);
                    if(!NavMesh.SamplePosition(at,out var hit,2,NavMesh.AllAreas)){blocked++;continue;}
                    sampled++;
                    var overlaps=Physics.OverlapCapsule(hit.position+Vector3.up*.65f,hit.position+Vector3.up*1.3f,.55f,~0,QueryTriggerInteraction.Ignore);
                    if(overlaps.Length>0)
                    {
                        blocked++;
                        report.AppendLine($"  clearance at {hit.position:F2}: "+string.Join(", ",overlaps.Select(c=>c.name+" / "+c.transform.parent?.name)));
                    }
                }
                Check(blocked==0,$"horse-size clearance {g.name}: {sampled-blocked}/{sampled} samples");
                index++;
                if(index<gates.Length){ResetGates();stage=2;next=now+2;}
                else{ResetGates();foreach(var gate in gates)gate.NetOpen();stage=4;next=now+3;}
            }
            else if(stage==4)
            {
                AllSpawnPaths(true);SpawnCoverage();
                Capture("empty");
                var spawn=typeof(ElderboomGame).GetMethod("SpawnZombie",Private);
                foreach(var point in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsSortMode.None).Take(40))spawn.Invoke(game,new object[]{point.transform,10});
                stage=5;next=EditorApplication.timeSinceStartup+20;
            }
            else if(stage==5)
            {
                int living=game.Zombies.Count(z=>z!=null&&z.IsAlive);
                Check(living>=30,"horde smoke run: "+living+" living zombies after 20 seconds");
                Capture("horde");
                Finish();
            }
        }
        catch(Exception e){Check(false,e.ToString());Finish();}
    }
    static void SightRules()
    {
        var method=typeof(ElderboomGame).GetMethod("HiddenFromObserver",Private);
        var feet=new Vector3(5000,0,5000);var eye=feet+Vector3.up*1.6f;
        bool Hidden(Vector3 at)=>(bool)method.Invoke(game,new object[]{at,feet,eye,Vector3.forward});
        Check(!Hidden(feet+Vector3.forward*50),"visible spawn beyond 36m rejected");
        Check(!Hidden(feet-Vector3.forward*10),"nearby spawn behind player rejected");
        Check(Hidden(feet-Vector3.forward*50),"distant spawn behind player allowed");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            wall.transform.position=feet+Vector3.forward*25+Vector3.up*1.5f;wall.transform.localScale=new Vector3(5,3,1);Physics.SyncTransforms();
            Check(Hidden(feet+Vector3.forward*50),"opaque cover hides whole body");
            wall.layer=2;Physics.SyncTransforms();Check(!Hidden(feet+Vector3.forward*50),"invisible boundary does not hide spawn");
            wall.layer=0;wall.transform.localScale=new Vector3(5,.7f,1);wall.transform.position=feet+Vector3.forward*25+Vector3.up*.35f;Physics.SyncTransforms();
            Check(!Hidden(feet+Vector3.forward*50),"visible head above low cover rejects spawn");
        }
        finally{Object.DestroyImmediate(wall);}
    }
    static void WaterBankTraversal()
    {
        // Real CharacterController.Move against the saved terrain. NavMesh checks
        // alone cannot detect a player or mounted horse walking through a river.
        var terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(t=>t.terrainData.size.x).First();
        var sites=new[]{new Vector2(-24.3f,-47.0f),new Vector2(-9.3f,-47.2f),new Vector2(23.7f,-49.2f),new Vector2(43.7f,-48.5f)};
        var proxy=new GameObject("Creek_physical_probe");var body=proxy.AddComponent<CharacterController>();body.height=1.8f;body.center=Vector3.up*.9f;body.skinWidth=.04f;
        try
        {
            foreach(bool horse in new[]{false,true})foreach(var site in sites)
            {
                bool crossed=false,trapped=false;float furthest=-100;
                foreach(float jumpAt in horse?new[]{-100f}:new[]{-100f,5f,3.5f,1.5f})
                {
                    body.enabled=false;body.radius=horse?.55f:.35f;body.stepOffset=horse?.45f:.3f;body.slopeLimit=horse?50:45;
                    var start=new Vector3(site.x,0,site.y+7);start.y=terrain.SampleHeight(start)+terrain.transform.position.y+.08f;proxy.transform.position=start;body.enabled=true;Physics.SyncTransforms();
                    float vertical=0;bool jumped=false;
                    for(int k=0;k<480;k++)
                    {
                        if(body.isGrounded&&vertical<0)vertical=-1.5f;
                        if(!horse&&!jumped&&jumpAt>0&&proxy.transform.position.z-site.y<jumpAt&&body.isGrounded){vertical=Mathf.Sqrt(2*9.81f);jumped=true;}
                        vertical-=9.81f/60;body.Move(new Vector3(0,vertical/60,-(horse?9:6)/60f));
                        furthest=Mathf.Max(furthest,site.y-proxy.transform.position.z);
                        if(proxy.transform.position.z<site.y-4.9f){crossed=true;break;}
                    }
                    for(int k=0;k<480;k++)
                    {if(body.isGrounded&&vertical<0)vertical=-1.5f;vertical-=9.81f/60;body.Move(new Vector3(0,vertical/60,(horse?3.5f:4)/60f));if(proxy.transform.position.z>site.y+5)break;}
                    if(proxy.transform.position.z<site.y+4)trapped=true;
                }
                Check(!crossed,$"creek cut bank stops {(horse?"horse":"player + three jump timings")} at x={site.x:F1}, furthest south={furthest:F2}m");
                Check(!trapped,$"creek return to north bank remains possible at x={site.x:F1} ({(horse?"horse":"player")})");
            }
        }
        finally{Object.DestroyImmediate(proxy);}
    }
    static void Capture(string label)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){report.AppendLine("OFFSCREEN "+label+": skipped on null graphics device; use saved Unity art captures for visual review.");return;}
        var camera=Camera.main;var old=camera.targetTexture;var previous=RenderTexture.active;
        var rt=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var timings=new List<double>();var clock=new System.Diagnostics.Stopwatch();
        try
        {
            camera.targetTexture=rt;
            for(int i=0;i<23;i++)
            {
                clock.Restart();camera.Render();RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();clock.Stop();
                if(i>=3)timings.Add(clock.Elapsed.TotalMilliseconds);
            }
            Directory.CreateDirectory("Logs/MapRevision");File.WriteAllBytes("Logs/MapRevision/play-"+label+".jpg",pixels.EncodeToJPG(90));
            timings.Sort();report.AppendLine($"OFFSCREEN {label} 1280x720, 20 samples, render+GPU readback: median={timings[10]:F1}ms p95={timings[18]:F1}ms. Excludes gameplay update cost; not standalone FPS.");
        }
        finally{camera.targetTexture=old;RenderTexture.active=previous;Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);}
    }
    static void Finish()
    {
        report.AppendLine("Failures: "+failures);Directory.CreateDirectory("Logs/MapRevision");File.WriteAllText("Logs/MapRevision/play-validation.txt",report.ToString());
        HDRPSlicePlayer.DevInvulnerable=false;finishing=true;SessionState.SetBool(Pending,false);EditorApplication.isPlaying=false;
        if(Application.isBatchMode) EditorApplication.delayCall+=()=>EditorApplication.Exit(failures==0?0:1);
    }
}
