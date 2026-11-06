using System.Collections;
using UnityEngine;

// Eli: projects an acoustic decoy where she aims. Zombies near that point stop
// chasing her and go to investigate it. Not in the Godot original (which had no
// second Eli ability); designed to sit between Ignore (everyone) and Freeze (close).
[CreateAssetMenu(menuName = "Elderboom/Abilities/Acoustic Pulse", fileName = "ElderboomAcousticPulseAbility")]
public sealed class ElderboomAcousticPulseAbility : ElderboomAnimatedAbility
{
    public float windup = 1.11111f;
    [Tooltip("How far the decoy can be thrown along the aim.")]
    public float throwRange = 30f;
    [Tooltip("Zombies within this distance of the decoy are lured.")]
    public float lureRadius = 18f;
    public float lureSeconds = 8f;
    public string sound = "abilities/eli/Zombie_Ignore_melodi_Ability_1";
    public float soundPitch = 1.35f;

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(windup);
        var camera = game.AbilityCamera();
        if (camera == null || source == null) yield break;
        Vector3 from = camera.transform.position, dir = camera.transform.forward;
        Vector3 point = Physics.Raycast(from, dir, out var hit, throwRange, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
            ? hit.point + hit.normal * .3f : from + dir * throwRange;
        if (UnityEngine.AI.NavMesh.SamplePosition(point, out var nav, 4f, UnityEngine.AI.NavMesh.AllAreas)) point = nav.position;

        ElderboomAudio.Play(sound, point, .8f, true, soundPitch);
        int lured = 0;
        foreach (var zombie in game.Zombies)
        {
            if (zombie == null || !zombie.IsAlive) continue;
            if (Vector3.Distance(zombie.transform.position, point) > lureRadius) continue;
            zombie.IgnorePlayerFor(lureSeconds);
            zombie.HearNoise(point, lureSeconds);
            lured++;
        }
        game.ShowNotice("ACOUSTIC PULSE // " + lured + " ZOMBIES LURED");

        // A short blue ping at the decoy so the player sees where it landed.
        var ping = game.AbilityObject(new GameObject("Eli_Acoustic_Pulse"));
        ping.transform.position = point + Vector3.up * .5f;
        var light = ping.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(.25f, .7f, 1f); light.range = 6f; light.shadows = LightShadows.None;
        float t = 0f;
        while (t < 1.2f && light != null)
        {
            t += Time.deltaTime;
            light.intensity = Mathf.Lerp(6000f, 0f, t / 1.2f) * (.75f + .25f * Mathf.Sin(t * 30f));
            yield return null;
        }
        if (ping != null) Destroy(ping);
    }
}
