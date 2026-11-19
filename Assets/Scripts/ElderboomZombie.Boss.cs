using System.Collections;
using UnityEngine;

// The boss (ElderboomGame.Waves: every 10th wave). The obese farmer, much bigger, with two ranged attacks the
// player can read and dodge, a weak point and a second phase:
//  Rock throw - he tears a rock from the ground (groan, the rock in his hand), then lobs it at where you are going.
//               A direct hit hurts, the impact splashes a little.
//  Acid vomit - gurgling first, then a spray of acid blobs in a cone; each leaves a puddle that burns for seconds.
//  Weak point - the swollen, glowing pustule on his belly takes 2.5x damage.
//  Rage       - under half health he roars, moves faster and attacks more often (two rocks in a row).
// Runs on the host; projectiles damage LAN players through ElderboomLan.DamageAvatar.
public sealed partial class ElderboomZombie
{
    public bool IsBoss { get; private set; }
    public float MaxHealth { get; private set; }
    public bool BossEnraged => bossRage;
    public const string BossName = "THE BLOATED FARMER";

    bool bossRage, bossBusy;
    float nextThrowAt, nextVomitAt;
    Transform pustule;
    Material pustuleMat;
    Vector3 lastTargetPos; float lastTargetAt;
    Vector3 targetVelocity;

    public void MakeBoss()
    {
        IsBoss = true; MaxHealth = health;
        pushResist = .12f;
        // A wide body must still pass the village lanes: keep the agent radius near a normal zombie's.
        if (agent != null) { float s = Mathf.Max(.01f, transform.lossyScale.x); agent.radius = .55f / s; agent.height = 1.8f; agent.stoppingDistance = 1.6f / s; }
        nextThrowAt = Time.time + 4f; nextVomitAt = Time.time + 7f;
        BuildPustule();
    }

