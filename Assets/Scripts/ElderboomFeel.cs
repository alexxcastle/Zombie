using System.Collections.Generic;
using UnityEngine;

// Phase 1 game feel: view recoil and trauma shake, surface impacts with bullet
// holes and procedural impact audio, tracers, muzzle flash and damage-feedback
// textures. Everything is pooled and built at runtime from FeelParticles.shader.
public enum ElderboomSurface { Stone, Wood, Metal, Dirt }

public sealed class ElderboomViewKick
{
    public float Pitch, Yaw, Roll;
    public Vector3 Shake;
    float trauma;
    float seed = -1f; // Random may not be used from MonoBehaviour field initializers
    public float Trauma => trauma;
    // up > 0 lifts the view; side is degrees of yaw.
    public void Kick(float up, float side) { Pitch += up; Yaw += side; Roll += side * .6f; }
    public void AddTrauma(float amount) { trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, amount)); }
    public void Advance(float dt)
    {
        if (seed < 0f) seed = Random.value * 1000f;
        float k = 1f - Mathf.Exp(-12f * dt);
        Pitch = Mathf.Lerp(Pitch, 0f, k); Yaw = Mathf.Lerp(Yaw, 0f, k);
        Roll = Mathf.Lerp(Roll, 0f, 1f - Mathf.Exp(-16f * dt));
        trauma = Mathf.Max(0f, trauma - 1.5f * dt);
        float s = trauma * trauma, t = Time.time * 22f;
        Shake = new Vector3((Mathf.PerlinNoise(seed, t) - .5f) * 8f * s,
                            (Mathf.PerlinNoise(seed + 17f, t) - .5f) * 6f * s,
                            (Mathf.PerlinNoise(seed + 41f, t) - .5f) * 10f * s);
    }
    public void Reset() { Pitch = Yaw = Roll = trauma = 0f; Shake = Vector3.zero; }
}

public static class ElderboomFeel
{
    // ---- Weapon recoil profiles -------------------------------------------------
    // up/side in degrees per shot, trauma 0..1, climb = fraction of the kick that stays in the aim.
    public static void Recoil(ElderboomWeaponDefinition d, out float up, out float side, out float trauma, out float climb)
    {
        string id = d != null && d.id != null ? d.id : "";
        string category = d != null && d.category != null ? d.category : "assault_rifle";
        climb = .35f;
        switch (d != null ? d.FireMode : ElderboomFireMode.Hitscan)
        {
            case ElderboomFireMode.Melee: up = .5f; side = .9f; trauma = .06f; climb = 0f; return;
            case ElderboomFireMode.Flame: up = .08f; side = .06f; trauma = .025f; climb = 0f; return;
            case ElderboomFireMode.Explosive: up = 2.4f; side = .4f; trauma = .2f; climb = .25f; return;
            case ElderboomFireMode.Pulse: up = 1.2f; side = .2f; trauma = .22f; climb = 0f; return;
        }
        if (id == "minigun") { up = .22f; side = .22f; trauma = .07f; climb = .2f; return; }
        if (category == ElderboomIds.Category.Shotgun) { up = 3.3f; side = .7f; trauma = .24f; climb = .3f; return; }
        if (category == ElderboomIds.Category.Handgun)
        {
            bool heavy = id == "rhino" || id == "revolver";
            up = heavy ? 2.6f : 1.5f; side = heavy ? .5f : .3f; trauma = heavy ? .14f : .08f; return;
        }
        up = .75f; side = .28f; trauma = .045f;
    }

