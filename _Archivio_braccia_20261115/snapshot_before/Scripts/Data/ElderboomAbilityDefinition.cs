using System.Collections;
using UnityEngine;

// An operator ability as data. The game owns selection and cooldowns; the ability
// decides what happens when it is used. Activate returns true when the ability
// actually fired, which is when its cooldown starts.
public abstract class ElderboomAbilityDefinition : ScriptableObject
{
    public string id = "ability";
    public string displayName = "Ability";
    [Min(0f)] public float cooldown = 12f;
    [TextArea] public string description;

    public abstract bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner);
}

// Abilities that play the operator's ability animation, then run an effect coroutine.
public abstract class ElderboomAnimatedAbility : ElderboomAbilityDefinition
{
    public override bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner)
    {
        float seconds = owner != null ? owner.abilityAnimationSeconds : 3.1f;
        game.ShowNotice(displayName.ToUpperInvariant());
        source.TriggerAbilityAnimation(seconds);
        game.BeginAbilityAnimation(seconds);
        game.StartCoroutine(Run(game, source));
        return true;
    }

    protected abstract IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source);
}

// Small helpers shared by ability assets.
public static class ElderboomAbilityFx
{
    public static IEnumerator PlayAfter(float delay, string path, Vector3 position, float volume)
    {
        yield return new WaitForSeconds(delay);
        ElderboomAudio.Play(path, position, volume);
    }
}
