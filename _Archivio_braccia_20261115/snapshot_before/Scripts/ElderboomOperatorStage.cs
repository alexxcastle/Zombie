using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Full-screen 3D backdrop for the operator menu: a night corner of Elderboom village
// (Resources/Elderboom/Menu/OperatorSet.gltf, cut from the village block in Blender: eli_menu_set.blend).
// The operator's full-body model stands under the timber frame holding the AKMN (looping Rifle_Idle).
// Nodes authored in Blender: EliSpot / EliFacing (where the operator stands and faces), BarCenter,
// CamSpot / CamTarget (framing: operator on the right third, menu text on the left), Light_Lamp, Light_Fire.
// Night look: fixed exposure, gradient night sky, volumetric fog, moon + street lamp + campfire + key/rim.
public sealed class ElderboomOperatorStage : MonoBehaviour
{
    const string SetPath = "Elderboom/Menu/OperatorSet";
    static readonly Vector3 Origin = new Vector3(0f, -500f, 0f);

    // ---- Look tuning (HDRP physical units; exposure in EV100) ----
    // Same night as the village map: NightMoonBurst sky at 1400 lux, fixed exposure 7.2 (HDRPSliceBuilder).
    // Light values were balanced at EV 2.6 and scaled by 2^(7.2-2.6) so their ratios stay the same.
    const string SkyPath = "Elderboom/Menu/MenuSky";
    const float ExposureEV = 7.2f, SkyLux = 1400f, SkyRotation = 225f;   // MenuSky = the village sky pitched so the moon sits 17° up, turned into the shot
    const float LightScale = 24.25f;
    const float MoonLux = 3.5f * LightScale, LampLumen = 1700f * LightScale, FireLumen = 1100f * LightScale,
                KeyLumen = 1300f * LightScale, FillLumen = 700f * LightScale, RimLumen = 3200f * LightScale;
    const float CameraPullBack = 1.6f, CameraRaise = .25f, CameraTiltUp = .45f, CameraShiftRight = .28f; // framing on top of the Blender camera: more sky
    const float HeadHeight = 1.62f;       // head height above the ground standing (key light aims level with the face)
    const float TurnToCamera = 10f;       // extra yaw toward the camera so the face reads, degrees
    const float VerticalFov = 32.3f;      // Blender 35 mm lens on a 36 mm sensor at 16:9

    public Camera Camera { get; private set; }
    public ElderboomCharacterBody Body => body;

    Transform eliSpot, eliFacing, bar, camSpot, camTarget;
    ElderboomCharacterBody body;
    GameObject weapon;
    Light fire;
    float introStart;

    public static ElderboomOperatorStage Create()
    {
        var prefab = Resources.Load<GameObject>(SetPath);
        if (prefab == null) { Debug.LogWarning("[ElderboomOperatorStage] Missing Resources/" + SetPath); return null; }
        var root = new GameObject("OperatorStage");
        root.transform.position = Origin;
        var stage = root.AddComponent<ElderboomOperatorStage>();
        stage.Build(prefab);
        return stage;
    }

    void Build(GameObject prefab)
    {
        var set = Instantiate(prefab, transform, false);
        set.name = "OperatorSet";
        foreach (var c in set.GetComponentsInChildren<Collider>(true)) Destroy(c);
        eliSpot = Find(set.transform, "EliSpot");
        eliFacing = Find(set.transform, "EliFacing");
        bar = Find(set.transform, "BarCenter");
        var hangBar = Find(set.transform, "HangBar");
        if (hangBar != null) hangBar.gameObject.SetActive(false);   // standing pose: no bar under the frame
        camSpot = Find(set.transform, "CamSpot");
        camTarget = Find(set.transform, "CamTarget");
        var lampSpot = Find(set.transform, "Light_Lamp");
        var fireSpot = Find(set.transform, "Light_Fire");
        if (eliSpot == null || camSpot == null || camTarget == null) Debug.LogWarning("[ElderboomOperatorStage] Set nodes missing (EliSpot/CamSpot/CamTarget).");

        BuildCamera();
        BuildVolume();

        Vector3 eli = eliSpot != null ? eliSpot.position : transform.position;
        Vector3 cam = camSpot != null ? camSpot.position : eli + Vector3.forward * 6f;
        Vector3 toCam = cam - eli; toCam.y = 0f; toCam.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toCam);

