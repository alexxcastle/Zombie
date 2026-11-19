using System.Collections;
using UnityEngine;

// Soph: draws the plasma pistol and fires a volley of homing-free plasma bolts.
[CreateAssetMenu(menuName = "Elderboom/Abilities/Plasma Volley", fileName = "ElderboomPlasmaVolleyAbility")]
public sealed class ElderboomPlasmaVolleyAbility : ElderboomAnimatedAbility
{
    [Tooltip("Seconds from activation to the first shot (matches the draw animation).")]
    public float windup = 1.65556f;
    [Min(1)] public int shots = 3;
    public float shotInterval = 1f;
    public float boltDamage = 50f;
    public float shotTrauma = .14f;
    public string shotSound = "soph_abilities/plasma_shot";

    protected override IEnumerator Run(ElderboomGame game, HDRPSlicePlayer source)
    {
        int token = game.Session;
        yield return new WaitForSeconds(windup);
        for (int i = 0; i < shots; i++)
        {
            var camera = game.AbilityCamera();
            if (!game.IsSessionActive(token) || camera == null) break;
            var muzzle = source.AbilityMuzzle;
            Vector3 origin = muzzle != null ? muzzle.position : camera.transform.position + camera.transform.forward * .45f;
            ElderboomPlasmaBolt.Fire(camera, origin, boltDamage, source, game);
            ElderboomAudio.Play(shotSound, camera.transform.position, .9f);
            source.AddTrauma(shotTrauma);
            yield return new WaitForSeconds(shotInterval);
        }
    }
}
