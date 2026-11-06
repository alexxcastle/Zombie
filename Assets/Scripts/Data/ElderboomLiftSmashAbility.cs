using System.Collections;
using UnityEngine;

// Tactical: lifts the zombies ahead and slams them down. Needs at least one target;
// without one it does not fire and costs no cooldown.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Lift and Smash", fileName = "ElderboomLiftSmashAbility")]
public sealed class ElderboomLiftSmashAbility : ElderboomAbilityDefinition
{
    public float range = 5f;
    public float coneDot = .42f;
    public float liftHeight = 1.7f;
    public float impactDamage = 3f;
    public float recovery = .65f;
    public float impactFovKick = 2.5f;

    public override bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner)
    {
        var arms = game.TacticalArms(source);
        var camera = game.AbilityCamera(source);
        var targets = game.FindAbilityTargets(range, coneDot, camera.transform.position);
        if (targets.Count == 0)
        {
            game.ShowNotice("No zombies ahead within " + Mathf.RoundToInt(range) + " m");
            return false;
        }
        game.BeginAbilityAnimation(arms.BeginLift(source.ActiveWeaponRoot));
        game.ShowNotice("Lift & Smash — " + targets.Count + " zombie");
        game.StartCoroutine(ElderboomAbilityFx.PlayAfter(ElderboomTacticalArms.LiftCharge, "abilities/lift_smash_magnetic_levitation", source.transform.position, 0.4f));
        // Rise, hang and slam are timed to the lift animation (frames at 60 fps).
        const float rise = 30f / 60f, slam = 0.08f, hang = 100f / 60f - rise - slam;
        foreach (var zombie in targets)
            zombie.BeginLift(liftHeight, rise, hang, slam, impactDamage, recovery, zombie.transform.position - source.transform.position);
        game.StartCoroutine(Impact(source));
        return true;
    }

    IEnumerator Impact(HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(ElderboomTacticalArms.LiftImpact);
        if (source != null) { source.AddFovKick(impactFovKick); source.AddTrauma(.55f); }
        ElderboomAudio.Play("abilities/lift_smash_hard_magnetic_impact", source != null ? source.transform.position : Vector3.zero, 0.7f);
    }
}