        // Moon: high and behind the set, so roofs, trees and the operator get a cool outline.
        var moon = AddLight("Moon", LightType.Directional, Vector3.zero, new Color(.62f, .72f, 1f), MoonLux, "Lux", true);
        moon.transform.rotation = Quaternion.LookRotation((toCam * .8f - side * .45f + Vector3.down * .75f).normalized);
        // Street lamp behind the frame: warm side light and the shafts in the fog.
        if (lampSpot != null) AddLight("Lamp", LightType.Point, lampSpot.position, new Color(1f, .7f, .4f), LampLumen, "Lumen", true).range = 16f;
        // Campfire under the shelter: flickering orange on the back of the set.
        if (fireSpot != null)
        {
            fire = AddLight("Fire", LightType.Point, fireSpot.position + Vector3.up * .3f, new Color(1f, .42f, .12f), FireLumen, "Lumen", false);
            fire.range = 12f;
            AddEmbers(fireSpot.position + Vector3.up * .25f);
        }
        // Key on the face from camera-left, at head height: with the arms raised on the bar, any light from above
        // puts the forearms' shadow across the face, so the key comes in level with the head (≈2.05 m while hanging).
        Vector3 head = eli + Vector3.up * HeadHeight;
        var key = AddLight("Key", LightType.Spot, head + toCam * 2.6f + side * 1.5f + Vector3.down * .1f, new Color(.9f, .92f, 1f), KeyLumen, "Lumen", true);
        key.transform.rotation = Quaternion.LookRotation(head - key.transform.position);
        key.spotAngle = 40f; key.innerSpotAngle = 16f; key.range = 10f;
        // Soft front fill from the camera, no shadows: lifts the eye sockets and gives a catch-light.
        var fill = AddLight("FaceFill", LightType.Spot, cam + Vector3.up * .35f, new Color(.95f, .9f, .85f), FillLumen, "Lumen", false);
        fill.transform.rotation = Quaternion.LookRotation(head - fill.transform.position);
        fill.spotAngle = 22f; fill.innerSpotAngle = 6f; fill.range = 14f;
        var rim = AddLight("Rim", LightType.Spot, eli - toCam * 2.6f + side * 1.8f + Vector3.up * 3.4f, new Color(.55f, .7f, 1f), RimLumen, "Lumen", false);
        rim.transform.rotation = Quaternion.LookRotation(eli + Vector3.up * 1.6f - rim.transform.position);
        rim.spotAngle = 38f; rim.innerSpotAngle = 12f; rim.range = 12f;
        introStart = Time.unscaledTime;
    }

    void BuildCamera()
    {
        var go = new GameObject("StageCamera");
        go.transform.SetParent(transform, false);
        Camera = go.AddComponent<Camera>();
        Camera.fieldOfView = VerticalFov;
        Camera.nearClipPlane = .05f; Camera.farClipPlane = 250f;
        Camera.depth = 50f;
        var hd = go.AddComponent<HDAdditionalCameraData>();
        hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        PlaceCamera(0f);
    }

    void BuildVolume()
    {
        var go = new GameObject("StageVolume");
        go.transform.SetParent(transform, false);
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 50f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        float ground = eliSpot != null ? eliSpot.position.y : transform.position.y;

        var exposure = p.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(ExposureEV);

        var env = p.Add<VisualEnvironment>(true);
        var cube = Resources.Load<Cubemap>(SkyPath);
        if (cube != null)
        {
            env.skyType.Override((int)SkyType.HDRI);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var hdri = p.Add<HDRISky>(true);
            hdri.hdriSky.Override(cube);
            hdri.skyIntensityMode.Override(SkyIntensityMode.Lux);
            hdri.desiredLuxValue.Override(SkyLux);
            hdri.rotation.Override(SkyRotation);
        }
        else
        {
            Debug.LogWarning("[ElderboomOperatorStage] Missing Resources/" + SkyPath + ", using a gradient night sky.");
            env.skyType.Override((int)SkyType.Gradient);
            var sky = p.Add<GradientSky>(true);
            sky.top.Override(new Color(.010f, .016f, .040f));
            sky.middle.Override(new Color(.030f, .040f, .075f));
            sky.bottom.Override(new Color(.008f, .008f, .012f));
        }
        var tonemap = p.Add<Tonemapping>(true);
        tonemap.mode.Override(TonemappingMode.ACES);

        var fog = p.Add<Fog>(true);
        fog.enabled.Override(true);
        fog.meanFreePath.Override(38f);
        fog.baseHeight.Override(ground + 1.2f);
        fog.maximumHeight.Override(ground + 14f);
        fog.colorMode.Override(FogColorMode.SkyColor);
        fog.maxFogDistance.Override(90f);   // keep the sky (fogged at this distance) readable: clouds and moon
        fog.enableVolumetricFog.Override(true);
        fog.albedo.Override(new Color(.72f, .78f, .9f));

        var dof = p.Add<DepthOfField>(true);
        float focus = camSpot != null && eliSpot != null ? Vector3.Distance(camSpot.position, eliSpot.position) : 6f;
        dof.focusMode.Override(DepthOfFieldMode.Manual);
        dof.nearFocusStart.Override(0f); dof.nearFocusEnd.Override(.01f);
        dof.farFocusStart.Override(focus + 2.5f); dof.farFocusEnd.Override(focus + 34f);

        var bloom = p.Add<Bloom>(true);
        bloom.intensity.Override(.22f); bloom.scatter.Override(.72f);
        var vignette = p.Add<Vignette>(true);
        vignette.intensity.Override(.12f); vignette.smoothness.Override(.5f);
        volume.sharedProfile = p;
    }

    Light AddLight(string lightName, LightType type, Vector3 position, Color color, float intensity, string unit, bool shadows)
    {
        var go = new GameObject(lightName);
        go.transform.SetParent(transform, true);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        if (go.GetComponent<HDAdditionalLightData>() == null) go.AddComponent<HDAdditionalLightData>();
        // Light.lightUnit exists on recent Unity versions; set it by reflection so this compiles everywhere.
        var unitProperty = typeof(Light).GetProperty("lightUnit");
        if (unitProperty != null) { try { unitProperty.SetValue(light, Enum.Parse(unitProperty.PropertyType, unit)); } catch { } }
        light.intensity = intensity;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        var hd = go.GetComponent<HDAdditionalLightData>();
        if (hd != null) hd.volumetricDimmer = VolumetricDimmer(lightName);
        return light;
    }

    // How much each light lights the fog: the fire would otherwise fill the whole set with orange haze.
    static float VolumetricDimmer(string lightName) =>
        lightName == "Fire" ? .15f : lightName == "Lamp" ? .55f : lightName == "Moon" ? .8f : lightName == "Rim" ? .45f : .1f;

    void AddEmbers(Vector3 position)
    {
        var go = new GameObject("Embers");
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.up));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true; main.maxParticles = 120;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .9f);
        main.startSize = new ParticleSystem.MinMaxCurve(.006f, .016f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .55f, .15f), new Color(1f, .3f, .05f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.gravityModifier = -.03f;
        var emission = ps.emission; emission.rateOverTime = 6f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 18f; shape.radius = .25f;
        var noise = ps.noise; noise.enabled = true; noise.strength = .35f; noise.frequency = .6f; noise.scrollSpeed = .4f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, .35f, .08f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .1f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = EmberMaterial();
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
        ps.Play();
    }

    static Material emberMaterial;
    static Material EmberMaterial()
    {
        if (emberMaterial != null) return emberMaterial;
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float r = new Vector2((x + .5f) / (n * .5f) - 1f, (y + .5f) / (n * .5f) - 1f).magnitude;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
            px[y * n + x] = new Color(a, a, a, a);
        }
        tex.SetPixels(px); tex.Apply(false, true);
        var shader = Shader.Find("HDRP/Unlit");
        var m = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = "MenuEmbers" };
        m.SetColor("_UnlitColor", new Color(.7f * LightScale, .35f * LightScale, .14f * LightScale, 1f));   // HDR: glows through bloom at the stage exposure
        m.SetTexture("_UnlitColorMap", tex);
        m.SetFloat("_SurfaceType", 1f);
        m.SetFloat("_BlendMode", 1f);   // additive
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        HDMaterial.ValidateMaterial(m);
        m.renderQueue = 3000;
        return emberMaterial = m;
    }

    // ---- Operator ------------------------------------------------------------------------
    public bool ShowOperator(string op)
    {
        if (!ElderboomCharacterBody.Has(op)) { if (body != null) body.gameObject.SetActive(false); return false; }
        if (body != null && body.OperatorId == op && body.gameObject.activeSelf) return true; // already shown
        if (body != null && body.OperatorId != op) { Destroy(body.gameObject); body = null; weapon = null; }
        if (body == null)
        {
            body = ElderboomCharacterBody.Create(op, transform, true);
            if (body == null) return false;
        }
        body.gameObject.SetActive(true);
        Vector3 spot = eliSpot != null ? eliSpot.position : transform.position;
        Vector3 face = eliFacing != null ? eliFacing.position - spot : Vector3.back; face.y = 0f;
        if (face.sqrMagnitude < .001f) face = Vector3.back;
        // Turn a little further toward the camera (sign chosen so the yaw closes the angle to it).
        Vector3 toCam = (camSpot != null ? camSpot.position : spot + face) - spot; toCam.y = 0f;
        float sign = Mathf.Sign(Vector3.SignedAngle(face, toCam, Vector3.up));
        face = Quaternion.Euler(0f, sign * TurnToCamera, 0f) * face.normalized;
        body.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(face));
        if (weapon == null && body.HasClip("Rifle_Idle")) weapon = body.AttachProp("AKMN");
        body.Play(body.HasClip("Rifle_Idle") ? "Rifle_Idle" : "Idle_8", 0f, .85f, true);
        introStart = Time.unscaledTime;
        return true;
    }

    // The operator no longer hangs from the bar: nothing to play before the match loads.
    public float Drop() => 0f;

    void LateUpdate()
    {
        PlaceCamera(Time.unscaledTime - introStart);
        if (fire != null)
        {
            float t = Time.unscaledTime;
            fire.intensity = FireLumen * (.82f + .12f * Mathf.PerlinNoise(t * 7f, 0f) + .08f * Mathf.Sin(t * 23f));
        }
    }

    // Slow handheld drift plus a short push-in whenever an operator is shown.
    void PlaceCamera(float t)
    {
        if (Camera == null || camSpot == null || camTarget == null) return;
        Vector3 basePos = camSpot.position, target = camTarget.position;
        Vector3 fwd = (target - basePos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float intro = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 2.6f));
        float time = Time.unscaledTime;
        Vector3 drift = right * (Mathf.Sin(time * .11f) * .16f) + Vector3.up * (Mathf.Sin(time * .17f + 1.3f) * .045f);
        // Pulled back so the operator clears the footer buttons; aimed right so he moves toward the screen centre-right.
        basePos += Vector3.up * CameraRaise - fwd * CameraPullBack;
        target += Vector3.up * CameraTiltUp + right * CameraShiftRight;
        Camera.transform.position = basePos - fwd * (.7f * intro) + drift;
        Camera.transform.rotation = Quaternion.LookRotation(target + right * (Mathf.Sin(time * .07f) * .08f) - Camera.transform.position, Vector3.up);
    }

    public void SetVisible(bool on) => gameObject.SetActive(on);

    // Tuning hook (editor captures): turn the night sky around the vertical axis.
    public void SetSkyRotation(float degrees)
    {
        var volume = GetComponentInChildren<Volume>();
        if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out HDRISky hdri)) hdri.rotation.Override(degrees);
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name || t.name.StartsWith(name + ".") || t.name.StartsWith(name + "_")) return t;
        foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
        return null;
    }
}