    void BuildPustule()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "Boss_WeakPoint";
        Object.Destroy(go.GetComponent<Collider>());
        Transform parent = transform;
        if (animator != null && animator.isHuman) { var spine = animator.GetBoneTransform(HumanBodyBones.Spine); if (spine != null) parent = spine; }
        go.transform.SetParent(parent, false);
        // On the belly, in front: placed in the root's space, then kept on the spine bone so it follows the body.
        go.transform.position = transform.TransformPoint(new Vector3(0f, 1.0f, .27f));
        go.transform.localScale = Vector3.one * (.3f / Mathf.Max(.01f, parent.lossyScale.x));
        pustuleMat = new Material(Shader.Find("HDRP/Lit"));
        pustuleMat.SetColor("_BaseColor", new Color(.45f, .7f, .12f)); pustuleMat.SetFloat("_Smoothness", .85f);
        pustuleMat.EnableKeyword("_EMISSIVE_COLOR_MAP");
        go.GetComponent<Renderer>().sharedMaterial = pustuleMat;
        go.layer = gameObject.layer;
        pustule = go.transform;
    }

    // Weak point and the rage threshold; returns the damage to apply.
    float BossDamage(float amount, Vector3 hitPoint)
    {
        if (!IsBoss) return amount;
        if (pustule != null && (hitPoint - pustule.position).sqrMagnitude < Mathf.Pow(.32f * transform.lossyScale.x, 2f))
        {
            amount *= 2.5f;
            if (Time.time >= hitSoundAt - .3f) ElderboomAudio.Play("boss/weakpoint_hit", hitPoint, .9f, true, Random.Range(.9f, 1.1f));
        }
        if (!bossRage && health - amount <= MaxHealth * .5f && health - amount > 0f) StartCoroutine(BossRage());
        return amount;
    }

    IEnumerator BossRage()
    {
        bossRage = true;
        moveSpeed *= 1.3f; attackInterval *= .8f;
        bossBusy = true; CancelMelee();
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        SetMotion("AttackA");
        ElderboomAudio.Play("boss/rage", transform.position + Vector3.up * 2f, 1f, true, 1f, 0f, 6f, 80f);
        game?.ShowNotice(BossName + " IS ENRAGED", 3f);
        yield return new WaitForSeconds(1.6f);
        bossBusy = false;
        if (!dead) SetMotion(Locomotion());
    }

    // Called every frame before the normal chase; true while the boss is busy with a ranged attack.
    bool BossTick(float distance)
    {
        if (!IsBoss) return false;
        TrackTarget();
        if (pustuleMat != null)
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.time * (bossRage ? 9f : 4.5f));
            pustuleMat.SetColor("_EmissiveColor", new Color(.5f, 1f, .12f) * Mathf.Lerp(2f, bossRage ? 30f : 12f, pulse));
        }
        if (bossBusy) { if (agent != null) agent.isStopped = true; return true; }
        if (lifted || IsFrozen || hitReacting || melee || TargetIgnored || TargetDown) return false;
        bool sight = HasLineOfSight();
        if (Time.time >= nextVomitAt && distance > 2.5f && distance < 9f && sight) { StartCoroutine(Vomit()); return true; }
        if (Time.time >= nextThrowAt && distance > 6f && distance < 34f && sight) { StartCoroutine(ThrowRocks(bossRage ? 2 : 1)); return true; }
        return false;
    }

    void TrackTarget()
    {
        if (TargetDown) return;
        var p = TargetPos; float now = Time.time;
        if (lastTargetAt > 0f && now - lastTargetAt > .05f) { targetVelocity = Vector3.Lerp(targetVelocity, (p - lastTargetPos) / (now - lastTargetAt), .5f); lastTargetPos = p; lastTargetAt = now; }
        else if (lastTargetAt <= 0f) { lastTargetPos = p; lastTargetAt = now; }
    }

    Transform Hand()
    {
        if (animator != null && animator.isHuman) { var h = animator.GetBoneTransform(HumanBodyBones.RightHand); if (h != null) return h; }
        return null;
    }

    Vector3 HandPoint() { var h = Hand(); return h != null ? h.position : transform.TransformPoint(new Vector3(.45f, 1.9f, .2f)); }

    void BossHold()
    {
        CancelMelee(); bossBusy = true;
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); agent.velocity = Vector3.zero; }
    }

    IEnumerator ThrowRocks(int count)
    {
        BossHold();
        for (int i = 0; i < count && !dead; i++)
        {
            // Tell: groan, the rock torn from the ground and raised in his hand.
            SetMotion("AttackA");
            if (animator != null) { animator.speed = .55f; WakeAnimation(1.6f); }
            ElderboomAudio.Play("boss/boss_windup", transform.position + Vector3.up * 1.5f, 1f, true, Random.Range(.92f, 1.05f), 0f, 4f, 60f);
            var rock = ElderboomBossRock.Create(HandPoint());
            float t = 0f, windup = bossRage ? .75f : 1.05f;
            while (t < windup && !dead && !IsFrozen)
            {
                t += Time.deltaTime;
                if (!TargetDown) Face(TargetPos, 8f);
                if (rock != null) rock.transform.position = HandPoint();
                yield return null;
            }
            if (dead || IsFrozen || rock == null || TargetDown) { if (rock != null) Object.Destroy(rock.gameObject); break; }
            if (animator != null) animator.speed = 1f;
            float distance = Vector3.Distance(transform.position, TargetPos);
            float flight = Mathf.Clamp(distance / 17f, .55f, 1.6f);
            // Leads a moving target a little (not perfectly: sidestepping still works).
            Vector3 aim = TargetPos + Vector3.ProjectOnPlane(targetVelocity, Vector3.up) * flight * .6f;
            rock.Launch(aim, flight, damage * 1.3f, damage * .5f, chaseAvatar);
            ElderboomAudio.Play("boss/rock_whoosh", rock.transform.position, .8f, true);
            yield return new WaitForSeconds(.35f);
        }
        nextThrowAt = Time.time + (bossRage ? Random.Range(4.5f, 6f) : Random.Range(6.5f, 8.5f));
        yield return new WaitForSeconds(.25f);
        bossBusy = false;
        if (!dead) SetMotion(Locomotion());
    }

    IEnumerator Vomit()
    {
        BossHold();
        SetMotion("AttackPunch");
        if (animator != null) { animator.speed = .5f; WakeAnimation(2f); }
        ElderboomAudio.Play("boss/vomit_windup", transform.position + Vector3.up * 1.8f, 1f, true, 1f, 0f, 4f, 50f);
        float t = 0f;
        while (t < .9f && !dead && !IsFrozen) { t += Time.deltaTime; if (!TargetDown) Face(TargetPos, 6f); yield return null; }
        if (!dead && !IsFrozen && !TargetDown)
        {
            if (animator != null) animator.speed = 1f;
            Vector3 mouth = transform.TransformPoint(new Vector3(0f, 1.62f, .32f));
            ElderboomAudio.Play("boss/vomit", mouth, 1f, true, 1f, 0f, 4f, 50f);
            int blobs = bossRage ? 9 : 6;
            Vector3 toward = Vector3.ProjectOnPlane(TargetPos - transform.position, Vector3.up);
            float reach = Mathf.Clamp(toward.magnitude, 3f, 9f); toward = toward.sqrMagnitude > .01f ? toward.normalized : transform.forward;
            for (int i = 0; i < blobs && !dead; i++)
            {
                float angle = Mathf.Lerp(-28f, 28f, blobs == 1 ? .5f : i / (float)(blobs - 1)) + Random.Range(-6f, 6f);
                float range = reach * Random.Range(.6f, 1.15f);
                Vector3 landing = transform.position + Quaternion.Euler(0f, angle, 0f) * toward * range;
                ElderboomAcid.Spit(mouth, landing, .45f + range * .05f, damage * .35f, damage * .2f, bossRage ? 10f : 8f);
                yield return new WaitForSeconds(.07f);
            }
        }
        nextVomitAt = Time.time + (bossRage ? Random.Range(7f, 9f) : Random.Range(10f, 13f));
        yield return new WaitForSeconds(.4f);
        bossBusy = false;
        if (!dead) SetMotion(Locomotion());
    }
}

