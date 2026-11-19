using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Wave loop, spawn selection and spawning, the intensity director and atmosphere.
public sealed partial class ElderboomGame
{
    // Zombies alive at once, by difficulty: EASY 40 / NORMAL 70 / HARD 100, +10 per extra player, at most 130.
    // Per-difficulty value: EASY / NORMAL / HARD / INFERNO.
    T ByDifficulty<T>(T easy, T normal, T hard, T inferno) => selectedDifficulty == "EASY" ? easy : selectedDifficulty == "HARD" ? hard : selectedDifficulty == "INFERNO" ? inferno : normal;
    bool Inferno => selectedDifficulty == "INFERNO";
    // Zombies alive at once: EASY 40 / NORMAL 70 / HARD 100 / INFERNO 130, +10 per extra player (cap 130, INFERNO 160).
    public int MaxActiveZombies => Mathf.Min(ByDifficulty(40, 70, 100, 130) + 10 * (TeamSize - 1), Inferno ? 160 : 130);
    float HordeScale => ByDifficulty(.75f, 1f, 1.35f, 1.8f);
    // Difficulty grows gradually, wave by wave (no steps): 0 at wave 1, 1 by wave 10 for the early ramps.
    static float Ramp(int wave, int fullAt = 10) => Mathf.Clamp01((wave - 1) / (float)(fullAt - 1));

    public bool CanSpawn(int limit)
    {
        // Permanent blindness frees pressure capacity even while the last spawn
        // packets are pending; otherwise a full blind packet deadlocks wave 3+.
        int world=0, pressure=0;
        foreach(var zombie in zombies) if(zombie != null && zombie.IsAlive && zombie.gameObject.activeInHierarchy)
        { world++; if(zombie.CountsForWave && !zombie.PlasmaBlinded)pressure++; }
        return pressure < limit && world < MaxActiveZombies + 8;
    }

    public void ReportPlayerDamage(float amount) { intensity = Mathf.Min(2f, intensity + amount / 35f); }

    // Extra zombie from a scripted trigger; it does not count toward the wave.
    public void SpawnScripted(Transform marker)
    {
        if (state != State.Playing || marker == null) return;
        int before = zombies.Count;
        if (SpawnZombie(marker, Mathf.Max(1, wave)) && zombies.Count > before) zombies[zombies.Count - 1].CountsForWave = false;
    }

    public void ReportNoise(Vector3 position, float radius)
    {
        float r2 = radius * radius;
        foreach (var zombie in zombies) if (zombie != null && zombie.IsAlive && (zombie.transform.position - position).sqrMagnitude <= r2) zombie.HearNoise(position);
    }

    // Stuck or off-mesh zombies leave the wave without a reward so it can still end.
    public void RetireStuck(ElderboomZombie zombie)
    {
        if (zombie == null || zombie.Registered) return;
        zombie.Registered = true;
        if (zombie.CountsForWave) waveRemaining = Mathf.Max(0, waveRemaining - 1);
        zombie.gameObject.SetActive(false); Destroy(zombie.gameObject);
    }

