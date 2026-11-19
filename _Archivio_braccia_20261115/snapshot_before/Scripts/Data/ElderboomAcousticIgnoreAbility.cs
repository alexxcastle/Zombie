using System.Collections;
using UnityEngine;

// Eli: an acoustic signal that makes every zombie ignore the player for a while.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Ignore Signal", fileName = "ElderboomAcousticIgnoreAbility")]
public sealed class ElderboomAcousticIgnoreAbility : ElderboomAnimatedAbility
{
    [Tooltip("Seconds to the button press in Eli's animation.")]
    public float windup = 1.11111f;
    public float duration = 15f;
    public string sound = "abilities/eli/Zombie_Ignore_melodi_Ability_1";

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(windup);
        game.IgnorePlayerFor(duration);
        ElderboomAudio.Play(sound, source.transform.position, .75f);
        game.ShowNotice("ACOUSTIC SIGNAL // IGNORED FOR " + Mathf.RoundToInt(duration) + " SECONDS");
    }
}
