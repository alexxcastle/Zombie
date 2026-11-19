using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Video;

// 3D preview for the menu's weapon customization page (ElderboomMenu.Loadout): the weapon prefab with the loadout
// mounted, on a dark studio set far below the menu's other sets, with a looping video backdrop. Muzzle to the right so the right-side rail
// (light, charm) faces the camera. Drag to turn it.
public sealed class ElderboomLoadoutStage : MonoBehaviour
{
    static readonly Vector3 Origin = new Vector3(0f, -1500f, 0f);
    const float ExposureEV = 7f, VerticalFov = 26f;
    float Distance = 2.35f;
    const float KeyLumen = 14000f, FillLumen = 5000f, RimLumen = 16000f, TopLumen = 6000f;
    static readonly Color Backdrop = new Color(.045f, .058f, .066f);

    public Camera Camera { get; private set; }
    public Dictionary<string, Transform> Sockets { get; private set; } = new Dictionary<string, Transform>();
    public string WeaponId { get; private set; }

    Transform pivot, weapon;
    float yaw = -14f, yawTarget = -14f, pitch = 4f;
    // Where the weapon sits on screen (viewport): a bit right of centre, like the reference.
    const float ViewX = .56f, ViewY = .5f;

    // Animated backdrop behind the weapon: a seamless looping video (StreamingAssets/elderboom_loadout_loop.mp4,
    // edited as an eased forward/reverse loop, no audio) on an unlit quad that fills the camera view far behind the
    // weapon. Without the file the plain studio colour stays. BackdropGain < 1 darkens it for UI legibility.
    const string BackdropVideoFile = "elderboom_loadout_loop.mp4";
    const float BackdropDepth = 15f, BackdropGain = 1f, VideoAspect = 16f / 9f;
    VideoPlayer backdropVideo;
    RenderTexture backdropTexture;
    Material backdropMaterial;
    Transform backdropQuad;

    public static ElderboomLoadoutStage Create()
    {
        var go = new GameObject("LoadoutStage");
        go.transform.position = Origin;
        var stage = go.AddComponent<ElderboomLoadoutStage>();
        stage.Build();
        return stage;
    }

    void Build()
    {
        var camGo = new GameObject("LoadoutCamera");
        camGo.transform.SetParent(transform, false);
        Camera = camGo.AddComponent<Camera>();
        Camera.fieldOfView = VerticalFov;
        Camera.nearClipPlane = .05f; Camera.farClipPlane = 25f;
        Camera.depth = 60f;
        var hd = camGo.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        // The clear colour is exposed like the scene: scale it back up by the fixed exposure.
        float exposed = 1.2f * Mathf.Pow(2f, ExposureEV);
        hd.backgroundColorHDR = Backdrop * exposed;
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camGo.transform.localPosition = new Vector3(0f, 0f, -Distance);
        camGo.transform.localRotation = Quaternion.identity;

        BuildVideoBackdrop(exposed);

        pivot = new GameObject("WeaponPivot").transform;
        pivot.SetParent(transform, false);

        BuildVolume();
        var key = AddLight("Key", new Vector3(-1.4f, 1.2f, -1.6f), KeyLumen, new Color(1f, .96f, .9f), true);
        var fill = AddLight("Fill", new Vector3(1.6f, -.2f, -1.8f), FillLumen, new Color(.8f, .88f, 1f), false);
        var rim = AddLight("Rim", new Vector3(.6f, 1.1f, 1.6f), RimLumen, new Color(.75f, .85f, 1f), false);
        var top = AddLight("Top", new Vector3(0f, 2f, -.3f), TopLumen, new Color(1f, .82f, .62f), false);
        foreach (var l in new[] { key, fill, rim, top }) l.transform.LookAt(transform.position);
    }