    // Night ambience between fights; the music rises with nearby pressure and damage taken.
    void UpdateAtmosphere()
    {
        intensity = Mathf.MoveTowards(intensity, 0f, Time.deltaTime * .07f);
        popups.RemoveAll(p => Time.time - p.at > 1.2f);
        if (Time.unscaledTime >= musicCheckAt) { musicCheckAt = Time.unscaledTime + 1f; musicSetting = Mathf.Clamp01(PlayerPrefs.GetFloat("hdrp.settings.music_volume", .8f)); }
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>(); musicSource.clip = Resources.Load<AudioClip>("Elderboom/UI/haunted_corridor");
            ambienceSource = gameObject.AddComponent<AudioSource>(); ambienceSource.clip = ElderboomAudio.Clip("footsteps/cricket_ambience");
            foreach (var source in new[] { musicSource, ambienceSource })
            { source.loop = true; source.volume = 0f; source.spatialBlend = 0f; source.playOnAwake = false; if (source.clip != null) source.Play(); }
        }
        int near = 0;
        if (player != null)
            foreach (var zombie in zombies)
                if (zombie != null && zombie.IsAlive && !zombie.PlasmaBlinded && (zombie.transform.position - player.transform.position).sqrMagnitude < 400f) near++;
        bool playing = state == State.Playing;
        float combat = playing ? Mathf.Clamp01(near / 8f + intensity * .5f) : 0f;
        float k = 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime);
        musicSource.volume = Mathf.Lerp(musicSource.volume, playing && !ElderboomBossHud.MusicOn ? musicSetting * Mathf.Lerp(.06f, .32f, combat) : 0f, k);
        ambienceSource.volume = Mathf.Lerp(ambienceSource.volume, playing ? musicSetting * .22f * (1f - combat * .7f) : 0f, k);
    }

    public void RetireBlindSurvivors()
    {
        foreach(var zombie in zombies)
        {
            if(zombie == null || !zombie.IsAlive || !zombie.PlasmaBlinded || zombie.CountsForWave || zombie.RetiredAt<0 || Time.time-zombie.RetiredAt<20f)continue;
            if(player != null && Vector3.Distance(zombie.transform.position,player.transform.position)<24f)continue;
            var point = playerCamera != null ? playerCamera.WorldToViewportPoint(zombie.transform.position+Vector3.up) : new Vector3(-1,-1,-1);
            if(point.z>0 && point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1)continue;
            zombie.gameObject.SetActive(false); Destroy(zombie.gameObject);
        }
    }

    IEnumerator StartGame()
    {
        state = State.Playing;
        selectedAbility = 0;
        credits = 1000;
        wave = 0;
        ConfigureLoadout();
        player.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        ShowNotice("ELDERBOOM VILLAGE // SURVIVE THE NIGHT");
        yield return new WaitForSeconds(2f);
        if (ElderboomTestRange.Active) { ShowNotice("TEST RANGE // " + selectedOperator + " // 7 TACTICAL  8 ELI  9 SOPH", 8f); yield break; }
        if (ElderboomLan.IsClient) yield break; // LAN client: the host runs the waves
        while (state == State.Playing && player != null && !TeamDown)
        {
            wave++;
            yield return StartCoroutine(SpawnWave(wave));
            if (state != State.Playing || TeamDown) break;
            wavesCleared++;
            ElderboomEvents.RaiseWaveCleared(wave);
            int pause = Inferno ? 8 : wave < 6 ? 15 : 12; // more room to regroup and spend credits early on (INFERNO: short breaks)
            ShowNotice("WAVE " + wave + " CLEAR // NEXT WAVE IN " + pause);
            yield return new WaitForSeconds(pause);
        }
    }

    IEnumerator SpawnWave(int number)
    {
        float multiplier = HordeScale;
        // Bigger hordes with more players: +50% per extra player (multiplayer host).
        float team = 1f + .5f * (TeamSize - 1);
        // Endless, slow growth (NORMAL, one player): 6 at wave 1, ~15 at 5, ~29 at 10, ~60 at 20,
        // ~100 at 30, ~200 at 50, ~560 at 100. No ceiling on the wave size: only the number
        // alive at once is capped (performance), so late waves last longer instead of piling up.
        int w0 = number - 1;
        int count = Mathf.Max(5, Mathf.RoundToInt((6f + 2.2f * w0 + .035f * w0 * w0) * multiplier * team));
        // Boss wave: the boss and half the usual horde as his escort.
        bool bossWave = IsBossWave(number);
        if (bossWave) count = Mathf.Max(6, count / 2);
        int packet = Mathf.Clamp(3 + number / 3, 3, 12);
        int maxActive = Mathf.Min(MaxActiveZombies, Mathf.RoundToInt((6f + 1.3f * w0) * multiplier * team));
        float spacing = Mathf.Max(.35f, 1.25f - number * .03f) * ByDifficulty(1.2f, 1f, .75f, .55f);
        waveRemaining = count + (bossWave ? 1 : 0);
        spawning = true;
        recentSpawns.Clear();
        ShowNotice(bossWave ? "WAVE " + number + " // BOSS: " + ElderboomZombie.BossName : "WAVE " + number + " // " + count + " HOSTILES", bossWave ? 6f : 4f);
        if (bossWave) StartCoroutine(SpawnBoss(number));
        waveBannerAt = Time.time;
        ElderboomEvents.RaiseWaveStarted(number);
        if (player != null) ElderboomAudio.Play("zombies/zombie-feral-snarl", player.transform.position, .35f, false, .85f);

        for (int index = 0; index < count && state == State.Playing; index++)
        {
            if (index > 0) yield return new WaitForSeconds(index % packet == 0 ? Mathf.Max(2.5f, 7f - number * .15f) : spacing);
            // Director: after a burst of damage, hold new spawns briefly so pressure comes in waves.
            float relaxStart = Time.time;
            while (intensity > .9f && Time.time - relaxStart < 6f && state == State.Playing) yield return new WaitForSeconds(.25f);
            while (!CanSpawn(maxActive) && state == State.Playing) yield return new WaitForSeconds(.2f);
            var spawn=SelectSpawn(index % packet == 0 ? "pressure" : index / packet % 2 == 1 ? "flank" : "opening");
            float waitSince=Time.time, fallbackAt=Time.time+3f;
            while(spawn==null && state==State.Playing && Time.time-waitSince<20f)
            {
                yield return new WaitForSeconds(.35f);
                spawn=SelectSpawn("flank");
                if(spawn==null && Time.time>=fallbackAt){spawn=FindHiddenFallbackSpawn();fallbackAt=Time.time+1f;}
            }
            // Never materialize a zombie in view to rescue a wave. An impossible spawn
            // expires without points; the remaining enemies can still finish the wave.
            if(spawn==null && state==State.Playing)Debug.LogWarning("Elderboom: skipped a spawn after 20s without a hidden reachable position.");
            if(state==State.Playing && !SpawnZombie(spawn, number))waveRemaining=Mathf.Max(0,waveRemaining-1);
        }
        spawning = false;
        float blindSince = -1f;
        while (state == State.Playing && waveRemaining > 0)
        {
            bool allBlind = false;
            foreach (var zombie in zombies) if (zombie != null && zombie.IsAlive && zombie.CountsForWave)
            { allBlind=true; if(!zombie.PlasmaBlinded){allBlind=false;break;} }
            if (allBlind)
            {
                if (blindSince < 0f) blindSince = Time.time;
                if (Time.time - blindSince >= 8f)
                {
                    foreach (var zombie in zombies) if (zombie != null && zombie.IsAlive && zombie.CountsForWave) zombie.RetireFromWave();
                    waveRemaining = 0;
                }
            }
            else blindSince = -1f;
            yield return null;
        }
    }

    // ---- boss waves ----
    public static bool IsBossWave(int number) => number > 0 && number % 10 == 0;

    IEnumerator SpawnBoss(int number)
    {
        yield return new WaitForSeconds(5f);
        Transform spawn = null; float since = Time.time;
        while (spawn == null && state == State.Playing && Time.time - since < 25f)
        {
            spawn = SelectSpawn("pressure");
            if (spawn == null) { yield return new WaitForSeconds(.5f); spawn = FindHiddenFallbackSpawn(); }
        }
        if (state != State.Playing) yield break;
        int before = zombies.Count;
        if (spawn == null || !SpawnZombie(spawn, number, true) || zombies.Count == before) { waveRemaining = Mathf.Max(0, waveRemaining - 1); yield break; }
        var boss = zombies[zombies.Count - 1];
        ElderboomBossHud.Show(boss);
        if (player != null) ElderboomAudio.Play("boss/boss_roar", player.transform.position, .9f, false);
        ShowNotice(ElderboomZombie.BossName + " // AIM FOR THE GLOWING BELLY", 5f);
    }

    // Guaranteed reward: credits for the team and a special weapon where he fell.
    void BossDefeated(ElderboomZombie boss)
    {
        const int reward = 2000;
        credits += reward; totalScore += reward;
        ShowNotice("BOSS DEFEATED // +" + reward + " CREDITS", 5f);
        var ids = ElderboomSpecialPickup.DropIds;
        ElderboomSpecialPickup.Spawn(boss.transform.position, ElderboomWeapons.Find(ids[UnityEngine.Random.Range(0, ids.Length)]));
    }

    Transform SelectSpawn(string phase)
    {
        if (spawnPoints.Count == 0) return null;
        Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = playerCamera != null ? playerCamera.transform.forward : Vector3.forward;
        // Multiplayer: spawns take turns around every living player, not only the host.
        var focus = SpawnFocus();
        if (focus != null) { playerPosition = focus.position; forward = focus.forward; }
        // Path from the nearest NavMesh point: the player may be mid-jump or on a prop.
        if (NavMesh.SamplePosition(playerPosition, out var playerNav, 3f, NavMesh.AllAreas)) playerPosition = playerNav.position;
        Transform best = null;
        float bestScore = float.NegativeInfinity;
        foreach (var point in spawnPoints)
        {
            if (point == null) continue;
            if (!ElderboomArea.IsSpawnAllowed(point.GetComponent<ElderboomSpawnPoint>())) continue;
            Vector3 offset = point.position - playerPosition; offset.y = 0;
            if(!NavMesh.CalculatePath(point.position,playerPosition,NavMesh.AllAreas,spawnPath) || spawnPath.status!=NavMeshPathStatus.PathComplete)continue;
            bool occupied=false;
            foreach(var zombie in zombies)if(zombie != null && zombie.IsAlive && (zombie.transform.position-point.position).sqrMagnitude<1.4f*1.4f){occupied=true;break;}
            if(occupied)continue;
            float distance = offset.magnitude;
            if (distance < 14f) continue;
            if (!HiddenFromTeam(point.position)) continue;
            float score = -Mathf.Abs(distance - 27f) * .12f + 3f;
            float facing = Vector3.Dot(forward.normalized, offset.normalized);
            score += phase == "flank" ? -Mathf.Abs(facing) * 2f : facing * .7f;
            foreach (var recent in recentSpawns) if ((recent - point.position).sqrMagnitude < 9f) score -= 5f;
            if (score > bestScore) { best = point; bestScore = score; }
        }
        return best;
    }

    readonly RaycastHit[] spawnSightHits = new RaycastHit[32];
    Transform fallbackSpawn;

    bool HiddenFromTeam(Vector3 at)
    {
        if(player!=null && !player.IsDead && !HiddenFromObserver(at,player.transform.position,
            playerCamera!=null?playerCamera.transform.position:player.transform.position+Vector3.up*1.6f,
            playerCamera!=null?playerCamera.transform.forward:player.transform.forward))return false;
        if(ElderboomLan.IsHost)
            foreach(var avatar in ElderboomLanAvatar.All)
                if(avatar!=null && !avatar.IsDead && !HiddenFromObserver(at,avatar.transform.position,avatar.transform.position+Vector3.up*1.6f,avatar.transform.forward))return false;
        return true;
    }

    bool HiddenFromObserver(Vector3 at,Vector3 feet,Vector3 eye,Vector3 forward)
    {
        var delta=at-feet;delta.y=0;
        if(delta.sqrMagnitude<14f*14f)return false;
        // Use a generous forward hemisphere, including the periphery of wide-FOV cameras.
        forward.y=0;
        if(Vector3.Dot(forward.normalized,delta.normalized)<-.1f)return true;
        var side=Vector3.Cross(Vector3.up,delta.normalized)*.4f;
        return SpawnOccluded(eye,at+Vector3.up*.5f) && SpawnOccluded(eye,at+Vector3.up*1.8f)
            && SpawnOccluded(eye,at+Vector3.up+side) && SpawnOccluded(eye,at+Vector3.up-side);
    }

    bool SpawnOccluded(Vector3 eye,Vector3 target)
    {
        var delta=target-eye;
        // Invisible boundary boxes and transient actors must not count as visual cover.
        int n=Physics.RaycastNonAlloc(eye,delta.normalized,spawnSightHits,delta.magnitude,ElderboomCombat.WorldMask & ~(1<<2),QueryTriggerInteraction.Ignore);
        if(n==spawnSightHits.Length)return false;
        for(int i=0;i<n;i++)
        {
            var c=spawnSightHits[i].collider;
            if(c.GetComponentInParent<HDRPSlicePlayer>()!=null || c.GetComponentInParent<ElderboomZombie>()!=null || c.GetComponentInParent<ElderboomLanAvatar>()!=null)continue;
            return true;
        }
        return false;
    }

    Transform FindHiddenFallbackSpawn()
    {
        if(player==null || spawnPoints.Count==0)return null;
        var focus=SpawnFocus();var target=focus!=null?focus.position:player.transform.position;
        if(!NavMesh.SamplePosition(target,out var targetNav,3f,NavMesh.AllAreas))return null;
        // Bounded search around authored markers, never outside an unlocked area.
        for(int i=0;i<32;i++)
        {
            var anchor=spawnPoints[UnityEngine.Random.Range(0,spawnPoints.Count)];
            if(anchor==null || !ElderboomArea.IsSpawnAllowed(anchor.GetComponent<ElderboomSpawnPoint>()))continue;
            var offset=UnityEngine.Random.insideUnitCircle*12f;
            if(!NavMesh.SamplePosition(anchor.position+new Vector3(offset.x,0,offset.y),out var hit,1.5f,NavMesh.AllAreas))continue;
            var at=hit.position;var area=ElderboomArea.At(at);
            if(area==null || !area.Unlocked || !HiddenFromTeam(at))continue;
            if(Physics.CheckCapsule(at+Vector3.up*.5f,at+Vector3.up*1.4f,.4f,ElderboomCombat.WorldMask,QueryTriggerInteraction.Ignore))continue;
            if(!NavMesh.CalculatePath(at,targetNav.position,NavMesh.AllAreas,spawnPath) || spawnPath.status!=NavMeshPathStatus.PathComplete)continue;
            if(fallbackSpawn==null){fallbackSpawn=new GameObject("Hidden_Spawn_Fallback").transform;fallbackSpawn.SetParent(transform);}
            fallbackSpawn.position=at;return fallbackSpawn;
        }
        return null;
    }

    // Players in the match (1 offline / on clients, host + connected players on the host).
    int TeamSize => ElderboomLan.IsHost && ElderboomLan.Instance != null ? Mathf.Max(1, ElderboomLan.Instance.Players) : 1;
    int spawnFocusTurn;
    // A remote player's avatar to spawn around this time (null = the host's own player).
    Transform SpawnFocus()
    {
        if (!ElderboomLan.IsHost || ElderboomLanAvatar.All.Count == 0) return null;
        var alive = new List<Transform>();
        if (player != null && !player.IsDead) alive.Add(null);
        foreach (var a in ElderboomLanAvatar.All) if (a != null && !a.IsDead) alive.Add(a.transform);
        if (alive.Count == 0) return null;
        return alive[spawnFocusTurn++ % alive.Count];
    }

    bool SpawnZombie(Transform marker, int number, bool boss = false)
    {
        if (marker == null) return false;
        // The rural set (all villagers of the valley); the old prefabs only if the set is missing.
        int netModel;
        var prefab = boss ? ElderboomZombieModels.Boss(out netModel) : ElderboomZombieModels.Pick(out netModel);
        var keys = new[] { "Elderboom/Zombie", "Elderboom/ZombieFemale" };
        if (prefab == null) prefab = Resources.Load<GameObject>(keys[UnityEngine.Random.Range(0, keys.Length)]);
        if (prefab == null)
        {
            foreach (var key in keys)
            {
                prefab = Resources.Load<GameObject>(key);
                if (prefab != null) break;
            }
        }
        if (prefab == null)
        {
            Debug.LogError("Elderboom zombie prefab missing from Resources/Elderboom.");
            return false;
        }
        var root = Instantiate(prefab, marker.position, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
        root.transform.localScale *= ElderboomZombie.BodyScale;
        root.name = "Zombie_W" + number;
        var zombie = root.GetComponent<ElderboomZombie>();
        float healthBase = ByDifficulty(3f, 4f, 6f, 8f);
        float damageBase = ByDifficulty(7f, 10f, 15f, 20f);
        // Slow continuous growth with no ceiling: x1.2 health by wave 5, x1.5 by 10, x2.2 by 20, x3.8 by 40, x6.7 by 70.
        // Damage rises gently and stops at x1.8 (wave ~33) so late waves are about numbers, not one-shots.
        int w = number - 1;
        float health = healthBase * 1.5f * (1f + .045f * w + .0006f * w * w), hit = damageBase * Mathf.Min(1.8f, 1f + .025f * w);
        float speed = ByDifficulty(1.9f, 2.5f, 3f, 3.4f);
        float interval = ByDifficulty(1.35f, 1f, .75f, .6f);
        // Variety: runners from wave 3, brutes from wave 5.
        string kind = "walker"; float roll = UnityEngine.Random.value;
        var profile = root.GetComponent<ElderboomZombieProfile>();
        if (profile != null) { health *= profile.health; hit *= profile.damage; speed *= profile.speed; interval *= profile.attackInterval; }
        if (profile != null && profile.scale > .01f && Mathf.Abs(profile.scale - 1f) > .001f) root.transform.localScale *= profile.scale;
        // Early waves are slower and hit less often; runners from wave 4, brutes from wave 8, both ramping in slowly.
        if (!Inferno) { speed *= Mathf.Lerp(.8f, 1f, Ramp(number)); interval *= Mathf.Lerp(1.25f, 1f, Ramp(number)); }
        int vw = Inferno ? number + 3 : number; // INFERNO: runners and brutes three waves earlier
        if (boss) { kind = "boss"; health *= 50f * (1f + .75f * (TeamSize - 1)); hit *= 1.5f; speed = Mathf.Max(1.7f, speed * .85f); interval *= 1.2f; }
        else if (vw >= 8 && !(profile != null && profile.noBrute) && roll < Mathf.Min(Inferno ? .18f : .12f, .006f * (vw - 7))) { kind = "brute"; health *= 3.2f; hit *= 1.7f; speed *= .72f; interval *= 1.25f; }
        else if (vw >= 4 && !(profile != null && profile.noRunner) && roll < Mathf.Min(Inferno ? .4f : .3f, .018f * (vw - 3))) { kind = "runner"; health *= .65f; speed *= 1.85f; interval *= .9f; }
        zombie.Initialize(this, player, health, hit, speed, interval);
        zombie.SetVariant(kind);
        if (boss) zombie.MakeBoss();
        zombie.NetFemale = prefab.name.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0;
        zombie.NetModel = netModel;
        AssignZombieSkill(zombie, kind, number);
        zombies.Add(zombie);
        recentSpawns.Add(marker.position);
        if (recentSpawns.Count > 3) recentSpawns.RemoveAt(0);
        return true;
    }

    // Most of the horde stays plain: only a minority gets a skill, with a cap on how many are alive at once.
    // Ambush from wave 2, grab from wave 1. Armed zombies (the cook's cleaver) never get a second skill.
    void AssignZombieSkill(ElderboomZombie zombie, string kind, int number)
    {
        if (zombie.GetComponent<ElderboomZombieWeapon>() != null || kind == "brute" || kind == "boss") return;
        float ambushChance = ByDifficulty(.06f, .10f, .14f, .20f);
        float grabChance = ByDifficulty(.08f, .12f, .16f, .22f);
        int maxAmbush = ByDifficulty(2, 4, 6, 9);
        int maxGrab = ByDifficulty(3, 5, 8, 12);
        int ambushers = 0, grabbers = 0;
        foreach (var z in zombies) if (z != null && z.IsAlive) { if (z.Skill == ElderboomZombieSkill.Ambush) ambushers++; else if (z.Skill == ElderboomZombieSkill.Grab) grabbers++; }
        // Skills come in gradually: grab from wave 3, ambush from wave 4, full chance by wave 14.
        float skillRamp = Ramp(number - 2, 12); ambushChance *= skillRamp; grabChance *= skillRamp;
        float roll = UnityEngine.Random.value;
        if (number >= 4 && kind != "runner" && roll < ambushChance && ambushers < maxAmbush) zombie.AssignSkill(ElderboomZombieSkill.Ambush);
        else if (number >= 3 && roll >= ambushChance && roll < ambushChance + grabChance && grabbers < maxGrab) zombie.AssignSkill(ElderboomZombieSkill.Grab);
    }
}
