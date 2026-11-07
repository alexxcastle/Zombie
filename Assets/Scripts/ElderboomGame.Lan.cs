using UnityEngine;

// LAN co-op hooks (see ElderboomLan): the host keeps the waves going while any player is alive;
// clients mirror the host's wave counter and receive their own kill rewards.
public sealed partial class ElderboomGame
{
    // Down-but-revivable players are not out yet, unless nobody is left standing to revive them.
    bool TeamDown => player == null
        || (player.IsDead && !player.Downed && !(ElderboomLan.IsHost && ElderboomLan.AnyRemoteAlive))
        || (player.Downed && !ElderboomLan.AnyRemoteAlive);

    // Co-op revive: stand next to a downed teammate and hold interact for 3 s.
    const float ReviveHold = 3f;
    ElderboomLanAvatar reviving; float reviveProgress;
    public float ReviveProgress => reviving != null ? reviveProgress / ReviveHold : 0f;
    void TickRevive()
    {
        if (state != State.Playing || player == null || player.IsDead || !ElderboomLan.CoopActive) { reviving = null; reviveProgress = 0; return; }
        ElderboomLanAvatar near = null; float best = 2.4f;
        foreach (var a in ElderboomLanAvatar.All)
        {
            if (a == null || !a.Downed) continue;
            float d = Vector3.Distance(a.transform.position, player.transform.position); if (d < best) { best = d; near = a; }
        }
        if (near == null) { reviving = null; reviveProgress = 0; return; }
        bool holding = ElderboomInput.Held("interact");
        string label = player.ActionLabel("interact") + " (HOLD) · REVIVE P" + (near.Id + 1);
        if (holding) label += "  " + Mathf.RoundToInt(reviveProgress / ReviveHold * 100f) + "%";
        ElderboomPrompt.Offer(player.transform.position, near.transform.position + Vector3.up * .4f, label);
        if (!holding || near != reviving) { reviving = near; reviveProgress = holding ? reviveProgress : 0f; if (!holding) return; }
        reviveProgress += Time.deltaTime;
        if (reviveProgress >= ReviveHold) { ElderboomLan.SendRevive(near.Id); ShowNotice("P" + (near.Id + 1) + " REVIVED"); reviving = null; reviveProgress = 0; }
    }
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