    void BuildVideoBackdrop(float exposed)
    {
        string path = Path.Combine(Application.streamingAssetsPath, BackdropVideoFile);
        if (!path.Contains("://") && !File.Exists(path)) return;
        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null) return;
        backdropTexture = new RenderTexture(1920, 1080, 0) { name = "LoadoutBackdropVideo" };
        backdropMaterial = new Material(shader) { name = "LoadoutBackdrop" };
        // Video through the emissive channel with exposure weight 0, so the stage's fixed exposure doesn't touch it
        // (same setup as ElderboomImportedVfx). The unlit colour stays black.
        backdropMaterial.SetColor("_UnlitColor", Color.black);
        backdropMaterial.SetColor("_EmissiveColor", Color.white * BackdropGain);
        backdropMaterial.SetTexture("_EmissiveColorMap", backdropTexture);
        backdropMaterial.SetFloat("_EmissiveExposureWeight", 0f);
        backdropMaterial.SetFloat("_UseEmissiveIntensity", 0f);
        HDMaterial.ValidateMaterial(backdropMaterial);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "LoadoutBackdrop";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(Camera.transform, false);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = backdropMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.enabled = false; // until the video is prepared (the studio colour shows meanwhile)
        backdropQuad = quad.transform;
        FitBackdrop();

