using System;
using UnityEngine;

// Game-wide events. Systems publish here instead of calling each other directly;
// HUD, audio, triggers and future systems subscribe.
public static class ElderboomEvents
{
    public static event Action SessionStarted;
    public static event Action<int> WaveStarted;
    public static event Action<int> WaveCleared;
    public static event Action<ElderboomZombie, int> ZombieKilled;
    public static event Action<float, Vector3> PlayerDamaged;
    public static event Action PlayerDied;
    public static event Action<string> AbilityUsed;
    public static event Action<string> PowerUpActivated;
    public static event Action<string> AreaUnlocked;
    public static event Action<string> TriggerFired;

    public static void RaiseSessionStarted() => SessionStarted?.Invoke();
    public static void RaiseWaveStarted(int wave) => WaveStarted?.Invoke(wave);
    public static void RaiseWaveCleared(int wave) => WaveCleared?.Invoke(wave);
    public static void RaiseZombieKilled(ElderboomZombie zombie, int points) => ZombieKilled?.Invoke(zombie, points);
    public static void RaisePlayerDamaged(float amount, Vector3 source) => PlayerDamaged?.Invoke(amount, source);
    public static void RaisePlayerDied() => PlayerDied?.Invoke();
    public static void RaiseAbilityUsed(string id) => AbilityUsed?.Invoke(id);
    public static void RaisePowerUpActivated(string id) => PowerUpActivated?.Invoke(id);
    public static void RaiseAreaUnlocked(string area) => AreaUnlocked?.Invoke(area);
    public static void RaiseTriggerFired(string trigger) => TriggerFired?.Invoke(trigger);

    // Static events survive Enter Play Mode without a domain reload; start clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSubscribers()
    {
        SessionStarted = null; WaveStarted = null; WaveCleared = null; ZombieKilled = null; PlayerDamaged = null; PlayerDied = null;
        AbilityUsed = null; PowerUpActivated = null; AreaUnlocked = null; TriggerFired = null;
    }
}
