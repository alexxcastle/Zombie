using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
static class ElderboomHorseAssemblyValidation
{
    const string Pending="elderboom.horse.assembly.validation";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static double next,started;
    static int stage,failures;
    static StringBuilder report=new StringBuilder();
    static ElderboomHorseAssembly quest;
    static ElderboomGame game;
    static ElderboomMount horse;
    static ElderboomHorseAssemblyValidation(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        if(EditorSceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save map before validation");
        SessionState.SetBool(Pending,true);EditorApplication.isPlaying=true;
    }
    static void Check(bool ok,string message){report.AppendLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
        double now=EditorApplication.timeSinceStartup;
        if(started==0){started=now;next=now+5;}
        if(now<next)return;
        try
        {
            if(now-started>180)throw new TimeoutException("Horse validation timeout");
            if(stage==0)
            {
                quest=ElderboomHorseAssembly.Instance;game=ElderboomGame.Active;horse=ElderboomMount.Instance;
                if(quest==null||game==null||horse==null)throw new InvalidOperationException("Quest, game or horse missing in Play Mode");
                game.StopAllCoroutines();game.Player.enabled=false;HDRPSlicePlayer.DevInvulnerable=true;
                foreach(var z in game.Zombies.ToArray())if(z!=null)Object.Destroy(z.gameObject);
                Check(quest.parts.Length==5,"five authored components");
                Check(!horse.Assembled&&!horse.GetComponent<CharacterController>().enabled&&!horse.transform.Find("MechHorse_Rider").gameObject.activeSelf,"no completed horse visible or rideable at start");
                Check(quest.CollectedMask==0&&!quest.Built&&quest.workshopVisual.activeSelf,"empty shared inventory and visible chassis");
                var state=typeof(ElderboomGame).GetField("state",Private);state.SetValue(game,Enum.Parse(state.FieldType,"Playing"));Time.timeScale=1;
                Check(!quest.TryInteract(ElderboomHorseAssembly.Assemble,quest.transform.position,true,quest.Revision),"assembly rejected before components recovered");
                Check(!quest.TryInteract(0,quest.parts[0].anchor.position,true,quest.Revision),"component in locked zone rejected");
                foreach(var a in ElderboomArea.All)ElderboomArea.Unlock(a.areaName);
                foreach(var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsSortMode.None))g.NetOpen();
                stage=1;next=now+3;return;
            }
            if(stage==1)
            {
                for(byte i=0;i<quest.parts.Length;i++)
                {
                    var part=quest.parts[i];Vector3 actor=Approach(i);
                    Check(NavMesh.SamplePosition(actor,out _,1,NavMesh.AllAreas),"walkable interaction spot: "+part.label);
                    var path=new NavMeshPath();
                    bool reachable=NavMesh.SamplePosition(quest.transform.position+Vector3.right*1.8f,out var start,3,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,actor,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                    Check(reachable,"open-gate route from workshop: "+part.label);
                    Check(!quest.TryInteract(i,actor+Vector3.up*20,true,quest.Revision),"remote collection rejected: "+part.label);
                    Check(!quest.TryInteract(i,actor,false,quest.Revision),"dead actor rejected: "+part.label);
                    Check(!quest.TryInteract(i,actor,true,quest.Revision+1),"stale/racing request rejected: "+part.label);
                    if(i==0)
                    {
                        Vector3 eye=actor+Vector3.up*1.3f,target=part.anchor.position+Vector3.up*.6f;
                        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=(eye+target)*.5f;wall.transform.rotation=Quaternion.LookRotation(target-eye);wall.transform.localScale=new Vector3(3,3,.15f);Physics.SyncTransforms();
                        Check(!quest.TryInteract(i,actor,true,quest.Revision),"cannot recover through a wall");Object.DestroyImmediate(wall);Physics.SyncTransforms();
                    }
                    Check(quest.TryInteract(i,actor,true,quest.Revision),"recover "+part.label);
                    Check(!part.visual.activeSelf,"collected geometry hidden: "+part.label);
                    Check(!quest.TryInteract(i,actor,true,quest.Revision),"duplicate recovery rejected: "+part.label);
                }
                Check(quest.Ready&&!horse.Assembled,"all components still require return and assembly");
                Vector3 workshop=Approach(ElderboomHorseAssembly.Assemble);
                Check(quest.TryInteract(ElderboomHorseAssembly.Assemble,workshop,true,quest.Revision),"assemble at workshop");
                Check(horse.Assembled&&horse.GetComponent<CharacterController>().enabled&&!quest.workshopVisual.activeSelf,"assembled horse replaces supported chassis");
                Check(horse.transform.Find("MechHorse_Rider").gameObject.activeSelf,"complete animated model enabled");
                // Exercise the real network snapshot serializer/deserializer, including late-join state.
                var lanObject=new GameObject("HorseSnapshotProbe");var lan=lanObject.AddComponent<ElderboomLan>();
                using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))using(var reader=new BinaryReader(stream))
                {
                    typeof(ElderboomLan).GetMethod("WriteHorse",Private).Invoke(lan,new object[]{writer});
                    quest.ApplySnapshot(0,false,quest.Revision);Check(!horse.Assembled,"snapshot fixture locked");stream.Position=0;
                    typeof(ElderboomLan).GetMethod("ReadHorse",Private).Invoke(lan,new object[]{reader});
                    Check(quest.Built&&quest.Ready&&horse.Assembled&&stream.Position==stream.Length,"snapshot restores all parts, assembly and consumes exact payload");
                    quest.ApplySnapshot(0,false,quest.Revision-1);Check(quest.Built,"out-of-order snapshot cannot undo assembly");
                }
                Object.DestroyImmediate(lanObject);
                typeof(ElderboomMount).GetMethod("Mount",Private).Invoke(horse,null);
                Check(horse.Mounted,"completed horse can be mounted");
                ElderboomEvents.RaiseSessionStarted();
                Check(!horse.Mounted&&!horse.Passenger&&!horse.Assembled,"session reset safely dismounts and locks horse");
                Check(!quest.Built&&quest.CollectedMask==0&&quest.parts.All(p=>p.visual.activeSelf)&&quest.workshopVisual.activeSelf,"session reset restores every component and chassis");
                Check(Vector3.Distance(horse.transform.position,quest.transform.position)<.01f,"session reset returns horse to workshop");
                Finish();
            }
        }
        catch(Exception e){Check(false,e.ToString());Finish();}
    }
    static Vector3 Approach(byte item)
    {
        Vector3 at=quest.InteractionPoint(item);
        for(int k=0;k<16;k++)
        {
            var p=at+Quaternion.Euler(0,k*22.5f,0)*Vector3.right*1.8f;
            if(NavMesh.SamplePosition(p,out var hit,.7f,NavMesh.AllAreas)&&quest.Reachable(item,hit.position))return hit.position;
        }
        throw new InvalidOperationException("No unobstructed interaction approach for "+item);
    }
    static void Finish()
    {
        report.AppendLine("Failures: "+failures);Directory.CreateDirectory("Logs/HorseAssembly");File.WriteAllText("Logs/HorseAssembly/play-validation.txt",report.ToString());
        SessionState.SetBool(Pending,false);EditorApplication.isPlaying=false;
    }
}
