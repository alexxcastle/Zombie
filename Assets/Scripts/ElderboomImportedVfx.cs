using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.VFX;

public sealed class ElderboomImportedVfx : ScriptableObject
{
    public GameObject shockwave;
    public GameObject fireball;
    public GameObject bloodHit;
    public GameObject bloodDeath;
    public GameObject keepAway;
    public Texture2D bloodSpray;
    public Texture2D plasmaFlipbook;

    static ElderboomImportedVfx catalog;

    public static ElderboomImportedVfx Catalog => catalog != null ? catalog : catalog = Resources.Load<ElderboomImportedVfx>("Elderboom/ImportedVfx");

    public static GameObject Shockwave(Vector3 position)
    {
        var instance = Spawn(Catalog != null ? Catalog.shockwave : null, position, Quaternion.identity, 1.6f);
        if (instance == null) return null;
        instance.transform.localScale = Vector3.one * 12f;
        var streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV
        };
        foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(streams);
            renderer.material = ShockwaveMaterial();
        }
        return instance;
    }

    public static GameObject Fireball(Vector3 position, Vector3 forward)
    {
        var instance = new GameObject("PlasmaFireball");
        instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward));
        var particles = instance.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = 1.2f;
        main.startSpeed = 0f;
        main.startSize = 0.85f;
        main.maxParticles = 4;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var sheet = particles.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = 8;
        sheet.numTilesY = 8;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.cycleCount = 1;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        var renderer = instance.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.enableGPUInstancing = false;
        renderer.sharedMaterial = HdrpParticle(null, new Color(1f, 0.72f, 0.35f, 1f), true, Catalog != null ? Catalog.plasmaFlipbook : null, 0.22f);
        return instance;
    }

    public static void Blood(Vector3 position, bool death)
    {
        Blood(position, Vector3.up, death);
    }

    static Material bloodMaterial;
    // Pooled: each hit reuses one of a fixed set of two-layer emitters instead of
    // building a new GameObject with two particle systems per impact.
    const int BloodPoolSize = 24;
    static readonly List<ParticleSystem> bloodPool = new List<ParticleSystem>();
    static int nextBlood;
    public static GameObject Blood(Vector3 position, Vector3 normal, bool death)
    {
        if (normal.sqrMagnitude < .001f) normal = Vector3.up;
        normal.Normalize();
        bloodPool.RemoveAll(p => p == null);
        ParticleSystem particles = null;
        foreach (var candidate in bloodPool) if (!candidate.IsAlive(true)) { particles = candidate; break; }
        if (particles == null)
        {
            if (bloodPool.Count < BloodPoolSize) { particles = BuildBlood(); bloodPool.Add(particles); }
            else { particles = bloodPool[nextBlood % bloodPool.Count]; nextBlood++; }
        }
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.transform.SetPositionAndRotation(position + normal * .025f, Quaternion.LookRotation(normal));
        var main = particles.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, death ? 4f : 3.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(.13f, death ? .46f : .34f);
        particles.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(death ? 72 : 48)) });
        var mist = particles.transform.GetChild(0).GetComponent<ParticleSystem>();
        mist.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(death ? 18 : 12)) });
        particles.gameObject.SetActive(true);
        particles.Play(true);
        return particles.gameObject;
    }

    static ParticleSystem BuildBlood()
    {
        var instance = new GameObject("BloodImpact");
        Object.DontDestroyOnLoad(instance);
        var particles = instance.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false; main.playOnAwake = false; main.duration = .6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.42f, .9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(.50f, .012f, .019f, .98f);
        main.gravityModifier = .65f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        var shape = particles.shape;
        shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 26f; shape.radius = .035f;
        var fade = particles.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .45f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
        if (bloodMaterial == null)
        {
            var shader = Resources.Load<Shader>("Elderboom/Shaders/BloodParticles");
            bloodMaterial = new Material(shader) { name = "BloodImpactShared" };
            if (Catalog != null && Catalog.bloodSpray != null) bloodMaterial.mainTexture = Catalog.bloodSpray;
        }
        renderer.sharedMaterial = bloodMaterial;
        // A compact mist core gives the fine spray silhouette enough density at
        // gameplay distances. Both layers remain depth-tested at the hit surface.
        var mistObject = new GameObject("BloodMist"); mistObject.transform.SetParent(instance.transform, false);
        var mist = mistObject.AddComponent<ParticleSystem>(); mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var mm = mist.main; mm.loop = false; mm.playOnAwake = false; mm.duration = .4f;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(.32f, .6f); mm.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .65f);
        mm.startSize = new ParticleSystem.MinMaxCurve(.26f, .44f); mm.startColor = new Color(.42f, .009f, .015f, .72f);
        mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.maxParticles = 20;
        var me = mist.emission; me.rateOverTime = 0;
        var ms = mist.shape; ms.shapeType = ParticleSystemShapeType.Cone; ms.angle = 30; ms.radius = .025f;
        var mf = mist.colorOverLifetime; mf.enabled = true; mf.color = gradient;
        var mr = mist.GetComponent<ParticleSystemRenderer>(); mr.sharedMaterial = ElderboomCombatVfx.ParticleMaterial;
        mr.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
        return particles;
    }

    public static GameObject KeepAway(Transform follow)
    {
        var instance = Spawn(Catalog != null ? Catalog.keepAway : null, follow.position, Quaternion.identity, 0f);
        if (instance == null) return null;
        instance.transform.SetParent(follow, false);
        instance.transform.localPosition = Vector3.zero;
        instance.SetActive(true);
        var visual = instance.GetComponent<VisualEffect>();
        if (visual != null)
        {
            if (visual.HasFloat("Radius")) visual.SetFloat("Radius", 3f);
            visual.Play();
        }
        return instance;
    }

    static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float life)
    {
        if (prefab == null) return null;
        var instance = Instantiate(prefab, position, rotation);
        if (life > 0f) Destroy(instance, life);
        return instance;
    }

    static void AdaptParticles(GameObject root, Color tint, bool additive, Texture forceTexture = null, float emissiveScale = 1.4f)
    {
        var streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV
        };
        foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh == null)
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.enabled = true;
            renderer.enableGPUInstancing = false;
            renderer.SetActiveVertexStreams(streams);
            var source = renderer.sharedMaterial;
            if (source != null && source.shader != null && Supported(source.shader)) continue;
            renderer.sharedMaterial = HdrpParticle(source, tint, additive, forceTexture, emissiveScale);
        }
    }

    static bool Supported(Shader shader)
    {
        string name = shader.name;
        return name.StartsWith("HDRP/") || name.StartsWith("Hidden/HDRP") || name.StartsWith("Shader Graphs/") || name.Contains("VFX");
    }

    static Material shockwaveMaterial;
    static Texture2D shockwaveRing;
    static Texture2D shockwaveDistortion;

    static Material ShockwaveMaterial()
    {
        if (shockwaveMaterial != null) return shockwaveMaterial;
        var shader = Shader.Find("HDRP/Unlit");
        var material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        var tint = new Color(0.45f, 0.9f, 1f, 0.9f);
        material.SetColor("_UnlitColor", tint);
        material.SetColor("_EmissiveColor", tint);
        material.SetFloat("_EmissiveExposureWeight", 0f);
        material.SetFloat("_UseEmissiveIntensity", 0f);
        material.SetTexture("_UnlitColorMap", RingTexture());
        material.SetTexture("_EmissiveColorMap", RingTexture());
        material.SetFloat("_SurfaceType", 1f);
        material.SetFloat("_BlendMode", 1f);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_DistortionEnable", 1f);
        material.SetFloat("_DistortionOnly", 0f);
        material.SetFloat("_DistortionBlendMode", 0f);
        material.SetFloat("_DistortionScale", 1.5f);
        material.SetFloat("_DistortionBlurScale", 0f);
        material.SetTexture("_DistortionVectorMap", DistortionTexture());
        material.SetOverrideTag("RenderType", "Transparent");
        HDMaterial.ValidateMaterial(material);
        material.SetShaderPassEnabled("DistortionVectors", true);
        material.renderQueue = 3000;
        shockwaveMaterial = material;
        return material;
    }

    static Texture2D RingTexture()
    {
        if (shockwaveRing != null) return shockwaveRing;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.72f) / 0.055f, 2f));
                pixels[y * n + x] = new Color(ring, ring, ring, ring);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        shockwaveRing = tex;
        return tex;
    }

    static Texture2D DistortionTexture()
    {
        if (shockwaveDistortion != null) return shockwaveDistortion;
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
        var pixels = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.72f) / 0.07f, 2f));
                float nx = d > 0.001f ? u / d : 0f;
                float ny = d > 0.001f ? v / d : 0f;
                float push = ring * 0.85f;
                pixels[y * n + x] = new Color(0.5f + nx * push * 0.5f, 0.5f + ny * push * 0.5f, 0f, 1f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        shockwaveDistortion = tex;
        return tex;
    }

    // One material per distinct look, shared by every effect instance. Previously each
    // fireball/impact created a new Material that was never destroyed.
    static readonly System.Collections.Generic.Dictionary<(Material, Color, bool, Texture, float), Material> particleMaterials =
        new System.Collections.Generic.Dictionary<(Material, Color, bool, Texture, float), Material>();

    static Material HdrpParticle(Material source, Color tint, bool additive, Texture forceTexture = null, float emissiveScale = 4f)
    {
        var key = (source, tint, additive, forceTexture, emissiveScale);
        if (particleMaterials.TryGetValue(key, out var cached) && cached != null) return cached;
        var created = CreateHdrpParticle(source, tint, additive, forceTexture, emissiveScale);
        particleMaterials[key] = created;
        return created;
    }

    static Material CreateHdrpParticle(Material source, Color tint, bool additive, Texture forceTexture, float emissiveScale)
    {
        var shader = Shader.Find("HDRP/Unlit");
        var material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        Texture texture = forceTexture;
        if (texture == null && source != null)
        {
            if (source.HasProperty("_MainTexture")) texture = source.GetTexture("_MainTexture");
            if (texture == null && source.HasProperty("_ErosionTexture")) texture = source.GetTexture("_ErosionTexture");
            if (texture == null && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
            if (texture == null && source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
            if (texture == null && source.HasProperty("_UnlitColorMap")) texture = source.GetTexture("_UnlitColorMap");
        }
        material.SetColor("_UnlitColor", tint);
        material.SetColor("_EmissiveColor", tint * emissiveScale);
        material.SetFloat("_EmissiveExposureWeight", 0f);
        material.SetFloat("_UseEmissiveIntensity", 0f);
        if (texture != null)
        {
            material.SetTexture("_UnlitColorMap", texture);
            material.SetTexture("_EmissiveColorMap", texture);
            material.SetTexture("_MainTex", texture);
        }
        material.SetFloat("_SurfaceType", 1f);
        material.SetFloat("_BlendMode", additive ? 1f : 0f);
        material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        HDMaterial.ValidateMaterial(material);
        material.renderQueue = 3000;
        return material;
    }
}
