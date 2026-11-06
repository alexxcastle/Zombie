using System.Collections;
using UnityEngine;

// Tactical: a barrier around the player that holds zombies outside its radius.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Keep Away", fileName = "ElderboomKeepAwayAbility")]
public sealed class ElderboomKeepAwayAbility : ElderboomAbilityDefinition
{
    public float barrierRadius = 3f;
    [Tooltip("Zombies closer than this are checked against the barrier each frame.")]
    public float checkRadius = 4.2f;

    public override bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner)
    {
        var arms = game.TacticalArms(source);
        game.BeginAbilityAnimation(arms.BeginKeep(source.ActiveWeaponRoot));
        game.ShowNotice("Keep Away — " + barrierRadius.ToString("0.#") + " m barrier for " + Mathf.RoundToInt(ElderboomTacticalArms.KeepDuration) + " seconds");
        game.StartCoroutine(Field(game, source, arms));
        return true;
    }

    IEnumerator Field(ElderboomGame game, HDRPSlicePlayer source, ElderboomTacticalArms arms)
    {
        yield return new WaitForSeconds(ElderboomTacticalArms.KeepFieldStart);
        var spiral = source != null ? game.AbilityObject(ElderboomImportedVfx.KeepAway(source.transform)) : null;
        float end = Time.time + ElderboomTacticalArms.KeepDuration;
        while (Time.time < end && game.IsPlaying && source != null)
        {
            float fade = Mathf.Min(1f, Mathf.Clamp01((end - Time.time) / 0.25f));
            if (spiral == null && arms != null) arms.SetField(true, fade);
            foreach (var zombie in game.Zombies)
            {
                if (zombie == null || !zombie.IsAlive) continue;
                Vector3 flat = zombie.transform.position - source.transform.position; flat.y = 0f;
                if (flat.sqrMagnitude <= checkRadius * checkRadius) zombie.EnforceKeepAway(source.transform.position, barrierRadius);
            }
            yield return null;
        }
        if (spiral != null) Destroy(spiral);
        if (arms != null) arms.SetField(false, 0f);
    }
}
