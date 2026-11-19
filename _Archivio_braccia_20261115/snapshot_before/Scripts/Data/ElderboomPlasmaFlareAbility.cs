using System.Collections;
using UnityEngine;

// Soph: charges a plasma flare in front of the camera, then blinds zombies in a cone.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Blinding Flare", fileName = "ElderboomPlasmaFlareAbility")]
public sealed class ElderboomPlasmaFlareAbility : ElderboomAnimatedAbility
{
    public float windup = .65556f;
    public float chargeSeconds = 3f;
    public float range = 15f;
    [Range(5f, 90f)] public float coneHalfAngle = 35f;
    public float blindStrength = 2f;
    public float peakIntensity = 9500f;

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(windup);
        var camera = game.AbilityCamera();
        if (camera == null) yield break;
        var flare = game.AbilityObject(new GameObject("Soph_Plasma_Blinding_Flare"));
        flare.transform.SetParent(camera.transform, false);
        flare.transform.localPosition = new Vector3(0, 0, .8f);
        var light = flare.AddComponent<Light>();
        light.type = LightType.Point; light.range = range; light.color = new Color(1f, .22f, .02f); light.intensity = 0f;
        float t = 0f;
        while (t < chargeSeconds)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / chargeSeconds);
            light.intensity = Mathf.Lerp(0f, peakIntensity, p * p);
            light.color = Color.Lerp(new Color(1f, .22f, .02f), new Color(1f, .94f, .82f), p);
            yield return null;
        }
        if (camera == null) yield break;
        float minDot = Mathf.Cos(coneHalfAngle * Mathf.Deg2Rad);
        foreach (var zombie in game.Zombies)
        {
            if (zombie == null) continue;
            var offset = zombie.transform.position + Vector3.up - camera.transform.position;
            if (offset.magnitude > range || Vector3.Dot(camera.transform.forward, offset.normalized) < minDot) continue;
            if (Physics.Raycast(camera.transform.position, offset.normalized, out var hit, offset.magnitude, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<ElderboomZombie>() != zombie) continue;
            zombie.ApplyPlasmaBlind(blindStrength);
        }
        Destroy(flare, .18f);
        game.ShowNotice("PLASMA FLARE // HORDE BLINDED");
    }
}
