using System.Collections;
using UnityEngine;

// Tactical: a glove shockwave that pushes, stuns and damages zombies in front.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Shockwave", fileName = "ElderboomShockwaveAbility")]
public sealed class ElderboomShockwaveAbility : ElderboomAbilityDefinition
{
    public float range = 5f;
    [Tooltip("Minimum dot product with the view direction (0.15 ≈ 81° half-angle).")]
    public float coneDot = .15f;
    public float minForce = 10f, maxForce = 28f;
    public float stun = .75f;
    public float closeDistance = 3f;
    public float closeDamage = 3f, farDamage = 2f;
    public float fovKick = 5f, trauma = .45f;

    public override bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner)
    {
        var arms = game.TacticalArms(source);
        game.BeginAbilityAnimation(arms.BeginShockwave(source.ActiveWeaponRoot));
        game.ShowNotice("Shockwave!");
        game.StartCoroutine(Run(game, source));
        return true;
    }

    IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(ElderboomTacticalArms.ShockSound);
        var camera = game.AbilityCamera();
        if (camera != null) ElderboomAudio.Play("weapons/shockwave_glove_2", camera.transform.position, 0.85f, false, 1f, 0.675f);
        yield return new WaitForSeconds(ElderboomTacticalArms.ShockImpact - ElderboomTacticalArms.ShockSound);
        camera = game.AbilityCamera();
        if (camera == null || source == null) yield break;
        Transform view = camera.transform;
        Vector3 origin = view.position + view.forward * 0.55f - view.right * 0.24f - view.up * 0.12f;
        foreach (var zombie in game.FindAbilityTargets(range, coneDot, origin))
        {
            float distance = Vector3.Distance(source.transform.position, zombie.transform.position);
            float proximity = 1f - Mathf.Clamp01(distance / range);
            zombie.ApplyShockwave(origin, Mathf.Lerp(minForce, maxForce, proximity), stun, distance < closeDistance ? closeDamage : farDamage);
        }
        source.AddFovKick(fovKick); source.AddTrauma(trauma);
        Vector3 ground = origin;
        if (Physics.Raycast(origin + Vector3.up * 0.4f, Vector3.down, out var groundHit, 6f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore))
            ground = groundHit.point + Vector3.up * 0.05f;
        else ground.y = source.transform.position.y + 0.05f;
        game.AbilityObject(ElderboomImportedVfx.Shockwave(ground));
        var flash = game.AbilityObject(new GameObject("ShockwaveFlash"));
        flash.transform.position = origin;
        var light = flash.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(0f, 0.9f, 1f); light.range = 8f; light.intensity = 12000f; light.shadows = LightShadows.None;
        float fade = 0f;
        while (fade < 8f / 60f && light != null)
        {
            fade += Time.deltaTime;
            light.intensity = Mathf.Lerp(12000f, 0f, fade / (8f / 60f));
            yield return null;
        }
        if (flash != null) Destroy(flash);
    }
}
