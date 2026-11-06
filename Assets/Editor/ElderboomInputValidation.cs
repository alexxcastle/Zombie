using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

// Deterministic regression cases: no synthetic input is sent to the user's desktop.
public static class ElderboomInputValidation
{
    public static void BuildVerified()
    {
        Run();
        HDRPSliceStandaloneBuild.BuildPort();
    }

    public static void Run()
    {
        var report=new StringBuilder();int failures=0;
        void Check(bool ok,string message){report.AppendLine((ok?"PASS ":"FAIL ")+message);if(!ok)failures++;}
        var type=typeof(ElderboomInput).Assembly.GetType("ElderboomButtonState",true);
        var sample=type.GetMethod("Sample");var held=type.GetProperty("Held");var pressed=type.GetProperty("Pressed");
        object state=Activator.CreateInstance(type,true);int frame=0;
        void Tick(bool h,bool d,bool u,bool? physical){sample.Invoke(state,new object[]{++frame,h,d,u,physical});}
        bool Held()=>(bool)held.GetValue(state);bool Down()=>(bool)pressed.GetValue(state);
        Tick(true,false,false,false);Check(!Held()&&!Down(),"startup stale A held, OS released: no movement");
        Tick(true,false,false,null);Check(!Held(),"unsupported native query cannot start movement from held alone");
        Tick(false,false,false,true);Check(!Held(),"native state alone cannot create input");
        Tick(true,true,false,true);Check(Held()&&Down(),"fresh key press starts movement");
        sample.Invoke(state,new object[]{frame,false,false,true,false});Check(Held()&&Down(),"same-frame consumers share press");
        Tick(true,false,false,true);Check(Held()&&!Down(),"holding continues without repeated press");
        Tick(true,false,false,false);Check(!Held(),"lost Unity key-up is vetoed by OS release");
        Tick(true,false,false,true);Check(!Held(),"stale held cannot re-arm without a new press");
        Tick(true,true,false,true);Check(Held(),"new press recovers normally");
        Tick(false,false,true,false);Check(!Held()&&!Down(),"ordinary release stops movement");
        Tick(true,true,false,null);Check(Held()&&Down(),"unmapped key supports fresh legacy press");
        Tick(false,false,true,null);Check(!Held(),"unmapped key release stops movement");
        Tick(false,true,true,false);Check(Down()&&!Held(),"fast press/release keeps one click without stuck fire");
        Tick(true,true,false,false);Check(!Held()&&!Down(),"stale key-down rejected when OS reports released");
        state=Activator.CreateInstance(type,true);Tick(true,false,false,true);Check(!Held(),"fresh state after focus loss requires new press");
        Tick(true,true,false,true);Check(Held(),"key works after focus restore and fresh press");

        // Use the actual reset path, including disabled-domain-reload test override cleanup.
        ElderboomInput.HeldOverride=_=>true;
        typeof(ElderboomInput).GetMethod("ResetStatics",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        Check(ElderboomInput.HeldOverride==null,"play/session statics clear test override");
        ElderboomInput.ReleaseAll();
        Check(!ElderboomInput.AcceptsInput&&!ElderboomInput.Held("move_left")&&!ElderboomInput.Down("fire"),"focus/menu flush suppresses movement and fire in transition frame");
        var focus=typeof(ElderboomInput).GetMethod("OnFocusChanged",BindingFlags.NonPublic|BindingFlags.Static);
        focus.Invoke(null,new object[]{false});Check(!ElderboomInput.Held("move_left"),"focus loss clears actions");
        focus.Invoke(null,new object[]{true});Check(!ElderboomInput.Held("move_left"),"focus gain cannot replay old actions");
#if UNITY_EDITOR_OSX
        foreach(string name in new[]{"CGEventSourceKeyState","CGEventSourceButtonState"})
        {
            var method=typeof(ElderboomNativeKeys).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
            Check(method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value==UnmanagedType.I1,name+" uses native one-byte bool ABI");
        }
        // Read only one gameplay key; do not make assumptions about whether the user is pressing it.
        Check(ElderboomNativeKeys.Read(KeyCode.A).HasValue,"macOS native A-state query executes successfully");
#endif
        var player=typeof(HDRPSlicePlayer);
        float Constant(string name)=>(float)player.GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        float duration=100/Constant("SprintDrainPerSecond");float recovery=Constant("StaminaRecoveryDelay")+100/Constant("StaminaRecoveryPerSecond");
        Check(duration>5&&duration<6,"sprint duration slightly extended: "+duration.ToString("F2")+"s");
        Check(recovery>4.8f&&recovery<5.5f,"full recovery including delay: "+recovery.ToString("F2")+"s");
        report.AppendLine("Failures: "+failures);Directory.CreateDirectory("Logs/InputFix");File.WriteAllText("Logs/InputFix/regression.txt",report.ToString());
        if(failures!=0)throw new Exception("Input regression failures: "+failures);
        Debug.Log("[ElderboomInputValidation] All input and stamina regression checks passed");
    }
}
