using UnityEngine;

// Per-model stats from the character's build (multipliers on the wave's difficulty values):
// a heavy body is slow, tough and hits hard; a tall gaunt one is fast and fragile with a longer reach.
public sealed class ElderboomZombieProfile : MonoBehaviour
{
    [Tooltip("Shown in debug logs")] public string build = "average";
    public float health = 1f, damage = 1f, speed = 1f, attackInterval = 1f;
    [Tooltip("Push / shockwave strength taken (1 = normal, lower resists)")] public float pushTaken = 1f;
    [Tooltip("Extra melee reach in metres")] public float reach = 0f;
    [Tooltip("Never rolled as a runner (too heavy) / brute (too frail)")] public bool noRunner, noBrute;
}
