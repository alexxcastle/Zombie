using System.Collections.Generic;
using UnityEngine;

// The church tech field kit runs on one generator: every match starts with it off. Starting it (credits, at the
// generator) powers what is cabled to it: the floodlights come on and the weapon upgrader can be used.
// Built at runtime from the scene's Church_Tech_Setup (ElderboomClaudeBlocks.ChurchTech places it).
public sealed class ElderboomPowerGrid : MonoBehaviour
{
    public static ElderboomPowerGrid Instance { get; private set; }
    public static bool Powered => Instance == null || Instance.on; // no grid in the scene: nothing is gated
    public const int Cost = 1000;
    const float Reach = 3f;

    Transform generator;
    readonly List<Light> lights = new List<Light>();
    readonly List<(Renderer r, Material[] lit)> glows = new List<(Renderer, Material[])>();
    bool on;
    public bool On => on;
    AudioSource hum;

    public static void Ensure()
    {
        if (Instance != null) return;
        var root = GameObject.Find("Church_Tech_Setup");
        if (root == null) return;
        Instance = root.AddComponent<ElderboomPowerGrid>();
    }

    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (generator == null && n.Contains("generator") && t.parent == transform) generator = t;
            if (n.Contains("floodlight") && t.parent == transform)
                foreach (var l in t.GetComponentsInChildren<Light>(true)) lights.Add(l);
        }
        ElderboomEvents.SessionStarted += ResetSession;
        Apply(false, false);
    }
    void OnDestroy() { ElderboomEvents.SessionStarted -= ResetSession; if (Instance == this) Instance = null; }
    void ResetSession() { awaitingHost = false; Apply(false, false); }

    // Co-op: the generator state from the host (or a client's start, on the host). Starting it plays the full
    // start-up here too; a running grid is never switched off by a late snapshot.
    public void NetSetPower(bool power)
    {
        if (power) awaitingHost = false;
        if (!power || on) return;
        Apply(true, true);
    }

    void Apply(bool power, bool announce)
    {
        on = power;
        foreach (var l in lights) if (l != null) l.enabled = power;
        if (hum == null && generator != null) hum = ElderboomAudio.Loop("ambience/generator_hum", generator.position + Vector3.up * .5f, .5f, 2f, 20f, transform);
        if (hum != null) { if (power) { if (!hum.isPlaying) hum.Play(); } else hum.Stop(); }
        if (!announce || generator == null) return;
        ElderboomAudio.Play("power/generator_start", generator.position, 1f);
        StartCoroutine(LightsOn());
    }
    System.Collections.IEnumerator LightsOn()
    {
        foreach (var l in lights) if (l != null) l.enabled = false;
        yield return new WaitForSeconds(2.6f);
        var at = lights.Count > 0 && lights[0] != null ? lights[0].transform.position : transform.position;
        ElderboomAudio.Play("power/floodlights_on", at, 1f);
        foreach (var l in lights) { if (l != null) l.enabled = true; yield return new WaitForSeconds(.18f); }
        var altar = ElderboomAltarUpgrader.Instance;
        if (altar != null) ElderboomAudio.Play("power/upgrader_boot", altar.transform.position, .9f);
        ElderboomGame.Active?.ShowNotice("POWER ON // WEAPON UPGRADER ONLINE", 4f);
    }

    // The upgrader asks before taking a weapon.
    public static bool RequirePower(Vector3 at)
    {
        if (Powered) return true;
        ElderboomAudio.Play("power/no_power", at, .8f);
        ElderboomGame.Active?.ShowNotice("NO POWER // START THE GENERATOR", 3f);
        return false;
    }

    // Client that started the generator: repeat the request until the host's snapshot shows it running.
    bool awaitingHost; float resendAt;
    void Update()
    {
        if (on && awaitingHost && ElderboomLan.IsClient && Time.unscaledTime >= resendAt) { resendAt = Time.unscaledTime + 1.5f; ElderboomLan.GeneratorStarted(); }
        if (on || generator == null) return;
        var game = ElderboomGame.Active; var player = game != null ? game.Player : null;
        if (player == null || !game.CanInteract || player.IsDead || player.Seated) return;
        if (Vector3.Distance(player.transform.position, generator.position) > Reach) return;
        ElderboomPrompt.Offer(player.transform.position, generator.position, player.ActionLabel("interact") + " · START GENERATOR\n" + Cost + " CREDITS");
        if (!player.ActionDown("interact")) return;
        if (game.Credits < Cost) { game.ShowNotice("NEED " + Cost + " CREDITS"); return; }
        if (!game.TrySpend(Cost)) return;
        Apply(true, true);
        ElderboomLan.GeneratorStarted(); // co-op: the host shares it with everyone
        if (ElderboomLan.IsClient) { awaitingHost = true; resendAt = Time.unscaledTime + 1.5f; }
    }
}
