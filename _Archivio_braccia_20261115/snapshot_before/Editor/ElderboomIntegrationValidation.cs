using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class ElderboomIntegrationValidation
{
    const string Command = "ElderboomIntegrationValidation.Run";
    static readonly StringBuilder report = new StringBuilder();
    static int errors;
    [Serializable] class Receipt { public Reference[] reference; }
    [Serializable] class Reference { public string clip; public int frame; public float seconds; public float[] left_hand; }
    static void Check(bool condition, string message)
    {
        report.AppendLine((condition ? "PASS " : "FAIL ") + message);
        if (!condition) { errors++; Debug.LogError(message); }
    }
    public static void Run()
    {
        ElderboomHK53Builder.Build();
        ElderboomPortValidation.ValidateSaved();
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    static void Restore()
    {
        if(Environment.GetCommandLineArgs().Contains(Command))
        { EditorApplication.playModeStateChanged -= OnPlay; EditorApplication.playModeStateChanged += OnPlay; }
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(state != PlayModeStateChange.EnteredPlayMode) return;
        report.Clear(); errors = 0;
        var game = UnityEngine.Object.FindAnyObjectByType<ElderboomGame>();
        ElderboomValidationHost.Create().StartCoroutine(Verify(game));
    }
    static IEnumerator Verify(ElderboomGame game)
    {
        yield return null;
        game.BeginSession("SOPH", "NORMAL");
        yield return new WaitForSeconds(.5f);
        var player = UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        player.enabled = false;
        var camera = player.GetComponentInChildren<Camera>();
        var arsenal = player.GetComponent<ElderboomArsenal>();
        Check(arsenal.Count == 3, "Soph retains AKMN and Rhino, plus HK53");
        Check(arsenal.Equip("HK53"), "HK53 can be selected");
        Check(player.WeaponDisplayName == "HK53" && player.Ammo == 30, "HK53 loadout 30/120");
        var root = player.ActiveWeaponRoot;
        var view = root.GetComponent<ElderboomViewmodel>(); view.enabled = false;
        Check(view.idle.length == 4 && Mathf.Abs(view.ReloadDuration - 3.5f) < .001f, "authored idle/reload durations");
        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText("Logs/hk53_export.json"));
        var hand = ElderboomWeaponFraming.FindNamed(root, "DEF-hand.L");
        foreach(var reference in receipt.reference)
        {
            var clip = reference.clip == "HK53_Reload" ? view.reload : view.idle;
            clip.SampleAnimation(view.model, reference.seconds);
            var position = reference.left_hand;
            var expected = view.model.transform.TransformPoint(new Vector3(-position[0],position[2],-position[1]));
            float error = Vector3.Distance(hand.position,expected);
            Check(error < .001f,"left hand matches Blender frame "+reference.frame+" within 1 mm ("+(error*1000).ToString("F3")+" mm)");
        }
        view.ResetToIdle();
        var handle = ElderboomWeaponFraming.FindNamed(root, "HK53_Charging_Handle");
        var magazine = ElderboomWeaponFraming.FindNamed(root, "MOVE_Magazine_Assembly");
        view.ResetToIdle();
        Vector3 closedHandle = root.InverseTransformPoint(handle.position);
        Vector3 seatedMag = root.InverseTransformPoint(magazine.position);
        view.reload.SampleAnimation(view.model, .9f);
        Check(Vector3.Distance(root.InverseTransformPoint(handle.position), closedHandle) > .025f, "charging handle slide baked into reload");
        view.reload.SampleAnimation(view.model, 1.65f);
        Check(Vector3.Distance(root.InverseTransformPoint(magazine.position), seatedMag) > .08f, "magazine removal baked into reload");
        view.ResetToIdle();
        Check(Vector3.Distance(root.InverseTransformPoint(handle.position), closedHandle) < .0001f, "idle resets charging handle");
        Check(Vector3.Distance(root.InverseTransformPoint(magazine.position), seatedMag) < .0001f, "idle resets magazine");
        Directory.CreateDirectory("Logs/integration");
        camera.cullingMask = 1 << 19; camera.nearClipPlane = .02f; camera.fieldOfView = 72;
        var data = camera.GetComponent<HDAdditionalCameraData>();
        data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; data.backgroundColorHDR = new Color(.12f,.14f,.17f);
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
        var previewVolume = new GameObject("Validation Exposure").AddComponent<Volume>();
        previewVolume.isGlobal = true; previewVolume.priority = 100;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); previewVolume.sharedProfile = profile;
        var exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(7f);
        profile.Add<Fog>(true).enabled.Override(false);
        var blur = profile.Add<MotionBlur>(true); blur.intensity.Override(0);
        var dof = profile.Add<DepthOfField>(true); dof.focusMode.Override(DepthOfFieldMode.Off);
        var fillObject = new GameObject("Validation Fill"); fillObject.transform.SetParent(camera.transform,false); fillObject.transform.localPosition = new Vector3(.1f,.2f,0);
        var fill = fillObject.AddComponent<Light>(); fill.type = LightType.Point; fill.range = 5; fill.intensity = 250; fillObject.AddComponent<HDAdditionalLightData>();
        yield return null;
        foreach(string name in new[]{"HK53_Root","Camera_FPS","DEF-hand.L","DEF-hand.R"})
        {
            var node = ElderboomWeaponFraming.FindNamed(root,name);
            report.AppendLine(name+" camera-local "+camera.transform.InverseTransformPoint(node.position).ToString("F4"));
        }
        foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            report.AppendLine("renderer "+renderer.name+" bounds "+camera.transform.InverseTransformPoint(renderer.bounds.center).ToString("F3")+" size "+renderer.bounds.size.ToString("F3"));
        report.AppendLine(ElderboomAbilityShots.Capture(camera,"Logs/integration/hk53_hip.png"));
        root.localPosition = view.adsPosition; root.localRotation = view.adsRotation; camera.fieldOfView = 55;
        yield return null;
        yield return null;
        yield return null;
        var rear = ElderboomWeaponFraming.FindNamed(root,"MainWeapon_ADS_Rear"); var front = ElderboomWeaponFraming.FindNamed(root,"MainWeapon_ADS_Front");
        foreach(var marker in new[]{rear,front})
        {
            var p = camera.WorldToViewportPoint(marker.position);
            Check(p.z > .02f && Mathf.Abs(p.x-.5f)<.001f && p.y>.48f && p.y<.49f, "HK53 ADS marker slightly below centre: "+marker.name);
        }
        Check(Mathf.Abs(camera.WorldToViewportPoint(rear.position).y-camera.WorldToViewportPoint(front.position).y)<.001f,"lowered HK53 ADS preserves sight alignment");
        var rearMesh = ElderboomWeaponFraming.FindNamed(root,"Rear_Sight");
        var rearCollider = rearMesh.gameObject.AddComponent<MeshCollider>();
        rearCollider.sharedMesh = rearMesh.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
        var eyeRay = new Ray(camera.transform.position,(front.position-camera.transform.position).normalized);
        Check(!rearCollider.Raycast(eyeRay,out _,Vector3.Distance(camera.transform.position,front.position)),"ADS line passes through the open rear aperture rather than solid metal");
        var frontMesh = ElderboomWeaponFraming.FindNamed(root,"Front_Sight_Block");
        var frontCollider = frontMesh.gameObject.AddComponent<MeshCollider>();frontCollider.sharedMesh=frontMesh.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();
        var postRay = new Ray(camera.transform.position,(front.position-camera.transform.up*.0005f-camera.transform.position).normalized);
        Check(frontCollider.Raycast(postRay,out _,Vector3.Distance(camera.transform.position,front.position)+.01f),"front sight post intersects its expected visible screen ray");
        UnityEngine.Object.Destroy(rearCollider);UnityEngine.Object.Destroy(frontCollider);
        report.AppendLine("ADS pose " + view.adsPosition.ToString("F4") + " rotation " + view.adsRotation.eulerAngles);
        report.AppendLine(ElderboomAbilityShots.Capture(camera,"Logs/integration/hk53_ads.png"));
        root.localPosition = view.hipPosition; root.localRotation = view.hipRotation; camera.fieldOfView = 72;
        foreach(float seconds in new[]{.516667f,.9f,1.65f,2.35f,3.1f})
        {
            view.reload.SampleAnimation(view.model,seconds);
            yield return null;
            yield return null;
            yield return null;
            report.AppendLine(ElderboomAbilityShots.Capture(camera,"Logs/integration/hk53_reload_"+seconds.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));
        }
        view.ResetToIdle(); view.enabled = true;
        var ammoField = typeof(HDRPSlicePlayer).GetField("ammo",BindingFlags.Instance|BindingFlags.NonPublic);
        ammoField.SetValue(player,19);
        var reload = (IEnumerator)typeof(HDRPSlicePlayer).GetMethod("Reload",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null);
        player.StartCoroutine(reload);
        Check(player.Reloading && !arsenal.Equip("AKMN ZENITCO"),"reload prevents switching");
        yield return new WaitForSeconds(3.2f);
        Check(player.Reloading && player.Ammo == 19,"HK53 cannot refill before authored reload completes");
        yield return new WaitForSeconds(.4f);
        Check(!player.Reloading && player.Ammo == 30 && player.ReserveAmmo == 109,"reload completes with correct ammunition transfer");
        int count = arsenal.Count;
        Check(!arsenal.Add(ElderboomWeapons.Find("HK53")) && arsenal.Count == count,"duplicate purchase rejected without another weapon");
        game.UseSelectedAbility(player);
        Check(game.AbilityBusy && !arsenal.Equip("RHINO 60DS"),"ability locks weapon changes");
        Check(player.ActiveWeaponRoot != root && player.ActiveWeaponRoot.GetComponent<ElderboomViewmodel>().ability != null,"Soph uses authored plasma ability model while HK53 is equipped");
        yield return new WaitForSeconds(7f);
        Check(player.ActiveWeaponRoot == root && player.Ammo == 30 && player.ReserveAmmo == 109,"HK53 and ammunition restored after plasma ability");
        root.gameObject.SetActive(false);
        camera.cullingMask = ~0;
        var zombie = UnityEngine.Object.FindObjectsByType<ElderboomZombie>(FindObjectsSortMode.None).FirstOrDefault(z=>z.IsAlive);
        Check(zombie != null,"wave spawns zombies");
        if(zombie != null)
        {
            Vector3 point = zombie.transform.position + Vector3.up*1.2f;
            Vector3 normal = (camera.transform.position-point).normalized;
            float before = zombie.Health;
            zombie.TakeDamage(1, player.transform.position, point, normal);
            var blood = GameObject.Find("BloodImpact");
            Check(zombie.Health == before-1 && blood != null && Vector3.Distance(blood.transform.position,point)<.04f,"hit emits blood at impact point");
            int credits = game.Credits; zombie.TakeDamage(999,player.transform.position,point,normal);
            zombie.TakeDamage(999,player.transform.position,point,normal);
            Check(game.Credits == credits+100,"death credits awarded exactly once");
        }
        // Exercise the real weapon raycast, not only the damage receiver/VFX API.
        var targetObject = new GameObject("ValidationBulletTarget");
        targetObject.transform.position = player.transform.position;
        var collider = targetObject.AddComponent<CapsuleCollider>(); collider.center = Vector3.up * 1.1f; collider.height = 2f; collider.radius = .4f;
        var bulletTarget = targetObject.AddComponent<ElderboomZombie>();
        bulletTarget.CountsForWave = false;
        bulletTarget.Initialize(game,player,100,0,0,100);
        targetObject.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
        camera.transform.position = player.transform.position + Vector3.up * 12f;
        camera.transform.rotation = Quaternion.identity;
        targetObject.transform.position = camera.transform.position + camera.transform.forward * 4f - Vector3.up * 1.1f;
        Physics.SyncTransforms();
        typeof(HDRPSlicePlayer).GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,null);
        Check(Mathf.Abs(bulletTarget.Health-95f)<.01f,"HK53 Fire raycast damages target using configured damage");
        var shotBlood = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
            .FirstOrDefault(p=>p.name=="BloodImpact" && Vector3.Distance(p.transform.position,collider.ClosestPoint(p.transform.position)) < .04f);
        Check(shotBlood != null,"actual shot emits blood on target surface");
        UnityEngine.Object.Destroy(targetObject);
        // Render a deterministic burst in front of the player camera, independent of zombie movement.
        camera.cullingMask = 1; camera.farClipPlane = 3;
        Vector3 burstPoint = camera.transform.position+camera.transform.forward*1.2f;
        var burst = ElderboomImportedVfx.Blood(burstPoint,-camera.transform.forward,true);
        var ps = burst.GetComponent<ParticleSystem>(); ps.Simulate(.12f,true,true,false); ps.Pause();
        Check(ps.particleCount > 0 && burst.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported,"blood particles alive and HDRP shader supported");
        report.AppendLine(ElderboomAbilityShots.Capture(camera,"Logs/integration/blood_impact.png"));
        Check(!ShaderUtil.ShaderHasError(burst.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader),"blood shader compiles without errors");
        report.AppendLine("errors="+errors+" graphics="+SystemInfo.graphicsDeviceType);
        File.WriteAllText("Logs/integration/verification.txt",report.ToString());
        Debug.Log("ELDERBOOM_INTEGRATION_"+(errors == 0 ? "PASSED" : "FAILED"));
        int result = errors == 0 ? 0 : 1;
        EditorApplication.isPlaying=false;
        EditorApplication.delayCall += () => EditorApplication.Exit(result);
    }
}