// Damage helper for the boss's attacks: the local player and LAN players in a radius.
public static class ElderboomBossDamage
{
    static HDRPSlicePlayer local;
    public static void Area(Vector3 point, float radius, float amount, Vector3 from)
    {
        if (local == null) local = Object.FindFirstObjectByType<HDRPSlicePlayer>();
        float r2 = radius * radius;
        if (local != null && !local.IsDead && Flat(local.transform.position, point) <= r2) local.TakeDamage(amount, from);
        foreach (var a in ElderboomLanAvatar.All)
            if (a != null && !a.IsDead && Flat(a.transform.position, point) <= r2) ElderboomLan.DamageAvatar(a, amount, from);
    }
    // Horizontal distance, and the point must be roughly at body height.
    static float Flat(Vector3 body, Vector3 point)
    {
        float dy = point.y - body.y;
        if (dy < -1f || dy > 2.4f) return float.MaxValue;
        body.y = point.y = 0f;
        return (body - point).sqrMagnitude;
    }
}

// A thrown rock: a ballistic arc to the aim point; hits a player on the way or bursts on the ground.
public sealed class ElderboomBossRock : MonoBehaviour
{
    Vector3 from, to; float flight, height, t, direct, splash; bool flying, landed;
    Vector3 spin;
    static GameObject prefab; static bool looked;

    public static ElderboomBossRock Create(Vector3 at)
    {
        if (!looked) { looked = true; prefab = Resources.Load<GameObject>("Elderboom/Boss/BossRock"); }
        GameObject go;
        if (prefab != null) go = Instantiate(prefab);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(.45f, .32f, .4f);
        }
        go.name = "Boss_Rock"; go.transform.position = at;
        go.transform.localScale *= 1.35f;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        return go.AddComponent<ElderboomBossRock>();
    }

    public void Launch(Vector3 aim, float seconds, float directDamage, float splashDamage, ElderboomLanAvatar target)
    {
        from = transform.position; to = aim; flight = seconds; direct = directDamage; splash = splashDamage;
        height = 1.2f + Vector3.Distance(from, to) * .12f; t = 0f; flying = true;
        spin = Random.onUnitSphere * Random.Range(240f, 520f);
    }

    void Update()
    {
        if (!flying || landed) return;
        Vector3 prev = transform.position;
        t += Time.deltaTime;
        float u = Mathf.Clamp01(t / flight);
        Vector3 next = Vector3.Lerp(from, to, u) + Vector3.up * (4f * height * u * (1f - u));
        transform.Rotate(spin * Time.deltaTime, Space.World);
        // A player on the path: direct hit.
        if (HitsPlayer(next)) { Burst(next, true); return; }
        if (Physics.Linecast(prev, next, out var hit, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<ElderboomZombie>() == null)
        { Burst(hit.point, false); return; }
        transform.position = next;
        if (u >= 1f)
        {
            // Past the aim point (open ground below): fall the rest of the way.
            if (Physics.Raycast(next + Vector3.up, Vector3.down, out var g, 6f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)) Burst(g.point, false);
            else Burst(next, false);
        }
    }

    bool HitsPlayer(Vector3 p)
    {
        var local = Object.FindFirstObjectByType<HDRPSlicePlayer>();
        if (local != null && !local.IsDead && (local.transform.position + Vector3.up - p).sqrMagnitude < .75f * .75f) return true;
        foreach (var a in ElderboomLanAvatar.All) if (a != null && !a.IsDead && (a.transform.position + Vector3.up - p).sqrMagnitude < .75f * .75f) return true;
        return false;
    }

    void Burst(Vector3 at, bool directHit)
    {
        landed = true; transform.position = at + Vector3.up * .12f;
        ElderboomAudio.Play("boss/rock_impact", at, 1f, true, Random.Range(.92f, 1.06f), 0f, 3f, 55f);
        if (directHit) ElderboomBossDamage.Area(at, 1.1f, direct, from);
        else ElderboomBossDamage.Area(at, 2.2f, splash, from);
        Destroy(gameObject, 4f);
    }
}

