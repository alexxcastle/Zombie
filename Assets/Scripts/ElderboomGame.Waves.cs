using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Wave loop, spawn selection and spawning, the intensity director and atmosphere.
public sealed partial class ElderboomGame
{
    // Zombies alive at once, by difficulty: EASY 40 / NORMAL 70 / HARD 100, +10 per extra player, at most 130.
    public int MaxActiveZombies => Mathf.Min((selectedDifficulty == "EASY" ? 40 : selectedDifficulty == "HARD" ? 100 : 70) + 10 * (TeamSize - 1), 130);
    float HordeScale => selectedDifficulty == "EASY" ? .8f : selectedDifficulty == "HARD" ? 1.6f : 1.1f;

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
        musicSource.volume = Mathf.Lerp(musicSource.volume, playing ? musicSetting * Mathf.Lerp(.06f, .32f, combat) : 0f, k);
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
        if (ElderboomLan.IsClient) yield break; // LAN client: the host runs the waves
        while (state == State.Playing && player != null && !TeamDown)
        {
            wave++;
            yield return StartCoroutine(SpawnWave(wave));
            if (state != State.Playing || TeamDown) break;
            wavesCleared++;
            ElderboomEvents.RaiseWaveCleared(wave);
            ShowNotice("WAVE " + wave + " CLEAR // NEXT WAVE IN 10");
            yield return new WaitForSeconds(10f);
        }
    }

    IEnumerator SpawnWave(int number)
    {
        float multiplier = HordeScale;
        // Bigger hordes with more players: +50% per extra player (multiplayer host).
        float team = 1f + .5f * (TeamSize - 1);
        int count = Mathf.Max(4, Mathf.RoundToInt((10 + number * 6) * multiplier * team));
        int packet = Mathf.Clamp(3 + number, 4, 10);
        int maxActive = Mathf.Min(MaxActiveZombies, Mathf.RoundToInt((16 + number * 6) * multiplier * team));
        float spacing = Mathf.Max(.18f, .7f - number * .06f) * (selectedDifficulty == "EASY" ? 1.2f : selectedDifficulty == "HARD" ? .65f : 1f);
        waveRemaining = count;
        spawning = true;
        recentSpawns.Clear();
        ShowNotice("WAVE " + number + " // " + count + " HOSTILES");
        waveBannerAt = Time.time;
        ElderboomEvents.RaiseWaveStarted(number);
        if (player != null) ElderboomAudio.Play("zombies/zombie-feral-snarl", player.transform.position, .35f, false, .85f);

        for (int index = 0; index < count && state == State.Playing; index++)
        {
            if (index > 0) yield return new WaitForSeconds(index % packet == 0 ? Mathf.Max(1.5f, 4f - number * .3f) : spacing);
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

    bool SpawnZombie(Transform marker, int number)
    {
        if (marker == null) return false;
        // The rural set (all villagers of the valley); the old prefabs only if the set is missing.
        var prefab = ElderboomZombieModels.Pick(out int netModel);
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
        root.name = "Zombie_W" + number;
        var zombie = root.GetComponent<ElderboomZombie>();
        float healthBase = selectedDifficulty == "EASY" ? 3 : selectedDifficulty == "HARD" ? 6 : 4;
        float damageBase = selectedDifficulty == "EASY" ? 7 : selectedDifficulty == "HARD" ? 15 : 10;
        int level = Mathf.Clamp(1 + (number - 1) / 3, 1, 10);
        float health = healthBase * 1.5f * (1f + .3f * (level - 1)), hit = damageBase * (1f + .15f * (level - 1));
        float speed = selectedDifficulty == "EASY" ? 1.9f : selectedDifficulty == "HARD" ? 3f : 2.5f;
        float interval = selectedDifficulty == "EASY" ? 1.35f : selectedDifficulty == "HARD" ? .75f : 1f;
        // Variety: runners from wave 3, brutes from wave 5.
        string kind = "walker"; float roll = UnityEngine.Random.value;
        if (number >= 5 && roll < Mathf.Min(.12f, .02f * (number - 3))) { kind = "brute"; health *= 3.2f; hit *= 1.7f; speed *= .72f; interval *= 1.25f; }
        else if (number >= 3 && roll < Mathf.Min(.35f, .08f * (number - 1))) { kind = "runner"; health *= .65f; speed *= 1.85f; interval *= .9f; }
        zombie.Initialize(this, player, health, hit, speed, interval);
        zombie.SetVariant(kind);
        zombie.NetFemale = prefab.name.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0;
        zombie.NetModel = netModel;
        zombies.Add(zombie);
        recentSpawns.Add(marker.position);
        if (recentSpawns.Count > 3) recentSpawns.RemoveAt(0);
        return true;
    }
}