        backdropVideo = gameObject.AddComponent<VideoPlayer>();
        backdropVideo.playOnAwake = false; backdropVideo.url = path; backdropVideo.isLooping = true; backdropVideo.skipOnDrop = true;
        backdropVideo.audioOutputMode = VideoAudioOutputMode.None;
        backdropVideo.renderMode = VideoRenderMode.RenderTexture; backdropVideo.targetTexture = backdropTexture;
        // Shown once the video is prepared (no frameReady events: they read every frame back to the CPU and stall HDRP).
        backdropVideo.prepareCompleted += v => { r.enabled = true; if (isActiveAndEnabled) v.Play(); };
        backdropVideo.errorReceived += (v, e) => { r.enabled = false; Debug.LogWarning("Loadout backdrop video: " + e); };
        backdropVideo.Prepare();
    }

    // Cover the camera view at BackdropDepth without stretching the 16:9 video.
    void FitBackdrop()
    {
        if (backdropQuad == null || Camera == null) return;
        float h = 2f * Mathf.Tan(Camera.fieldOfView * .5f * Mathf.Deg2Rad) * BackdropDepth;
        float w = h * (Camera.aspect > 0f ? Camera.aspect : VideoAspect);
        if (w / h > VideoAspect) h = w / VideoAspect; else w = h * VideoAspect;
        backdropQuad.localPosition = new Vector3(0f, 0f, BackdropDepth);
        backdropQuad.localRotation = Quaternion.identity;
        backdropQuad.localScale = new Vector3(w * 1.01f, h * 1.01f, 1f);
    }

    // The page hides the whole set (SetActive), which stops the player: resume when it comes back.
    void OnEnable()
    {
        if (backdropVideo == null) return;
        if (backdropVideo.isPrepared) backdropVideo.Play(); else backdropVideo.Prepare();
    }

    void OnDestroy()
    {
        if (backdropTexture != null) { backdropTexture.Release(); Destroy(backdropTexture); }
        if (backdropMaterial != null) Destroy(backdropMaterial);
    }

    void BuildVolume()
    {
        var go = new GameObject("LoadoutVolume");
        go.transform.SetParent(transform, false);
        // Local volume around the set only: the menu's other sets keep their own look.
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = false; volume.priority = 60f; volume.blendDistance = 0f;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true; box.size = Vector3.one * 30f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        var exposure = p.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(ExposureEV);
        var env = p.Add<VisualEnvironment>(true);
        env.skyType.Override((int)SkyType.Gradient);
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = p.Add<GradientSky>(true);
        sky.top.Override(new Color(.32f, .38f, .45f));
        sky.middle.Override(new Color(.16f, .18f, .2f));
        sky.bottom.Override(new Color(.05f, .05f, .055f));
        sky.multiplier.Override(.35f);
        var fog = p.Add<Fog>(true);
        fog.enabled.Override(false);
        var tonemap = p.Add<Tonemapping>(true);
        tonemap.mode.Override(TonemappingMode.ACES);
        var bloom = p.Add<Bloom>(true);
        bloom.intensity.Override(.12f);
        var vignette = p.Add<Vignette>(true);
        vignette.intensity.Override(.32f);
        var dof = p.Add<DepthOfField>(true);
        dof.focusMode.Override(DepthOfFieldMode.Off);
        volume.sharedProfile = p;
    }

    Light AddLight(string lightName, Vector3 local, float lumen, Color color, bool shadows)
    {
        var go = new GameObject(lightName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        go.AddComponent<HDAdditionalLightData>();
        var unit = typeof(Light).GetProperty("lightUnit");
        if (unit != null) { try { unit.SetValue(light, Enum.Parse(unit.PropertyType, "Lumen")); } catch { } }
        light.intensity = lumen;
        light.range = 8f;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        var hd = go.GetComponent<HDAdditionalLightData>();
        if (hd != null) hd.volumetricDimmer = 0f;
        return light;
    }

    public void SetVisible(bool on)
    {
        if (gameObject.activeSelf != on) gameObject.SetActive(on);
    }

    // Builds (or rebuilds) the weapon with this loadout.
    public void Show(string weaponId, Dictionary<string, string> loadout)
    {
        var def = ElderboomWeapons.Find(weaponId);
        if (weaponId != WeaponId || weapon == null)
        {
            if (weapon != null) ElderboomWeaponMods.Kill(weapon.gameObject);
            weapon = null;
            WeaponId = weaponId;
            var prefab = def != null && !string.IsNullOrEmpty(def.resource) ? Resources.Load<GameObject>(def.resource) : null;
            if (prefab == null) { Sockets = new Dictionary<string, Transform>(); return; }
            weapon = Instantiate(prefab, pivot, false).transform;
            weapon.name = "Preview_" + weaponId;
            foreach (var c in weapon.GetComponentsInChildren<Collider>(true)) ElderboomWeaponMods.Kill(c);
            foreach (var l in weapon.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var t in weapon.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true)) if (ElderboomWeaponMods.IsArmPart(r.name)) r.enabled = false;
            foreach (var mb in weapon.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is ElderboomCharmSway)) mb.enabled = false;
            weapon.gameObject.SetActive(true);
        }
        Sockets = ElderboomWeaponMods.Apply(weapon, weaponId, loadout, false);
        foreach (var t in weapon.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        Frame();
    }

    // Weapon muzzle along +X (screen right), centred on the pivot.
    void Frame()
    {
        if (weapon == null) return;
        weapon.localPosition = Vector3.zero;
        weapon.localRotation = Quaternion.Euler(0f, 90f, 0f);
        float t = Mathf.Tan(VerticalFov * .5f * Mathf.Deg2Rad);
        float aspect = Camera != null && Camera.aspect > 0f ? Camera.aspect : 16f / 9f;
        if (ElderboomWeaponMods.MeshBounds(weapon, pivot, out var b, c => c.gameObject.activeInHierarchy && c.GetComponent<Renderer>() is Renderer r && r.enabled && !r.forceRenderingOff))
        {
            weapon.localPosition = -b.center;
            // Distance so the weapon's length fills about half the screen (pistols come closer than rifles).
            float length = Mathf.Max(b.size.x, .12f);
            Distance = Mathf.Clamp(length / (.5f * 2f * t * aspect), .45f, 3f);
            Camera.transform.localPosition = new Vector3(0f, 0f, -Distance);
            Camera.nearClipPlane = Mathf.Max(.01f, Distance * .1f);
        }
        // Place the pivot so the weapon's centre lands at (ViewX, ViewY) of the screen.
        pivot.localPosition = new Vector3((ViewX - .5f) * 2f * t * aspect * Distance, (ViewY - .5f) * 2f * t * Distance, 0f);
    }

    public void Drag(float dx) { yawTarget = Mathf.Clamp(yawTarget + dx * .35f, -150f, 150f); }
    public void ResetView() { yawTarget = -14f; }
    public void SetYaw(float value) { yaw = yawTarget = value; if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, yaw, 0f); }

    void LateUpdate()
    {
        yaw = Mathf.Lerp(yaw, yawTarget, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        if (pivot != null) pivot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        FitBackdrop();
    }

    // A world point on the 1920x1080 menu panel (it scales with the screen height).
    public bool ToPanel(Vector3 world, out Vector2 panel)
    {
        panel = default;
        if (Camera == null) return false;
        var s = Camera.WorldToScreenPoint(world);
        if (s.z <= 0f) return false;
        float scale = 1080f / Mathf.Max(1, Screen.height);
        panel = new Vector2(s.x * scale, (Screen.height - s.y) * scale);
        return true;
    }

    public float PanelWidth => Screen.width * (1080f / Mathf.Max(1, Screen.height));
}