// Acid: blobs arc out of the boss's mouth; each leaves a burning puddle.
public sealed class ElderboomAcid : MonoBehaviour
{
    static Material blobMat, puddleMat;
    Vector3 from, to; float flight, t, hit, burn, life; bool puddle;
    float nextTick, until; AudioSource sizzle; Light glow;

    static Material Mat(ref Material m, Color baseColor, Color emissive, float smooth)
    {
        if (m != null) return m;
        m = new Material(Shader.Find("HDRP/Lit")); m.SetColor("_BaseColor", baseColor); m.SetFloat("_Smoothness", smooth);
        m.EnableKeyword("_EMISSIVE_COLOR_MAP"); m.SetColor("_EmissiveColor", emissive);
        return m;
    }

    public static void Spit(Vector3 mouth, Vector3 landing, float seconds, float puddleDps, float hitDamage, float puddleLife)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(go.GetComponent<Collider>());
        go.name = "Boss_AcidBlob"; go.transform.position = mouth; go.transform.localScale = Vector3.one * Random.Range(.16f, .24f);
        go.GetComponent<Renderer>().sharedMaterial = Mat(ref blobMat, new Color(.4f, .75f, .1f), new Color(.6f, 1.6f, .15f) * 6f, .9f);
        var a = go.AddComponent<ElderboomAcid>();
        a.from = mouth; a.to = landing; a.flight = seconds; a.burn = puddleDps; a.hit = hitDamage; a.life = puddleLife;
    }

    void Update()
    {
        if (!puddle)
        {
            t += Time.deltaTime; float u = Mathf.Clamp01(t / flight);
            Vector3 next = Vector3.Lerp(from, to, u) + Vector3.up * (1.6f * u * (1f - u) * 2f);
            if (u >= 1f || Physics.Linecast(transform.position, next, out _, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)) { Land(next); return; }
            transform.position = next;
            return;
        }
        if (Time.time >= until)
        {
            float fade = Mathf.Clamp01(1f - (Time.time - until) / 1.2f);
            transform.localScale = new Vector3(3f * fade, .02f, 3f * fade);
            if (glow != null) glow.intensity *= .9f;
            if (sizzle != null) sizzle.volume *= .9f;
            if (fade <= 0f) { if (sizzle != null) Destroy(sizzle.gameObject); Destroy(gameObject); }
            return;
        }
        if (Time.time >= nextTick) { nextTick = Time.time + .5f; ElderboomBossDamage.Area(transform.position + Vector3.up * .5f, 1.5f, burn * .5f, transform.position); }
    }

    void Land(Vector3 at)
    {
        if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var g, 4f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)) at = g.point;
        ElderboomBossDamage.Area(at + Vector3.up * .5f, 1f, hit, from);
        // Several blobs landing close together make one puddle, not a stack.
        foreach (var other in FindObjectsByType<ElderboomAcid>(FindObjectsSortMode.None))
            if (other != this && other.puddle && Time.time < other.until && (other.transform.position - at).sqrMagnitude < 1.2f * 1.2f)
            { other.until = Mathf.Max(other.until, Time.time + life); Destroy(gameObject); return; }
        puddle = true; until = Time.time + life; nextTick = Time.time + .2f;
        transform.position = at + Vector3.up * .02f; transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        transform.localScale = new Vector3(3f, .02f, 3f);
        var mesh = GetComponent<MeshFilter>(); var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        if (mesh != null) mesh.sharedMesh = cylinder.GetComponent<MeshFilter>().sharedMesh; Destroy(cylinder);
        GetComponent<Renderer>().sharedMaterial = Mat(ref puddleMat, new Color(.32f, .55f, .06f), new Color(.5f, 1.4f, .12f) * 3f, .95f);
        GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var splat = Resources.Load<GameObject>("Elderboom/Boss/AcidSplat");
        if (splat != null) { var s = Instantiate(splat, at + Vector3.up * .05f, Quaternion.identity); Destroy(s, 6f); }
        glow = new GameObject("AcidGlow").AddComponent<Light>(); glow.transform.SetParent(transform, false);
        glow.transform.position = at + Vector3.up * .4f; glow.type = LightType.Point; glow.color = new Color(.5f, 1f, .2f); glow.range = 3.5f; glow.intensity = 600f;
        sizzle = ElderboomAudio.Loop("boss/acid_sizzle", at, .55f, 1.5f, 18f);
    }
}
