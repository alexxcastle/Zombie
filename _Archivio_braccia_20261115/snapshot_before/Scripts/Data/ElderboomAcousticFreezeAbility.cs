using System.Collections;
using UnityEngine;

// Eli: zombies inside the radius around the player are frozen while the signal plays.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Freeze Signal", fileName = "ElderboomAcousticFreezeAbility")]
public sealed class ElderboomAcousticFreezeAbility : ElderboomAnimatedAbility
{
    public float windup = 1.11111f;
    public float duration = 15f;
    public float radius = 7f;
    public float tick = .1f;
    public string sound = "abilities/eli/zombie_blocked_7metersradius_ability_2";

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        yield return new WaitForSeconds(windup);
        ElderboomAudio.Play(sound, source.transform.position, .75f);
        float end = Time.time + duration;
        while (Time.time < end && source != null)
        {
            float remaining = end - Time.time;
            foreach (var zombie in game.Zombies)
                if (zombie != null && Vector3.Distance(zombie.transform.position, source.transform.position) <= radius)
                    zombie.FreezeFor(remaining);
            yield return new WaitForSeconds(tick);
        }
    }
}
