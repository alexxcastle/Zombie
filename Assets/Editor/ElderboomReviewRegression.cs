using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class ElderboomReviewRegression
{
    const string Command="ElderboomReviewRegression.Run";
    const string MenuRunKey="ElderboomReviewRegression.MenuRun";
    // Same regression from the Editor menu: survives the play-mode domain reload via
    // SessionState and leaves the Editor open when it finishes.
    [MenuItem("Elderboom HDRP/Run Review Regression")]
    static void RunFromMenu(){SessionState.SetBool(MenuRunKey,true);Run();}
    static readonly StringBuilder report=new StringBuilder();
    static readonly List<GameObject> fixtures=new List<GameObject>();
    static readonly List<string> assets=new List<string>();
    static int errors, checks;
    static bool finished;
    static double deadline;
    static HDRPSlicePlayer player;
    static ElderboomGame game;
    static Vector3 startPosition;
    static Quaternion startRotation;
    static int oldFullscreen,oldResolution,oldQuality;
    static bool hadFullscreen,hadResolution,hadQuality;
    static void Check(bool value,string message)
    {
        checks++;report.AppendLine((value?"PASS ":"FAIL ")+message);
        if(!value){errors++;Debug.LogError("REVIEW_CHECK: "+message);}
    }
    public static void Run()
    {
        ElderboomPortValidation.ValidateSaved();
        EditorApplication.playModeStateChanged+=OnPlay;EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]static void Restore()
    {
        if(Environment.GetCommandLineArgs().Contains(Command) || SessionState.GetBool(MenuRunKey,false))
        {EditorApplication.playModeStateChanged-=OnPlay;EditorApplication.playModeStateChanged+=OnPlay;}
    }
    static void OnLog(string text,string stack,LogType type)
    {
        if(finished || text.StartsWith("REVIEW_CHECK:"))return;
        if((type==LogType.Error || type==LogType.Exception || type==LogType.Assert) && !text.StartsWith("Curl error"))
        {errors++;report.AppendLine("RUNTIME_ERROR "+text);}
    }
    static bool hadRunInBackground;
    static void OnPlay(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode)return;
        report.Clear();errors=checks=0;finished=false;deadline=EditorApplication.timeSinceStartup+900;
        game=UnityEngine.Object.FindAnyObjectByType<ElderboomGame>();player=UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        startPosition=player.transform.position;startRotation=player.transform.rotation;
        hadFullscreen=PlayerPrefs.HasKey(ElderboomDisplaySettings.FullscreenKey);oldFullscreen=PlayerPrefs.GetInt(ElderboomDisplaySettings.FullscreenKey,1);
        hadResolution=PlayerPrefs.HasKey(ElderboomDisplaySettings.ResolutionKey);oldResolution=PlayerPrefs.GetInt(ElderboomDisplaySettings.ResolutionKey,1);
        hadQuality=PlayerPrefs.HasKey(ElderboomDisplaySettings.QualityKey);oldQuality=PlayerPrefs.GetInt(ElderboomDisplaySettings.QualityKey,1);
        // Keep the game ticking while the Editor is behind other windows; restored in Finish.
        hadRunInBackground=Application.runInBackground;Application.runInBackground=true;
        Application.logMessageReceived+=OnLog;
        EditorApplication.update+=Timeout;
        ElderboomValidationHost.Create().StartCoroutine(Guard(Verify()));
    }
    static void Timeout(){if(!finished && EditorApplication.timeSinceStartup>deadline){Check(false,"runtime regression timeout");Finish();}}
    static IEnumerator Guard(IEnumerator source)
    {
        var stack=new Stack<IEnumerator>();stack.Push(source);
        while(stack.Count>0 && !finished)
        {
            object next=null;
            try
            {
                var iterator=stack.Peek();
                if(!iterator.MoveNext()){stack.Pop();continue;}
                next=iterator.Current;
            }
            catch(Exception exception){Check(false,exception.ToString());Finish();yield break;}
            if(next is IEnumerator nested){stack.Push(nested);continue;}
            yield return next;
        }
        Finish();
    }
    static IEnumerator Session(string op)
    {
        foreach(var go in fixtures)if(go!=null){go.SetActive(false);UnityEngine.Object.Destroy(go);}fixtures.Clear();
        Time.timeScale=1;player.transform.SetPositionAndRotation(startPosition,startRotation);
        game.BeginSession(op,"NORMAL");yield return null;game.StopAllCoroutines();player.enabled=false;
        player.ViewCamera.transform.localRotation=Quaternion.identity;
        yield return null;
    }
    static GameObject Track(GameObject go){fixtures.Add(go);return go;}
    static ElderboomZombie Target(Vector3 position,bool register=false,bool liveAgent=false,float health=100)
    {
        var go=Track(new GameObject("ReviewTarget"));go.transform.position=startPosition;
        var collider=go.AddComponent<CapsuleCollider>();collider.center=Vector3.up;collider.height=2;collider.radius=.35f;
        var z=go.AddComponent<ElderboomZombie>();z.CountsForWave=false;z.Initialize(game,player,health,0,0,100);
        if(!liveAgent)go.GetComponent<NavMeshAgent>().enabled=false;
        if(liveAgent && NavMesh.SamplePosition(position,out var hit,4,NavMesh.AllAreas))go.GetComponent<NavMeshAgent>().Warp(hit.position);
        else go.transform.position=position;
        if(register)((List<ElderboomZombie>)game.Zombies).Add(z);
        return z;
    }
    static GameObject Wall(Vector3 position,Vector3 size)
    {
        var go=Track(GameObject.CreatePrimitive(PrimitiveType.Cube));go.name="ReviewCover";go.transform.position=position;go.transform.localScale=size;return go;
    }
    static ElderboomWeaponDefinition Gun(string mode="hitscan",int pierce=1,float damage=5)
        =>new ElderboomWeaponDefinition{id="review",name="Review",category="assault_rifle",mode=mode,capacity=30,reserve=120,damage=damage,range=20,falloffStart=10,minimumDamage=.5f,spread=0,pellets=1,pierce=pierce,fireInterval=.08f};
    static void Shoot(ElderboomWeaponDefinition definition,Transform muzzle)
    {Physics.SyncTransforms();ElderboomWeaponFire.Fire(player,definition,player.ViewCamera,muzzle,false,0);}
    static IEnumerator Verify()
    {
        yield return Session("SOPH");
        Check(ElderboomWeapons.All.Select(w=>w.id).Distinct().Count()==ElderboomWeapons.All.Length,"weapon catalog has unique IDs");
        Check(player.MainMuzzle!=null && player.MainMuzzle.name=="Soph_Rifle_Muzzle","rifle uses its own muzzle rather than the plasma pistol marker");
        yield return BlindWaves();
        yield return Ballistics();
        yield return Inventory();
        yield return Abilities();
        yield return SettingsAndMaterials();
        yield return Session("SOPH");
        Check(game.Credits==1000 && game.TotalScore==0 && !game.AbilityBusy && !player.Reloading,"fresh session resets score, cooldown and action ownership");
    }
    static IEnumerator BlindWaves()
    {
        yield return Session("SOPH");
        for(int i=0;i<12;i++)
        {
            var z=Target(startPosition+Vector3.back*30+Vector3.right*i,true);z.ApplyPlasmaBlind(1);z.RetireFromWave();
        }
        Check(game.CanSpawn(10),"retired blind survivors do not consume active wave slots");
        for(int i=0;i<10;i++){var z=Target(startPosition+Vector3.forward*25+Vector3.right*i,true);z.CountsForWave=true;}
        Check(!game.CanSpawn(10),"living current-wave zombies still enforce pressure limit");
        foreach(var z in game.Zombies.Where(z=>z.CountsForWave))z.ApplyPlasmaBlind(1);
        Check(game.CanSpawn(10),"fully blinded current packet frees capacity for pending spawns");
        foreach(var z in game.Zombies.Where(z=>z.CountsForWave))z.TakeDamage(1000,Vector3.zero);
        Check(game.CanSpawn(10),"death animation corpses do not occupy pressure slots");
        yield return Session("SOPH");
        // Run the real wave loop, without replacing its spawn/countdown logic.
        game.BeginSession("SOPH","NORMAL");player.enabled=false;Time.timeScale=20;
        float limit=Time.time+240;int highest=0,worldPeak=0;
        var moved=new HashSet<ElderboomZombie>();
        while(game.Wave<4 && Time.time<limit)
        {
            highest=Mathf.Max(highest,game.Wave);
            int alive=0;
            foreach(var z in game.Zombies)
            {
                if(z==null || !z.IsAlive)continue;alive++;z.FreezeFor(500);z.ApplyPlasmaBlind(1);
                // Keep the fixture deterministic without freezing actors on top
                // of every spawn marker. Blind survivors normally wander away.
                if(z.CountsForWave && moved.Add(z))
                {
                    Vector3 away=startPosition+new Vector3(3+alive%5,0,3+alive/5);
                    if(NavMesh.SamplePosition(away,out var landing,3,NavMesh.AllAreas))z.GetComponent<NavMeshAgent>().Warp(landing.position);
                }
                if(!z.CountsForWave)
                {
                    var agent=z.GetComponent<NavMeshAgent>();if(agent!=null)agent.enabled=false;
                    z.transform.position=startPosition-Vector3.forward*40+Vector3.right*(alive%5);
                }
            }
            worldPeak=Mathf.Max(worldPeak,alive);yield return null;
        }
        Time.timeScale=1;
        Check(game.Wave>=4,"three consecutive blind waves advance to wave four (highest="+highest+")");
        Check(worldPeak<=32 && game.Credits==1000 && game.TotalScore==0,"world cap and uncredited retirement remain correct (peak="+worldPeak+")");
        game.StopAllCoroutines();
    }
    static IEnumerator Ballistics()
    {
        yield return Session("SOPH");
        var camera=player.ViewCamera;Vector3 origin=startPosition+Vector3.up*15;
        camera.transform.SetPositionAndRotation(origin,Quaternion.identity);
        var muzzle=Track(new GameObject("ReviewMuzzle")).transform;muzzle.position=origin+new Vector3(.6f,0,.8f);
        var target=Target(origin+Vector3.forward*5-Vector3.up);
        var first=Wall(origin+new Vector3(.3f,0,.4f),new Vector3(.12f,1.5f,.12f));
        Shoot(Gun(),muzzle);Check(target.Health==100,"camera-to-muzzle obstruction blocks shots even when reticle sees target");
        first.SetActive(false);
        var edge=Wall(origin+new Vector3(.42f,0,2),new Vector3(.14f,1.5f,.1f));
        Shoot(Gun(),muzzle);Check(target.Health==100,"cover beyond the muzzle blocks muzzle-to-reticle ray");
        edge.SetActive(false);
        var owned=Track(new GameObject("OwnedCollider"));owned.transform.SetParent(player.transform);owned.transform.position=origin+Vector3.forward;
        owned.AddComponent<BoxCollider>().size=new Vector3(.5f,.5f,.15f);
        Shoot(Gun(),muzzle);Check(Mathf.Abs(target.Health-95)<.01f,"reticle aim stays accurate and owner colliders are excluded");
        owned.SetActive(false);target.gameObject.SetActive(false);
        muzzle.position=origin;
        var a=Target(origin+Vector3.forward*4-Vector3.up);var b=Target(origin+Vector3.forward*6-Vector3.up);
        var child=Track(new GameObject("ExtraHitbox"));child.transform.SetParent(a.transform,false);child.transform.localPosition=Vector3.up;child.AddComponent<SphereCollider>().radius=.4f;
        Shoot(Gun(pierce:2),muzzle);Check(a.Health==95 && b.Health==95,"penetration damages two actors once despite multiple hitboxes");
        var between=Wall(origin+Vector3.forward*5,new Vector3(2,2,.08f));
        Shoot(Gun(pierce:3),muzzle);Check(a.Health==90 && b.Health==95,"penetration stops at solid cover");between.SetActive(false);
        a.gameObject.SetActive(false);b.gameObject.SetActive(false);
        var close=Target(origin+Vector3.forward*.5f-Vector3.up);
        muzzle.position=origin+Vector3.forward*.8f;Shoot(Gun(),muzzle);Check(close.Health==95,"target between camera and muzzle receives contact shot");close.gameObject.SetActive(false);
        muzzle.position=origin;
        var blastA=Target(origin+Vector3.forward*5-Vector3.up);var blastB=Target(origin+Vector3.forward*5+Vector3.right-Vector3.up);
        Shoot(Gun("explosive",damage:12),muzzle);yield return new WaitForSeconds(.35f);
        Check(blastA.Health<100 && blastB.Health<100,"grenade projectile detonates with area damage");
        blastA.gameObject.SetActive(false);blastB.gameObject.SetActive(false);
        var flame=Target(origin+Vector3.forward*5-Vector3.up);
        Shoot(Gun("flame",damage:3),muzzle);Check(flame.Health==97 && player.GetComponentInChildren<ElderboomFireStream>()!=null,"flamethrower applies sustained hit tick and creates visible stream");
        // Stream lifetime is ~1.23 s; 1.6 s leaves margin for low-frame-rate (background) runs.
        yield return new WaitForSeconds(1.6f);Check(player.GetComponentInChildren<ElderboomFireStream>()==null,"flame stream expires after trigger release");flame.gameObject.SetActive(false);
        var pulse=Target(origin+Vector3.forward*6-Vector3.up);Shoot(Gun("pulse",damage:9),muzzle);Check(pulse.Health==91,"pulse dispatches its area behavior");pulse.gameObject.SetActive(false);
        var behind=Target(origin+Vector3.forward*3-Vector3.up);
        var nearWall=Wall(origin+Vector3.forward*.2f,new Vector3(2,2,.04f));Physics.SyncTransforms();
        ElderboomPlasmaBolt.Fire(camera,origin+Vector3.forward*.45f,50,player,game);
        yield return new WaitForSeconds(.15f);Check(behind.Health==100,"plasma cannot spawn past cover between camera and muzzle");nearWall.SetActive(false);
        var thinWall=Wall(origin+Vector3.forward*.6f,new Vector3(2,2,.02f));Physics.SyncTransforms();
        ElderboomPlasmaBolt.Fire(camera,origin+Vector3.forward*.45f,50,player,game);
        yield return new WaitForSeconds(.2f);Check(behind.Health==100,"plasma collides with thin cover inside its first 80 cm");thinWall.SetActive(false);
        var kill=Target(origin+Vector3.forward*2-Vector3.up,true,health:1);behind.gameObject.SetActive(false);
        int score=game.TotalScore,credits=game.Credits;Shoot(Gun(),muzzle);
        Check(game.TotalScore==score+100 && game.Credits==credits+100 && player.Score==game.TotalScore && player.HitMarkerVisible,"weapon kill feeds unified HUD score, credits and hitmarker");
        var abilityKill=Target(origin+Vector3.forward*3-Vector3.up,true,health:1);
        abilityKill.TakeDamage(50,origin);Check(game.TotalScore==score+200 && player.Score==game.TotalScore,"ability kill contributes to the same displayed score");
        game.ShowNotice("REGRESSION PURCHASE FEEDBACK");Check(game.Notice.Contains("PURCHASE") && game.AbilityName.Length>0,"HUD exposes notice and selected ability feedback");
    }
    static IEnumerator Inventory()
    {
        yield return Session("SOPH");var arsenal=player.GetComponent<ElderboomArsenal>();
        Check(arsenal.Count==3 && arsenal.MaxSlots==3 && arsenal.Equip("HK53"),"Soph retains all three requested weapons");
        yield return Session("ELI");arsenal=player.GetComponent<ElderboomArsenal>();
        Check(arsenal.Count==2 && arsenal.MaxSlots==2,"other operators start with two regular slots");
        Check(arsenal.Add(ElderboomWeapons.Find("ar9")) && arsenal.Count==2 && !arsenal.Contains("sig550"),"purchasing with full inventory replaces selected regular weapon");
        var station=UnityEngine.Object.FindObjectsByType<ElderboomStation>().First(s=>s.category=="slot");
        int credits=game.Credits;Check(station.TryPurchase(player) && arsenal.MaxSlots==3 && game.Credits==credits-500,"third slot unlock charges exactly 500 credits");
        Check(!station.TryPurchase(player) && game.Credits==credits-500,"duplicate slot purchase is rejected without a charge");
        Check(arsenal.Add(ElderboomWeapons.Find("carbine")) && arsenal.Count==3,"unlocked third slot accepts an additional weapon");
        Check(arsenal.Add(ElderboomWeapons.Find("shotgun")) && arsenal.Count==3 && !arsenal.Contains("carbine"),"replacement remains bounded after slot upgrade");
        Check(arsenal.Equip("knife") && arsenal.Count==3,"permanent knife does not consume regular inventory slot");
        arsenal.Equip("ar9");player.ConfigureWeapon(ElderboomWeapons.Find("ar9"),14,51);
        Check(arsenal.ReceiveTemporary(ElderboomWeapons.Find("grenade"),.15f) && arsenal.Count==3 && player.Ammo==6,"temporary weapon preserves regular inventory");
        yield return new WaitForSeconds(.3f);
        Check(!arsenal.HasTemporary && player.WeaponDisplayName=="AR-9" && player.Ammo==14 && player.ReserveAmmo==51,"temporary expiry restores previous weapon and exact ammunition");
        arsenal.ReceiveTemporary(ElderboomWeapons.Find("grenade"),.2f);arsenal.ReceiveTemporary(ElderboomWeapons.Find("flame"),.15f);
        yield return new WaitForSeconds(.3f);
        Check(!arsenal.HasTemporary && player.Ammo==14 && player.ReserveAmmo==51,"replacement temporary drop retains original return slot");
        var pickup=ElderboomSpecialPickup.Spawn(startPosition+Vector3.right*10,ElderboomWeapons.Find("minigun"));
        Check(pickup!=null && pickup.TryCollect(player) && !pickup.TryCollect(player) && arsenal.TemporarySelected,"real drop pickup collects exactly once into temporary slot");
        arsenal.ExpireTemporary();
        var weapons=UnityEngine.Object.FindObjectsByType<ElderboomStation>().First(s=>s.category=="weapons");
        string[] original=weapons.weapons;weapons.weapons=new[]{"nonexistent"};credits=game.Credits;
        Check(!weapons.TryPurchase(player) && game.Credits==credits,"missing weapon never consumes purchase credits");weapons.weapons=original;
    }
    static IEnumerator Abilities()
    {
        foreach(float delay in new[]{.2f,1.8f})
        {
            yield return Session("SOPH");var arsenal=player.GetComponent<ElderboomArsenal>();arsenal.Equip("hk53");
            game.SelectAbility(0);game.UseSelectedAbility(player);yield return new WaitForSeconds(delay);player.TakeDamage(1000);
            yield return new WaitForSeconds(2f);
            Check(!game.IsPlaying && !game.AbilityBusy && !UnityEngine.Object.FindObjectsByType<ElderboomPlasmaBolt>().Any(b=>b.Owner==player),"death cancels volley before/after release at "+delay+"s");
            Check(player.ActiveWeaponRoot.GetComponent<ElderboomViewmodel>().authoredReloadTiming,"cancelled plasma pose restores HK53");
        }
        foreach(float delay in new[]{.2f,1f})
        {
            yield return Session("SOPH");game.SelectAbility(1);var target=Target(startPosition+Vector3.forward*4,true);
            game.UseSelectedAbility(player);yield return new WaitForSeconds(delay);player.TakeDamage(1000);yield return new WaitForSeconds(3.5f);
            Check(!target.PlasmaBlinded && GameObject.Find("Soph_Plasma_Blinding_Flare")==null,"death cancels flare during phase at "+delay+"s");
        }
        yield return Session("ELI");var frozen=Target(startPosition+Vector3.forward*3,true);
        game.SelectAbility(2);game.UseSelectedAbility(player);yield return new WaitForSeconds(1.5f);Check(frozen.IsFrozen,"freeze begins at authored gesture");
        player.TakeDamage(1000);yield return new WaitForSeconds(.3f);Check(!frozen.IsFrozen,"death removes ongoing freeze effect");
        yield return Session("ELI");game.SelectAbility(0);game.UseSelectedAbility(player);yield return new WaitForSeconds(1.5f);
        Check(game.PlayerIgnored,"ignore effect activates");player.TakeDamage(1000);Check(!game.PlayerIgnored,"death removes ignore effect");
        // Eli ability 2: the acoustic pulse lures nearby zombies to investigate the decoy point.
        yield return Session("ELI");var lured=Target(startPosition+player.transform.forward*8,true,true);
        game.SelectAbility(1);game.UseSelectedAbility(player);yield return new WaitForSeconds(1.6f);
        Check(lured.State==ElderboomZombieState.Investigate,"acoustic pulse lures zombies to the decoy (state="+lured.State+")");player.TakeDamage(1000);
        yield return Session("TACTICAL");var liftTarget=Target(startPosition+Vector3.forward*3,true,true);float originalY=liftTarget.transform.position.y;
        game.SelectAbility(1);game.UseSelectedAbility(player);yield return new WaitForSeconds(.7f);player.TakeDamage(1000);yield return new WaitForSeconds(.5f);
        Check(Mathf.Abs(liftTarget.transform.position.y-originalY)<.1f && liftTarget.Health==100 && !player.GetComponent<ElderboomTacticalArms>().Playing,"cancelled lift lands target without delayed slam damage");
        yield return Session("TACTICAL");game.SelectAbility(2);game.UseSelectedAbility(player);yield return new WaitForSeconds(.6f);player.TakeDamage(1000);yield return null;
        Check(!player.GetComponent<ElderboomTacticalArms>().Playing && player.transform.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>().Length==0,"cancelled keep-away removes field and overlay");
        yield return Session("SOPH");var a=player.GetComponent<ElderboomArsenal>();a.Equip("hk53");player.ConfigureWeapon(ElderboomWeapons.Find("hk53"),19,120);
        player.StartCoroutine((IEnumerator)typeof(HDRPSlicePlayer).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null));
        yield return new WaitForSeconds(.2f);player.TakeDamage(1000);yield return new WaitForSeconds(3.6f);
        Check(player.Ammo==19 && player.ReserveAmmo==120 && !player.Reloading,"death cancels pending reload ammunition transfer");
        yield return Session("SOPH");game.UseSelectedAbility(player);yield return new WaitForSeconds(.2f);
        yield return Session("ELI");yield return new WaitForSeconds(2f);
        Check(!UnityEngine.Object.FindObjectsByType<ElderboomPlasmaBolt>().Any() && !game.AbilityBusy && player.Health==100,"session replacement invalidates delayed prior-session effects");
    }
    static IEnumerator SettingsAndMaterials()
    {
        yield return Session("ELI");
        ElderboomDisplaySettings.SaveDisplay(false,0,QualitySettings.GetQualityLevel());
        var menu=Track(new GameObject("ColdMenuInitialization"));menu.AddComponent<ElderboomMenu>();
        Check(!ElderboomDisplaySettings.Fullscreen && ElderboomDisplaySettings.Resolution==0,"menu initialization preserves saved window mode and resolution");
        UnityEngine.Object.Destroy(menu);yield return null;
        var source=new Material(Shader.Find("Standard")){name="ReviewPbrFixture"};
        Texture2D Solid(Color color){var tex=new Texture2D(2,2,TextureFormat.RGBA32,false,true);tex.SetPixels(Enumerable.Repeat(color,4).ToArray());tex.Apply();return tex;}
        var metallic=Solid(new Color(.7f,0,0,.35f));var ao=Solid(new Color(.4f,.4f,.4f));var normal=Solid(new Color(.5f,.5f,1));
        source.SetTexture("_MetallicGlossMap",metallic);source.SetTexture("_OcclusionMap",ao);source.SetTexture("_BumpMap",normal);source.SetFloat("_GlossMapScale",1);
        var converted=ElderboomMaterialRepair.Convert(source,"review_validation");string path=AssetDatabase.GetAssetPath(converted);assets.Add(path);assets.Add(Path.ChangeExtension(path,".png"));
        Check(ElderboomMaterialRepair.NativeHdrp(converted) && converted.GetTexture("_NormalMap")==normal,"PBR conversion keeps normal map and HDRP shader");
        var mask=converted.GetTexture("_MaskMap");var rt=RenderTexture.GetTemporary(2,2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);Graphics.Blit(mask,rt);
        var previous=RenderTexture.active;RenderTexture.active=rt;var read=new Texture2D(2,2,TextureFormat.RGBA32,false,true);read.ReadPixels(new Rect(0,0,2,2),0,0);read.Apply();var pixel=read.GetPixel(0,0);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
        Check(Mathf.Abs(pixel.r-.7f)<.04f && Mathf.Abs(pixel.g-.4f)<.04f && Mathf.Abs(pixel.a-.35f)<.04f,"HDRP mask retains metallic, AO and gloss channels ("+pixel+")");
        Check(ElderboomMaterialRepair.Convert(converted,"identity")==converted,"compatible HDRP material is preserved by identity");
        UnityEngine.Object.Destroy(source);UnityEngine.Object.Destroy(metallic);UnityEngine.Object.Destroy(ao);UnityEngine.Object.Destroy(normal);UnityEngine.Object.Destroy(read);
        foreach(var definition in ElderboomWeapons.All.Where(d=>!d.IsOperatorPrimary))
        {
            var prefab=Resources.Load<GameObject>(definition.resource);
            Check(prefab!=null && prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).All(m=>m==null||ElderboomMaterialRepair.NativeHdrp(m)),"PBR-compatible materials in "+definition.id);
            Check(ElderboomWeaponFraming.FindNamed(prefab.transform,"MainWeapon_Muzzle","HK53_Muzzle")!=null,"physical muzzle marker in "+definition.id);
            if(definition.asset.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))
            {
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(definition.asset);
                var model=prefab.GetComponent<ElderboomViewmodel>().model.transform;
                var bindings=original.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>AnimationUtility.CalculateTransformPath(r.transform,original.transform));
                bool exact=model.GetComponentsInChildren<Renderer>(true).All(r=>bindings.TryGetValue(AnimationUtility.CalculateTransformPath(r.transform,model),out var sourceRenderer) && r.sharedMaterials.SequenceEqual(sourceRenderer.sharedMaterials));
                Check(exact,"glTF shader graph and all authored PBR bindings preserved in "+definition.id);
            }
        }
    }
    static void RestorePref(string key,bool existed,int value){if(existed)PlayerPrefs.SetInt(key,value);else PlayerPrefs.DeleteKey(key);}
    static void Finish()
    {
        if(finished)return;finished=true;Time.timeScale=1;Application.runInBackground=hadRunInBackground;
        RestorePref(ElderboomDisplaySettings.FullscreenKey,hadFullscreen,oldFullscreen);RestorePref(ElderboomDisplaySettings.ResolutionKey,hadResolution,oldResolution);RestorePref(ElderboomDisplaySettings.QualityKey,hadQuality,oldQuality);PlayerPrefs.Save();
        foreach(var path in assets)AssetDatabase.DeleteAsset(path);assets.Clear();
        report.AppendLine("checks="+checks+" errors="+errors);Directory.CreateDirectory("Logs/review");File.WriteAllText("Logs/review/regression.txt",report.ToString());
        Application.logMessageReceived-=OnLog;EditorApplication.update-=Timeout;
        Debug.Log("ELDERBOOM_REVIEW_REGRESSION_"+(errors==0?"PASSED":"FAILED"));
        int exit=errors==0?0:1;EditorApplication.isPlaying=false;
        if(SessionState.GetBool(MenuRunKey,false)){SessionState.EraseBool(MenuRunKey);return;}
        EditorApplication.delayCall+=()=>EditorApplication.Exit(exit);
    }
}
