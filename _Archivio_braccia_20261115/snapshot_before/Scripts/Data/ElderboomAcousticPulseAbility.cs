using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Eli: sticks the acoustic device to the wall (or any surface) she aims at. While it plays, every zombie that
// can hear it stops chasing and walks to that spot; when it runs out the device sparks and goes quiet.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Acoustic Pulse", fileName = "ElderboomAcousticPulseAbility")]
public sealed class ElderboomAcousticPulseAbility : ElderboomAnimatedAbility
{
    public float windup = 1.11111f;
    [Tooltip("How far away the device can be stuck along the aim.")]
    public float throwRange = 30f;
    [Tooltip("Zombies within this distance of the device hear it and are drawn to it.")]
    public float hearRadius = 28f;
    [Tooltip("How long the device keeps playing once attached.")]
    public float deviceSeconds = 12f;
    [Tooltip("Zombies this close to a player keep fighting instead of turning to the sound.")]
    public float engagedDistance = 2.5f;
    public string sound = "abilities/eli/Zombie_Ignore_melodi_Ability_1";
    public float soundPitch = 1.35f;

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(windup);
        var camera = game.AbilityCamera();
        if (camera == null || source == null) yield break;
        Vector3 from = camera.transform.position, dir = camera.transform.forward;
        if (!Physics.Raycast(from, dir, out var hit, throwRange, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
            || hit.collider.GetComponentInParent<ElderboomZombie>() != null)
        {
            game.ShowNotice("ACOUSTIC DEVICE // NO WALL IN RANGE");
            yield break;
        }
        // The device sits on the wall; zombies gather on the ground in front of it.
        Vector3 mount = hit.point + hit.normal * .02f;
        Vector3 lure = hit.point + hit.normal * .8f;
        if (NavMesh.SamplePosition(lure, out var nav, 4f, NavMesh.AllAreas)) lure = nav.position;
        else if (NavMesh.SamplePosition(lure + Vector3.down * 2f, out nav, 6f, NavMesh.AllAreas)) lure = nav.position;

        var device = game.AbilityObject(BuildDevice(mount, hit.normal));
        var light = device.GetComponentInChildren<Light>();
        var audio = device.AddComponent<AudioSource>();
        audio.clip = ElderboomAudio.Clip(sound); audio.loop = true; audio.pitch = soundPitch; audio.volume = .8f;
        audio.spatialBlend = 1f; audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 3f; audio.maxDistance = hearRadius + 10f; audio.dopplerLevel = 0f;
        if (audio.clip != null) audio.Play();
        ElderboomAudio.Play("weapons/shockwave_glove_2", mount, .25f, true, 2.2f);

        int lured = 0; float started = Time.time, nextCall = 0f;
        var counted = new System.Collections.Generic.HashSet<ElderboomZombie>();
        while (Time.time - started < deviceSeconds && device != null)
        {
            float t = Time.time - started;
            if (light != null) light.intensity = 900f + 700f * Mathf.Abs(Mathf.Sin(t * 6f));
            if (Time.time >= nextCall)
            {
                nextCall = Time.time + .75f;
                foreach (var zombie in game.Zombies)
                {
                    if (zombie == null || !zombie.IsAlive) continue;
                    if ((zombie.transform.position - mount).sqrMagnitude > hearRadius * hearRadius) continue;
                    if (source != null && !source.IsDead && (zombie.transform.position - source.transform.position).sqrMagnitude < engagedDistance * engagedDistance) continue;
                    zombie.IgnorePlayerFor(1.3f);
                    zombie.HearNoise(lure, 1.6f);
                    if (counted.Add(zombie)) lured++;
                }
                if (t < .1f) game.ShowNotice("ACOUSTIC DEVICE ATTACHED // " + lured + " ZOMBIES HEAR IT");
            }
            yield return null;
        }
        if (device == null) yield break;
        // Last burst: zombies keep walking to the spot for a moment, then the chase resumes.
        if (audio != null) audio.Stop();
        ElderboomAudio.Play("weapons/shockwave_glove_2", mount, .3f, true, 2.6f);
        game.ShowNotice("ACOUSTIC DEVICE SILENT // " + lured + " ZOMBIES LURED");
        float fade = 0f;
        while (fade < .6f && light != null) { fade += Time.deltaTime; light.intensity = Mathf.Lerp(2500f, 0f, fade / .6f); yield return null; }
        if (device != null) Destroy(device);
    }

    // Small puck with a blue emitter, flat against the surface.
    static GameObject BuildDevice(Vector3 at, Vector3 normal)
    {
        var root = new GameObject("Eli_Acoustic_Device");
        root.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, normal));
        var shader = Shader.Find("HDRP/Lit");
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(body.GetComponent<Collider>());
        body.name = "Body"; body.transform.SetParent(root.transform, false); body.transform.localScale = new Vector3(.14f, .025f, .14f); body.transform.localPosition = Vector3.up * .025f;
        var emitter = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.Destroy(emitter.GetComponent<Collider>());
        emitter.name = "Emitter"; emitter.transform.SetParent(root.transform, false); emitter.transform.localScale = new Vector3(.08f, .012f, .08f); emitter.transform.localPosition = Vector3.up * .055f;
        if (shader != null)
        {
            var dark = new Material(shader); dark.SetColor("_BaseColor", new Color(.06f, .065f, .07f)); dark.SetFloat("_Metallic", .6f); dark.SetFloat("_Smoothness", .55f);
            body.GetComponent<Renderer>().sharedMaterial = dark;
            var glow = new Material(shader); var blue = new Color(.25f, .7f, 1f);
            glow.SetColor("_BaseColor", blue * .3f); glow.SetColor("_EmissiveColor", blue * 40f); glow.EnableKeyword("_EMISSIVE_COLOR_MAP");
            emitter.GetComponent<Renderer>().sharedMaterial = glow;
        }
        var lightGo = new GameObject("Glow"); lightGo.transform.SetParent(root.transform, false); lightGo.transform.localPosition = Vector3.up * .25f;
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(.25f, .7f, 1f); light.range = 4f; light.intensity = 1200f; light.shadows = LightShadows.None;
        return root;
    }
}
