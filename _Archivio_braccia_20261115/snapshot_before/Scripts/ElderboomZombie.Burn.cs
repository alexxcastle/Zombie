using UnityEngine;

// Flamethrower: a zombie hit by the flame catches fire, burns for a few seconds and takes damage over time.
// Hitting it again keeps it burning. Flames, smoke, a flickering light and a crackle loop follow the body.
public sealed partial class ElderboomZombie
{
    public const float BurnSeconds = 4f, BurnDamagePerSecond = 10f;
    const float BurnTick = .25f;
    const int MaxBurnLights = 6;
    static int burnLights;

    float burnUntil, burnDps, burnTickAt, burnSeed = -1f;
    GameObject burnFx;
    ParticleSystem burnFlames, burnSmoke;
    Light burnLight;
    AudioSource burnAudio;

    public bool Burning => Time.time < burnUntil;

    public void Ignite(float seconds = BurnSeconds, float damagePerSecond = BurnDamagePerSecond)
    {
        if (dead) return;
        if (Forward(9, seconds, damagePerSecond)) { ShowBurn(seconds); return; } // LAN client: the host does the damage
        if (!Burning) burnTickAt = Time.time + BurnTick;
        burnDps = Mathf.Max(burnDps, damagePerSecond);
        ShowBurn(seconds);
    }

    void ShowBurn(float seconds)
    {
        bool wasBurning = Burning;
        burnUntil = Mathf.Max(burnUntil, Time.time + seconds);
        if (burnFx == null) BuildBurnFx();
        if (!burnFlames.isPlaying) burnFlames.Play();
        if (!burnSmoke.isPlaying) burnSmoke.Play();
        if (!wasBurning) { int v = -1; ElderboomAudio.Zombie("hit", ref v, transform.position); }
    }

    // Called every frame from Update.
    void TickBurn()
    {
        if (burnFx == null) return;
        bool burning = Burning && !dead;
        if (burnLight != null) burnLight.intensity = burning ? 2200f * (.75f + .5f * Mathf.PerlinNoise(Time.time * 9f, burnSeed)) : 0f;
        if (burnAudio != null) burnAudio.volume = Mathf.MoveTowards(burnAudio.volume, burning ? .55f * ElderboomAudio.Effects : 0f, Time.deltaTime * 1.5f);
        if (!burning)
        {
            if (burnFlames.isEmitting) { burnFlames.Stop(true, ParticleSystemStopBehavior.StopEmitting); burnSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (!dead && !burnFlames.IsAlive(true) && (burnAudio == null || burnAudio.volume <= 0f)) ClearBurnFx();
            burnDps = 0f;
            return;
        }
        if (NetProxy || Time.time < burnTickAt) return;
        burnTickAt += BurnTick;
        float amount = burnDps * BurnTick;
        if (game != null && game.InstaKill) amount = health + 1f;
        if (health - amount > 0f) health -= amount; // no blood or flinch for each tick
        else TakeDamage(amount, transform.position);
    }

    void BuildBurnFx()
    {
        burnSeed = Random.Range(0f, 100f);
        burnFx = new GameObject("Burning");
        burnFx.transform.SetParent(transform, false);
        burnFx.transform.localPosition = Vector3.up * .95f;

        burnFlames = ElderboomCombatVfx.Particles(burnFx, new Color(1f, .62f, .16f), .55f, .35f, .5f);
        var main = burnFlames.main; main.loop = true; main.maxParticles = 160; main.gravityModifier = -.55f;
        main.startSize = new ParticleSystem.MinMaxCurve(.18f, .5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var emission = burnFlames.emission; emission.rateOverTime = 85;
        var shape = burnFlames.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(.45f, 1.45f, .32f);
        var size = burnFlames.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .15f));
        var noise = burnFlames.noise; noise.enabled = true; noise.strength = .6f; noise.frequency = 1.4f; noise.scrollSpeed = 1.2f;

        var smokeGo = new GameObject("Smoke"); smokeGo.transform.SetParent(burnFx.transform, false); smokeGo.transform.localPosition = Vector3.up * .6f;
        burnSmoke = ElderboomCombatVfx.Particles(smokeGo, new Color(.09f, .08f, .075f, .55f), 1.8f, .6f, .9f);
        var smain = burnSmoke.main; smain.loop = true; smain.maxParticles = 60; smain.gravityModifier = -.25f;
        var semission = burnSmoke.emission; semission.rateOverTime = 14;
        var sshape = burnSmoke.shape; sshape.shapeType = ParticleSystemShapeType.Box; sshape.scale = new Vector3(.35f, .5f, .3f);
        var sfade = burnSmoke.colorOverLifetime; sfade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(.12f, .1f, .09f), 0f), new GradientColorKey(new Color(.05f, .05f, .05f), 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.45f, .2f), new GradientAlphaKey(0f, 1f) });
        sfade.color = gradient;

        if (burnLights < MaxBurnLights)
        {
            burnLights++;
            var lightGo = new GameObject("FireLight"); lightGo.transform.SetParent(burnFx.transform, false); lightGo.transform.localPosition = Vector3.up * .3f;
            burnLight = lightGo.AddComponent<Light>();
            burnLight.type = LightType.Point; burnLight.color = new Color(1f, .55f, .18f); burnLight.range = 5f; burnLight.intensity = 0f; burnLight.shadows = LightShadows.None;
        }
        burnAudio = ElderboomAudio.Loop("weapons/flamethrower/burning_loop", burnFx.transform.position, 0f, 2f, 24f, burnFx.transform, false);
    }

    void ClearBurnFx()
    {
        if (burnLight != null) burnLights = Mathf.Max(0, burnLights - 1);
        if (burnFx != null) Destroy(burnFx);
        burnFx = null; burnFlames = burnSmoke = null; burnLight = null; burnAudio = null;
    }

    void OnDestroy() { if (burnLight != null) burnLights = Mathf.Max(0, burnLights - 1); burnLight = null; }
}