    // ---- Host / pooling -----------------------------------------------------------
    sealed class TracerLine { public LineRenderer line; public Vector3 from, dir; public float length, travelled, speed; public bool active; }
    sealed class Host : MonoBehaviour
    {
        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var t in tracers)
            {
                if (!t.active) continue;
                t.travelled += t.speed * dt;
                float head = Mathf.Min(t.travelled, t.length), tail = Mathf.Max(0f, t.travelled - 3.2f);
                if (tail >= t.length) { t.active = false; t.line.enabled = false; continue; }
                t.line.SetPosition(0, t.from + t.dir * tail);
                t.line.SetPosition(1, t.from + t.dir * head);
            }
        }
    }
    static Host host;
    static ParticleSystem dust, chips, sparks, flash;
    static Material dustMat, sparkMat, flashMat, holeMat, tracerMat;
    static Texture2D soft, hole, vignette, wedge;
    static readonly List<TracerLine> tracers = new List<TracerLine>();
    static readonly List<MeshRenderer> holes = new List<MeshRenderer>();
    static int nextHole, nextTracer, tracerCounter;
    static Mesh quad;
    static MaterialPropertyBlock block;
    static float lastImpactSound;
    static readonly Dictionary<ElderboomSurface, AudioClip> impactClips = new Dictionary<ElderboomSurface, AudioClip>();
    static AudioClip heartbeat;
    const int MaxHoles = 96, MaxTracers = 16;

    static void Ensure()
    {
        if (host != null) return;
        host = new GameObject("ElderboomFeel").AddComponent<Host>();
        holes.Clear(); tracers.Clear(); nextHole = nextTracer = 0;
        var shader = Resources.Load<Shader>("Elderboom/Shaders/FeelParticles");
        dustMat = Make(shader, SoftTexture(), false, 1f);
        sparkMat = Make(shader, SoftTexture(), true, 5f);
        flashMat = Make(shader, SoftTexture(), true, 7f);
        holeMat = Make(shader, HoleTexture(), false, 1f);
        tracerMat = Make(shader, Texture2D.whiteTexture, true, 4f);
        dust = System("ImpactDust", dustMat, ParticleSystemRenderMode.Billboard, -.04f, 64);
        chips = System("ImpactChips", dustMat, ParticleSystemRenderMode.Billboard, 1.1f, 96);
        sparks = System("ImpactSparks", sparkMat, ParticleSystemRenderMode.Stretch, 1.2f, 128);
        flash = System("MuzzleFlash", flashMat, ParticleSystemRenderMode.Billboard, 0f, 16);
        var grow = dust.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .6f, 1f, 1.8f));
        foreach (var ps in new[] { chips, sparks, flash })
        { var shrink = ps.sizeOverLifetime; shrink.enabled = true; shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .1f)); }
        var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>(); sparkRenderer.velocityScale = .025f; sparkRenderer.lengthScale = 1f;
    }

    static Material Make(Shader shader, Texture texture, bool additive, float intensity)
    {
        var m = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { mainTexture = texture };
        m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", additive ? 1f : 10f); m.SetFloat("_Intensity", intensity);
        m.renderQueue = 3000; return m;
    }

    static ParticleSystem System(string name, Material material, ParticleSystemRenderMode mode, float gravity, int max)
    {
        var go = new GameObject(name); go.transform.SetParent(host.transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity; main.maxParticles = max; main.startSpeed = 0f;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.7f, .4f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material; renderer.renderMode = mode;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static Vector3 Cone(Vector3 axis, float degrees)
    {
        Vector2 d = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
        return (Quaternion.LookRotation(axis.sqrMagnitude > .0001f ? axis : Vector3.up) * new Vector3(d.x, d.y, 1f)).normalized;
    }

    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
    {
        var p = new ParticleSystem.EmitParams { position = position, velocity = velocity, startSize = size, startLifetime = life, startColor = color, rotation = Random.Range(0f, 360f), applyShapeToPosition = false };
        ps.Emit(p, 1);
    }

    // ---- Surfaces and impacts -------------------------------------------------------
    public static ElderboomSurface Classify(RaycastHit hit)
    {
        if (hit.collider is TerrainCollider) return ElderboomSurface.Dirt;
        string n = hit.collider.name.ToLowerInvariant();
        var renderer = hit.collider.GetComponent<Renderer>();
        if (renderer == null) renderer = hit.collider.GetComponentInParent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null) n += " " + renderer.sharedMaterial.name.ToLowerInvariant();
        if (Has(n, "metal", "iron", "steel", "lamp", "pole", "bell", "cauldron", "generator", "pipe")) return ElderboomSurface.Metal;
        if (Has(n, "wood", "plank", "timber", "bench", "door", "roof", "fence", "barn", "log", "crate", "beam", "shingle", "gate", "cart")) return ElderboomSurface.Wood;
        if (Has(n, "grass", "dirt", "ground", "soil", "mud", "terrain", "field", "hay")) return ElderboomSurface.Dirt;
        return ElderboomSurface.Stone;
    }
    static bool Has(string s, params string[] terms) { foreach (var t in terms) if (s.Contains(t)) return true; return false; }

    static Color DustColor(ElderboomSurface s) => s == ElderboomSurface.Wood ? new Color(.42f, .31f, .19f, .55f)
        : s == ElderboomSurface.Dirt ? new Color(.30f, .24f, .17f, .65f) : s == ElderboomSurface.Metal ? new Color(.5f, .5f, .5f, .25f) : new Color(.55f, .53f, .5f, .55f);
    static Color ChipColor(ElderboomSurface s) => s == ElderboomSurface.Wood ? new Color(.55f, .40f, .24f, 1f)
        : s == ElderboomSurface.Dirt ? new Color(.22f, .17f, .11f, 1f) : new Color(.38f, .37f, .35f, 1f);

    // World (non-actor) hit: dust, debris, sparks, bullet hole and a surface sound.
    public static void Impact(RaycastHit hit, bool light = false)
    {
        if (hit.collider == null) return;
        Ensure();
        var surface = Classify(hit);
        Vector3 n = hit.normal.sqrMagnitude > .001f ? hit.normal.normalized : Vector3.up;
        Vector3 p = hit.point + n * .02f;
        int dustCount = light ? 2 : surface == ElderboomSurface.Dirt ? 6 : 4;
        for (int i = 0; i < dustCount; i++)
            Emit(dust, p, Cone(n, 35f) * Random.Range(.3f, 1.4f), Random.Range(.08f, .2f), Random.Range(.45f, .9f), DustColor(surface));
        if (!light && surface != ElderboomSurface.Metal)
            for (int i = 0; i < 6; i++)
                Emit(chips, p, Cone(n, 50f) * Random.Range(1.5f, 4.5f), Random.Range(.012f, .03f), Random.Range(.35f, .7f), ChipColor(surface));
        int sparkCount = surface == ElderboomSurface.Metal ? 10 : surface == ElderboomSurface.Stone && !light ? 3 : 0;
        for (int i = 0; i < sparkCount; i++)
            Emit(sparks, p, Cone(n, 55f) * Random.Range(3f, 9f), Random.Range(.01f, .02f), Random.Range(.12f, .3f), new Color(1f, .62f, .25f, 1f));
        if (!light && hit.collider.GetComponentInParent<ElderboomGate>() == null) Hole(hit.point + n * .004f, n, surface);
        if (Time.time - lastImpactSound > .025f)
        {
            lastImpactSound = Time.time;
            string recorded = surface == ElderboomSurface.Metal ? "impactMetal_light_00" : surface == ElderboomSurface.Wood ? "impactWood_light_00"
                : surface == ElderboomSurface.Dirt ? "impactSoft_medium_00" : "impactMining_00";
            var clip = ElderboomAudio.Clip("kenney/" + recorded + Random.Range(0, 5));   // Kenney Impact Sounds, CC0
            if (clip == null) clip = ImpactClip(surface);                                 // procedural fallback
            ElderboomAudio.PlayClip(clip, hit.point, light ? .3f : .5f, true, Random.Range(.92f, 1.08f));
        }
    }

    static void Hole(Vector3 position, Vector3 normal, ElderboomSurface surface)
    {
        if (surface == ElderboomSurface.Dirt && Random.value < .5f) return;
        if (quad == null) quad = QuadMesh();
        if (block == null) block = new MaterialPropertyBlock();
        MeshRenderer renderer;
        if (holes.Count < MaxHoles)
        {
            var go = new GameObject("BulletHole"); go.transform.SetParent(host.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = holeMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            holes.Add(renderer);
        }
        else { renderer = holes[nextHole]; nextHole = (nextHole + 1) % MaxHoles; if (renderer == null) return; }
        float size = surface == ElderboomSurface.Metal ? .035f : surface == ElderboomSurface.Wood ? .055f : surface == ElderboomSurface.Dirt ? .09f : .07f;
        var t = renderer.transform;
        t.SetPositionAndRotation(position, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        t.localScale = Vector3.one * size * Random.Range(.85f, 1.2f);
        Color tint = surface == ElderboomSurface.Metal ? new Color(.12f, .11f, .1f, .95f) : surface == ElderboomSurface.Wood ? new Color(.09f, .06f, .035f, .95f)
            : surface == ElderboomSurface.Dirt ? new Color(.05f, .04f, .03f, .7f) : new Color(.07f, .065f, .06f, .95f);
        block.SetColor("_Color", tint); renderer.SetPropertyBlock(block);
        renderer.gameObject.SetActive(true);
    }

    static Mesh QuadMesh()
    {
        var m = new Mesh { name = "FeelQuad" };
        m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    // ---- Muzzle flash and tracers -------------------------------------------------------
    public static void MuzzleFlash(Vector3 position, Vector3 forward, float scale)
    {
        Ensure();
        Color hot = new Color(1f, .72f, .36f, 1f);
        Emit(flash, position + forward * .03f, Vector3.zero, Random.Range(.16f, .24f) * scale, .045f, hot);
        Emit(flash, position + forward * .12f * scale, forward * .5f, Random.Range(.10f, .16f) * scale, .04f, new Color(1f, .55f, .2f, .9f));
        for (int i = 0; i < 3; i++) Emit(sparks, position, Cone(forward, 14f) * Random.Range(6f, 12f), .008f, Random.Range(.04f, .09f), hot);
        Emit(dust, position + forward * .05f, forward * .5f + Vector3.up * .15f, Random.Range(.06f, .1f) * scale, .6f, new Color(.6f, .6f, .6f, .18f));
    }

    public static void Tracer(Vector3 from, Vector3 to, bool alwaysShow)
    {
        if (!alwaysShow && (tracerCounter++ & 1) == 1) return;
        Vector3 delta = to - from; float length = delta.magnitude;
        if (length < 1.5f) return;
        Ensure();
        TracerLine t;
        if (tracers.Count < MaxTracers)
        {
            var line = new GameObject("Tracer").AddComponent<LineRenderer>(); line.transform.SetParent(host.transform, false);
            line.sharedMaterial = tracerMat; line.positionCount = 2; line.useWorldSpace = true; line.numCapVertices = 2;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, .004f), new Keyframe(1f, .014f));
            line.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(new Color(1f, .7f, .4f), 0f), new GradientColorKey(new Color(1f, .85f, .6f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.9f, 1f) } };
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            t = new TracerLine { line = line }; tracers.Add(t);
        }
        else { t = tracers[nextTracer]; nextTracer = (nextTracer + 1) % MaxTracers; if (t.line == null) return; }
        t.from = from; t.dir = delta / length; t.length = length; t.travelled = .6f; t.speed = 340f; t.active = true;
        t.line.SetPosition(0, from); t.line.SetPosition(1, from); t.line.enabled = true;
    }

    // ---- Procedural textures ---------------------------------------------------------------
    static Texture2D SoftTexture()
    {
        if (soft != null) return soft;
        soft = Paint(64, (u, v) => { float r = Mathf.Sqrt(u * u + v * v); float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f); return new Color(1, 1, 1, a); });
        return soft;
    }
    static Texture2D HoleTexture()
    {
        if (hole != null) return hole;
        var rng = new System.Random(7);
        var jag = new float[32]; for (int i = 0; i < jag.Length; i++) jag[i] = .8f + (float)rng.NextDouble() * .35f;
        hole = Paint(64, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = (Mathf.Atan2(v, u) / (2f * Mathf.PI) + .5f) * jag.Length;
            int a0 = (int)angle % jag.Length; float edge = Mathf.Lerp(jag[a0], jag[(a0 + 1) % jag.Length], angle - Mathf.Floor(angle));
            float core = 1f - Mathf.SmoothStep(.18f, .3f, r);              // dark centre
            float ring = (1f - Mathf.SmoothStep(.55f * edge, .95f * edge, r)) * .55f; // chipped rim
            float shade = Mathf.Lerp(1f, .35f, core);
            return new Color(shade, shade, shade, Mathf.Clamp01(Mathf.Max(core, ring)));
        });
        return hole;
    }
    public static Texture2D VignetteTexture
    {
        get
        {
            if (vignette != null) return vignette;
            vignette = Paint(128, (u, v) => { float r = Mathf.Sqrt(u * u * .8f + v * v); return new Color(1, 1, 1, Mathf.SmoothStep(.55f, 1.25f, r)); });
            return vignette;
        }
    }
    // A short arc wedge pointing up; rotated around the reticle by the HUD.
    public static Texture2D DamageWedge
    {
        get
        {
            if (wedge != null) return wedge;
            wedge = new Texture2D(128, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[128 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 128; x++)
            {
                float dx = x - 63.5f, dy = y + 182f; // arc centred on the reticle: the HUD draws this 182-214 px above it (row 0 is the bottom)
                float r = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Abs(Mathf.Atan2(dx, dy)) * Mathf.Rad2Deg;
                float band = 1f - Mathf.Clamp01(Mathf.Abs(r - 196f) / 9f);
                float sides = 1f - Mathf.SmoothStep(13f, 18f, ang);
                px[y * 128 + x] = new Color(1, 1, 1, Mathf.Clamp01(band * 1.6f) * sides);
            }
            wedge.SetPixels(px); wedge.Apply(false, true); return wedge;
        }
    }
    static Texture2D Paint(int n, System.Func<float, float, Color> f)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + .5f) / n * 2f - 1f, (y + .5f) / n * 2f - 1f);
        t.SetPixels(px); t.Apply(false, true); return t;
    }

    // ---- Procedural audio --------------------------------------------------------------------
    const int Rate = 44100;
    public static AudioClip ImpactClip(ElderboomSurface s)
    {
        if (impactClips.TryGetValue(s, out var clip) && clip != null) return clip;
        var rng = new System.Random(11 + (int)s);
        float low = 0f;
        switch (s)
        {
            case ElderboomSurface.Metal:
                clip = Synth("impact_metal", .55f, t => Noise(rng) * Mathf.Exp(-t * 90f) * .5f
                    + (Mathf.Sin(2f * Mathf.PI * 1650f * t) + .7f * Mathf.Sin(2f * Mathf.PI * 2730f * t) + .4f * Mathf.Sin(2f * Mathf.PI * 4120f * t)) * Mathf.Exp(-t * 11f) * .3f);
                break;
            case ElderboomSurface.Wood:
                clip = Synth("impact_wood", .22f, t => { low += (Noise(rng) - low) * .18f; return low * Mathf.Exp(-t * 38f) * 2.2f + Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-t * 30f) * .6f; });
                break;
            case ElderboomSurface.Dirt:
                clip = Synth("impact_dirt", .2f, t => { low += (Noise(rng) - low) * .07f; return low * Mathf.Exp(-t * 26f) * 3f + Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-t * 24f) * .5f; });
                break;
            default:
                clip = Synth("impact_stone", .18f, t => Noise(rng) * Mathf.Exp(-t * 60f) * .8f + Mathf.Sin(2f * Mathf.PI * 2350f * t) * Mathf.Exp(-t * 110f) * .5f);
                break;
        }
        impactClips[s] = clip; return clip;
    }
    public static AudioClip Heartbeat
    {
        get
        {
            if (heartbeat != null) return heartbeat;
            heartbeat = Synth("heartbeat", .62f, t =>
            {
                float Beat(float start, float gain) { float u = t - start; if (u < 0f) return 0f; float f = 62f - u * 40f; return Mathf.Sin(2f * Mathf.PI * f * u) * Mathf.Exp(-u * 18f) * gain; }
                return Beat(0f, 1f) + Beat(.27f, .7f);
            });
            return heartbeat;
        }
    }
    static float Noise(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;
    static AudioClip Synth(string name, float seconds, System.Func<float, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count]; float peak = .0001f;
        for (int i = 0; i < count; i++) { data[i] = sample(i / (float)Rate); peak = Mathf.Max(peak, Mathf.Abs(data[i])); }
        float gain = .9f / peak; int fade = Mathf.Min(count, Rate / 200);
        for (int i = 0; i < count; i++) { data[i] *= gain; if (i >= count - fade) data[i] *= (count - i) / (float)fade; }
        var clip = AudioClip.Create(name, count, 1, Rate, false); clip.SetData(data, 0); return clip;
    }
}
