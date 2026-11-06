using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object=UnityEngine.Object;

static partial class ElderboomAltarIntegration
{
    static StringBuilder checks = new StringBuilder();
    static void Check(bool ok, string text) { checks.AppendLine((ok?"PASS ":"FAIL ")+text); if(!ok)throw new InvalidOperationException(text); }
    static void ValidateAndCapture()
    {
        var station=Object.FindAnyObjectByType<ElderboomAltarUpgrader>();
        Check(station!=null,"station installed in saved scene");
        Check(station.weaponSocket!=null && station.cycle!=null,"socket and synchronized clip linked");
        var controls=station.model.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("CTRL_")).ToArray();
        Check(controls.Length==9,"nine independent animation controls");
        ElderboomAnimationSampling.Sample(station.cycle,station.model.gameObject,0);
        var start=controls.Select(t=>t.localToWorldMatrix).ToArray();
        ElderboomAnimationSampling.Sample(station.cycle,station.model.gameObject,3);
        Check(controls.Where((t,i)=>t.localToWorldMatrix!=start[i]).Count()>=5,"tray, covers and clamps move through imported clip");
        Check(AssetDatabase.GetDependencies(Folder+"/AltarUpgrader.prefab",true).All(p=>!p.EndsWith(".glb")),"runtime prefab detached from original large GLB");
        var shader=Resources.Load<Shader>("Elderboom/Shaders/CrimsonVeins");
        Check(shader!=null && !ShaderUtil.ShaderHasError(shader),"crimson vein shader compiles");
        ElderboomAnimationSampling.Sample(station.cycle,station.model.gameObject,0);
        Capture(station,"altar-installed",false);
        ElderboomAnimationSampling.Sample(station.cycle,station.model.gameObject,3);
        Capture(station,"altar-processing",false);
        ElderboomAnimationSampling.Sample(station.cycle,station.model.gameObject,0);
        File.WriteAllText(Report+"/validation.txt",checks.ToString());
        File.WriteAllText(Report+"/result.txt","Installation and static validation passed");
    }
    static void Capture(ElderboomAltarUpgrader station,string name,bool weapon)
    {
        var go=new GameObject("Altar_Review_Camera");var cam=go.AddComponent<Camera>();go.AddComponent<HDAdditionalCameraData>();
        var forward=station.interactionPoint.position-station.transform.position;forward.y=0;forward.Normalize();
        go.transform.position=station.transform.position+forward*5.5f+Vector3.up*2.4f;
        go.transform.LookAt(station.transform.position+Vector3.up*.4f);
        cam.fieldOfView=58;cam.nearClipPlane=.08f;cam.farClipPlane=150;
        var rt=new RenderTexture(1280,900,24);var tex=new Texture2D(1280,900,TextureFormat.RGB24,false);
        cam.targetTexture=rt;
        for(int i=0;i<4;i++)cam.Render();
        RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,900),0,0);tex.Apply();
        File.WriteAllBytes(Report+"/"+name+".jpg",tex.EncodeToJPG(92));
        RenderTexture.active=null;cam.targetTexture=null;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);
    }
    const string Testing="elderboom.altar.test";
    static double testAt;
    static int step;
    static HDRPSlicePlayer testPlayer;
    static ElderboomArsenal testArsenal;
    static ElderboomAltarUpgrader testStation;
    static ElderboomGame testGame;
    static ElderboomWeaponDefinition original;
    static int beforeCredits;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void BatchInstallAndTest()
    {
        try { Run("install"); StartTest(); }
        catch(Exception e){File.WriteAllText(Report+"/result.txt","FAIL "+e);EditorApplication.Exit(1);}
    }
    public static void BatchTest()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        StartTest();
    }
    public static void BatchReviewAndFix()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var scene=EditorSceneManager.GetActiveScene();
        var station=Object.FindAnyObjectByType<ElderboomAltarUpgrader>();
        station.transform.rotation=Quaternion.Euler(0,270,0); // face the nave, away from the east wall
        if(station.transform.Find("Tray_Ready_Light")==null)
        {
            var ready=new GameObject("Tray_Ready_Light");ready.transform.SetParent(station.transform,false);
            ready.transform.localPosition=new Vector3(0,1.5f,0);
            var light=ready.AddComponent<Light>();light.type=LightType.Point;light.range=3.3f;
            light.color=new Color(.38f,.68f,1f);
            ready.AddComponent<HDAdditionalLightData>().volumetricDimmer=0;light.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;light.intensity=450f;
        }
        station.processLight.lightUnit=UnityEngine.Rendering.LightUnit.Lumen;station.processLight.intensity=220f;
        FixLighting(); PrefabUtility.SaveAsPrefabAsset(station.gameObject,Folder+"/AltarUpgrader.prefab");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        foreach(var root in scene.GetRootGameObjects())
        {
            bool keep=root.GetComponentInChildren<ElderboomAltarUpgrader>(true)!=null ||root.name.Contains("FallenCity_Church")
                ||root.GetComponentInChildren<UnityEngine.Rendering.Volume>(true)!=null
                ||root.GetComponentsInChildren<Light>(true).Any(l=>l.type==LightType.Directional);
            if(!keep)Object.DestroyImmediate(root);
        }
        EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();ValidateAndCapture();
        EditorApplication.Exit(0);
    }
    static void MakeHarness()
    {
        var cameraGo=new GameObject("TestCamera");cameraGo.tag="MainCamera";cameraGo.AddComponent<AudioListener>();var camera=cameraGo.AddComponent<Camera>();cameraGo.AddComponent<HDAdditionalCameraData>();
        var playerGo=new GameObject("Altar_Test_Player");playerGo.AddComponent<CharacterController>();cameraGo.transform.SetParent(playerGo.transform,false);cameraGo.transform.localPosition=Vector3.up*1.7f;
        testPlayer=playerGo.AddComponent<HDRPSlicePlayer>();testPlayer.enabled=false;
        testArsenal=playerGo.AddComponent<ElderboomArsenal>();testArsenal.enabled=false;
        typeof(ElderboomArsenal).GetField("player",Private).SetValue(testArsenal,testPlayer);
        var gameGo=new GameObject("Altar_Test_Game");testGame=gameGo.AddComponent<ElderboomGame>();testGame.enabled=false;
        var state=typeof(ElderboomGame).GetField("state",Private);state.SetValue(testGame,Enum.Parse(state.FieldType,"Playing"));
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/AltarUpgrader.prefab");testStation=Object.Instantiate(prefab).GetComponent<ElderboomAltarUpgrader>();
        testStation.transform.position=Vector3.zero;testStation.transform.rotation=Quaternion.identity;
        SeedSlot("rhino",true);
        var torch=new GameObject("Flashlight");torch.transform.SetParent(cameraGo.transform,false);torch.AddComponent<Light>().type=LightType.Spot;
        torch.AddComponent<HDAdditionalLightData>().volumetricDimmer=0;
        var volume=new GameObject("ReviewLighting").AddComponent<UnityEngine.Rendering.Volume>();volume.isGlobal=true;
        volume.sharedProfile=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        var lamp=new GameObject("Review_Key").AddComponent<Light>();lamp.type=LightType.Directional;lamp.gameObject.AddComponent<HDAdditionalLightData>();lamp.lightUnit=UnityEngine.Rendering.LightUnit.Lux;lamp.intensity=1200;lamp.transform.rotation=Quaternion.Euler(45,30,0);
    }
    static void SeedSlot(string id,bool real)
    {
        var definition=ElderboomWeapons.Find(id);GameObject root;
        if(real)root=Object.Instantiate(Resources.Load<GameObject>(definition.resource),testPlayer.ViewmodelParent,false);
        else {root=GameObject.CreatePrimitive(PrimitiveType.Cube);root.name="Test_Weapon";root.transform.SetParent(testPlayer.ViewmodelParent,false);root.transform.localScale=new Vector3(.07f,.1f,.6f);Object.DestroyImmediate(root.GetComponent<Collider>());}
        var slotType=typeof(ElderboomArsenal).GetNestedType("Slot",BindingFlags.NonPublic);var slot=Activator.CreateInstance(slotType);
        slotType.GetField("definition").SetValue(slot,definition);slotType.GetField("root").SetValue(slot,root.transform);
        slotType.GetField("ammo").SetValue(slot,definition.capacity);slotType.GetField("reserve").SetValue(slot,definition.reserve);
        var slots=(System.Collections.IList)typeof(ElderboomArsenal).GetField("slots",Private).GetValue(testArsenal);slots.Add(slot);
        typeof(ElderboomArsenal).GetMethod("EquipUnchecked",Private).Invoke(testArsenal,new[]{slot});
    }
    static void StartTest()
    {
        SessionState.SetBool(Testing,true);EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]static void InstallTestTick(){EditorApplication.update-=TestTick;EditorApplication.update+=TestTick;}
    static void TestTick()
    {
        if(!SessionState.GetBool(Testing,false)||!EditorApplication.isPlaying)return;
        if(testAt==0){testAt=EditorApplication.timeSinceStartup+8;return;}
        if(EditorApplication.timeSinceStartup<testAt)return;
        try
        {
            if(step==0)
            {
                checks=new StringBuilder();MakeHarness();
                testGame.StopAllCoroutines();HDRPSlicePlayer.DevInvulnerable=true;
                testStation=Object.FindAnyObjectByType<ElderboomAltarUpgrader>();
                Check(testGame.IsPlaying && testArsenal.CanUpgradeActive,"match initialized with eligible starting firearm");
                var point=testStation.interactionPoint.position;
                Vector3 outDir=point-testStation.transform.position;outDir.y=0;outDir.Normalize();
                var cc=testPlayer.GetComponent<CharacterController>();cc.enabled=false;
                testPlayer.transform.position=point+outDir*1.5f-Vector3.up*1.8f;cc.enabled=true;
                testPlayer.enabled=false;
                var camera=testPlayer.ViewCamera;camera.transform.LookAt(point);
                typeof(ElderboomGame).GetField("credits",Private).SetValue(testGame,10000);
                original=ElderboomWeapons.Find("rhino");beforeCredits=testGame.Credits;
                Check(testStation.TryBegin(testPlayer),"deposit accepted from front of altar");
                Check(testGame.Credits==beforeCredits-testStation.cost,"one payment at deposit");
                Check(testPlayer.WeaponDeposited && !testArsenal.CanSwitch && !testGame.CanInteract,"shooting/switching/other purchases locked during deposit");
                Check(!testStation.TryBegin(testPlayer),"second deposit rejected while busy");
                testAt=EditorApplication.timeSinceStartup+4;step=1;return;
            }
            if(step==1)
            {
                if(testStation.Progress<.64f){testAt=EditorApplication.timeSinceStartup+.25;return;}
                Check(testStation.Busy,"mechanical cycle running");
                testAt=EditorApplication.timeSinceStartup+4;step=2;return;
            }
            if(step==2)
            {
                if(testStation.Busy){testAt=EditorApplication.timeSinceStartup+.25;return;}
                Check(!testPlayer.WeaponDeposited && testArsenal.ActiveUpgraded && testArsenal.CanSwitch,"upgraded weapon returned to original inventory slot");
                Check(testPlayer.Ammo==Mathf.CeilToInt(original.capacity*1.5f) && testPlayer.ReserveAmmo==Mathf.CeilToInt(original.reserve*1.5f),"magazine and reserve increased and refilled");
                var def=(ElderboomWeaponDefinition)typeof(HDRPSlicePlayer).GetField("definition",Private).GetValue(testPlayer);
                Check(Mathf.Approximately(def.damage,original.damage*2) && !original.upgraded,"damage doubled without changing catalog");
                Check(testPlayer.ActiveWeaponRoot.GetComponentsInChildren<Renderer>(true).Any(r=>r.name.StartsWith("CrimsonSurface_")),"crimson surface meshes attached");
                Check(!testStation.TryBegin(testPlayer),"repeat upgrade rejected");
                SeedSlot("hk53",false);
                Check(testPlayer.WeaponDisplayName=="HK53","second weapon equipped");
                Check(!testArsenal.ActiveUpgraded,"new weapon retains base stats");
                Check(testArsenal.Equip("rhino") && testArsenal.ActiveUpgraded,"upgrade survives weapon switching");
                Check(testArsenal.Equip("hk53"),"HK53 selected for abort test");
                Check(testStation.TryBegin(testPlayer),"second owned firearm deposit accepted");
                testStation.Abort();
                Check(testGame.Credits==beforeCredits-testStation.cost,"aborted transaction refunded without score reward");
                Check(!testArsenal.ActiveUpgraded && !testPlayer.WeaponDeposited,"aborted weapon restored with original stats");
                Check(testArsenal.Equip("rhino"),"upgraded weapon restored for visual review");
                var flash=testPlayer.ViewCamera.GetComponentsInChildren<HDAdditionalLightData>(true).FirstOrDefault(l=>l.GetComponent<Light>().type==LightType.Spot);
                Check(flash!=null && flash.volumetricDimmer==0,"flashlight cannot generate opaque volumetric halo");
                // Render the real first-person model in a clear review pose.
                var c=testPlayer.ViewCamera;c.transform.LookAt(testStation.transform.position+Vector3.up*.7f);
                typeof(HDRPSlicePlayer).GetMethod("UpdateWeaponPose",Private).Invoke(testPlayer,new object[]{null,false});
                var socket=new GameObject("Weapon_Art_Review").transform;
                socket.position=new Vector3(0,2,-6);
                var review=ElderboomUpgradedWeapon.CreateDisplay(testPlayer.ActiveWeaponRoot,def,"SOPH",socket);
                Check(review!=null,"upgraded weapon display uses actual gun geometry without arms");
                ElderboomUpgradedWeapon.Apply(review.transform,null,"",true);
                c.transform.position=socket.position+new Vector3(.12f,.6f,-.38f);c.transform.LookAt(socket.position+Vector3.up*.12f);
                c.fieldOfView=40;CapturePlayer(c,"crimson-weapon-detail");
                Object.Destroy(socket.gameObject);
                File.WriteAllText(Report+"/play-validation.txt",checks.ToString());
                File.WriteAllText(Report+"/result.txt","PASS: installation, animation, payment, return, stats, switching, abort/refund, flashlight and render checks.");
                SessionState.SetBool(Testing,false);EditorApplication.Exit(0);
            }
        }
        catch(Exception e)
        {
            File.WriteAllText(Report+"/play-validation.txt",checks+"\nFAIL "+e);File.WriteAllText(Report+"/result.txt","FAIL "+e);
            SessionState.SetBool(Testing,false);EditorApplication.Exit(1);
        }
    }
    static void CapturePlayer(Camera camera,string name)
    {
        var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);camera.targetTexture=rt;
        for(int i=0;i<4;i++)camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
        File.WriteAllBytes(Report+"/"+name+".jpg",tex.EncodeToJPG(92));camera.targetTexture=null;RenderTexture.active=null;
        Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);
    }
}
