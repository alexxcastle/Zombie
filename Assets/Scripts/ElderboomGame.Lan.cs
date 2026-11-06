using UnityEngine;

// LAN co-op hooks (see ElderboomLan): the host keeps the waves going while any player is alive;
// clients mirror the host's wave counter and receive their own kill rewards.
public sealed partial class ElderboomGame
{
    bool TeamDown => player == null || (player.IsDead && !(ElderboomLan.IsHost && ElderboomLan.AnyRemoteAlive));
    public int ShotsFired => shotsFired;
    public bool DoublePointsActive => DoublePoints;

    internal void NetSetWave(int number, int remaining)
    {
        if (state != State.Playing) return;
        if (number > 0 && number != wave)
        {
            wave = number; waveBannerAt = Time.time;
            ShowNotice("WAVE " + number);
            ElderboomEvents.RaiseWaveStarted(number);
        }
        waveRemaining = remaining;
    }

    internal void NetAddKill(int points)
    {
        if (state != State.Playing) return;
        credits += points; totalScore += points; kills++;
    }

    internal void NetTrackZombie(ElderboomZombie zombie)
    {
        if (zombie != null && !zombies.Contains(zombie)) zombies.Add(zombie);
    }
}
