using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public enum ElderboomZombieSkill { None, Ambush, Grab }

// Zombie skills (more to come):
//  Ambush - goes to a spot behind cover out of the player's sight, crouches and waits; when the player comes
//           within a few metres it springs out (jump attack) and keeps sprinting afterwards.
//  Grab   - instead of a claw swing it grabs the player and bites: the player is slowed while held;
//           stepping away (or a push / stun / hit) breaks the hold.
//  Weapon - a zombie with ElderboomZombieWeapon (the cook's cleaver) swings it: more reach and damage.
public sealed partial class ElderboomZombie
{
    public ElderboomZombieSkill Skill { get; private set; }
    ElderboomZombieWeapon weapon;
    float meleeAnimSpeed = 1f;

    public void AssignSkill(ElderboomZombieSkill skill)
    {
        Skill = skill;
        if (skill == ElderboomZombieSkill.Ambush) { ambushPhase = AmbushPhase.Find; ambushSince = Time.time; }
    }
    ElderboomZombieProfile profile;
    void InitSkills() { weapon = GetComponent<ElderboomZombieWeapon>(); profile = GetComponent<ElderboomZombieProfile>(); if (profile != null) pushResist *= profile.pushTaken; }
    float AttackReach => (weapon != null ? weapon.reach : 1.45f) + (profile != null ? profile.reach : 0f);
    bool Sprinter;

    // ---- Ambush ------------------------------------------------------------------------
    enum AmbushPhase { None, Find, Seek, Hide, Pounce }
    AmbushPhase ambushPhase;
    Vector3 ambushSpot; float ambushSince, ambushPhaseAt, ambushRetryAt; int ambushTries; bool pounceHit;
    public static string LastCoverReport;
    static readonly Collider[] coverHits = new Collider[16];

    // True while the skill drives the zombie this frame (normal chase/attack skipped).
    bool SkillTick(float distance)
    {
        if (ambushPhase == AmbushPhase.None) return false;
        if (lifted || IsFrozen || hitReacting || Time.time < pushUntil || InsideKeepAway() || TargetIgnored) { EndAmbush(false); return false; }
        switch (ambushPhase)
        {
            case AmbushPhase.Find:
                if (Time.time < ambushRetryAt) return false; // keep chasing meanwhile, look again shortly
                if (!FindCover(out ambushSpot)) { if (++ambushTries >= 4) EndAmbush(false); else ambushRetryAt = Time.time + 3f; return false; }
                ambushPhase = AmbushPhase.Seek; ambushPhaseAt = Time.time;
                agent.isStopped = false; agent.speed = moveSpeed * 1.15f; agent.SetDestination(ambushSpot); SetMotion(Locomotion());
                return true;
            case AmbushPhase.Seek:
                TickBody(distance);
                if (distance < 7f || Time.time - ambushPhaseAt > 25f) { EndAmbush(false); return false; }
                if (!agent.pathPending && agent.remainingDistance < .5f)
                {
                    ambushPhase = AmbushPhase.Hide; ambushPhaseAt = Time.time;
                    agent.isStopped = true; agent.ResetPath(); agent.velocity = Vector3.zero;
                    SetMotion("AmbushCover");
                }
                return true;
            case AmbushPhase.Hide:
                if (animator != null) { animator.speed = 1f; WakeAnimation(.5f); }
                agent.isStopped = true;
                Face(TargetPos, 4f);
                if (Time.time - ambushPhaseAt > 1.1f && animator != null && !animator.GetCurrentAnimatorStateInfo(0).IsName("AmbushIdle") && !animator.IsInTransition(0)) SetMotion("AmbushIdle");
                bool close = distance < 5.5f, timeout = Time.time - ambushSince > 50f;
                if (close || timeout) { if (close) BeginPounce(); else EndAmbush(true); }
                return true;
            case AmbushPhase.Pounce:
                if (animator != null) animator.speed = 1f;
                Face(TargetPos, 10f);
                agent.isStopped = false; agent.speed = moveSpeed * 2.4f; agent.SetDestination(TargetPos);
                float t = Time.time - ambushPhaseAt;
                if (!pounceHit && t > .55f && distance <= 2.1f && HasLineOfSight())
                {
                    pounceHit = true;
                    if (chaseAvatar != null) ElderboomLan.DamageAvatar(chaseAvatar, damage * 1.3f, transform.position);
                    else { player.TakeDamage(damage * 1.3f, transform.position); }
                }
                if (t > 1.25f) EndAmbush(true);
                return true;
        }
        return false;
    }

    void BeginPounce()
    {
        ambushPhase = AmbushPhase.Pounce; ambushPhaseAt = Time.time; pounceHit = false;
        SetMotion("AmbushPounce"); ElderboomAudio.Zombie("attack", ref attackVariant, transform.position);
        nextAttack = Time.time + 1.4f;
    }

    void EndAmbush(bool sprint)
    {
        bool wasHiding = ambushPhase == AmbushPhase.Hide || ambushPhase == AmbushPhase.Pounce;
        ambushPhase = AmbushPhase.None;
        if (sprint) { Sprinter = true; moveSpeed *= 1.25f; }
        if (agent != null && agent.isOnNavMesh) { agent.isStopped = false; agent.speed = moveSpeed; }
        repathAt = 0f;
        if (wasHiding && !dead) SetMotion(Locomotion());
    }

    void Face(Vector3 target, float rate)
    {
        var d = target - transform.position; d.y = 0; if (d.sqrMagnitude < .01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    // A walkable spot next to solid cover, out of the player's sight, 9-28 m from the player and reachable.
    bool FindCover(out Vector3 spot)
    {
        spot = default; if (TargetDown) return false;
        Vector3 target = TargetPos, eye = target + Vector3.up * 1.6f, me = transform.position;
        var path = new NavMeshPath(); float best = float.MinValue; bool found = false;
        int sampled = 0, far = 0, open = 0, seen = 0, unreachable = 0;
        for (int i = 0; i < 72; i++)
        {
            // A third around the zombie, a third on its way to the player, a third in a ring around the player.
            Vector3 c = i % 3 == 0 ? me + Random.insideUnitSphere * 20f
                : i % 3 == 1 ? Vector3.Lerp(me, target, Random.Range(.2f, .8f)) + Random.insideUnitSphere * 10f
                : target + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(9f, 26f);
            if (!NavMesh.SamplePosition(c, out var hit, 2f, NavMesh.AllAreas)) continue;
            sampled++;
            var p = hit.position; float d = Vector3.Distance(p, target);
            if (d < 9f || d > 28f) { far++; continue; }
            int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * .9f, 1.6f, coverHits, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore);
            bool cover = false;
            for (int k = 0; k < n; k++) { var h = coverHits[k]; if (h is TerrainCollider || h.GetComponentInParent<ElderboomZombie>() != null || h.GetComponentInParent<HDRPSlicePlayer>() != null || h.bounds.size.y < .9f) continue; cover = true; break; }
            if (!cover) { open++; continue; }
            if (!Physics.Linecast(eye, p + Vector3.up * .8f, ElderboomCombat.WorldMask & ~(1 << 2), QueryTriggerInteraction.Ignore)) { seen++; continue; } // visible: no ambush
            if (!NavMesh.CalculatePath(me, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { unreachable++; continue; }
            float len = 0; for (int k = 1; k < path.corners.Length; k++) len += Vector3.Distance(path.corners[k - 1], path.corners[k]);
            if (len > 50f) { unreachable++; continue; }
            float score = -Mathf.Abs(d - 14f) - len * .15f + Random.value * 2f;
            if (score > best) { best = score; spot = p; found = true; }
        }
        LastCoverReport = $"cover search: sampled {sampled} distance {far} open {open} visible {seen} unreachable {unreachable} found {found} {spot}";
        return found;
    }

    // ---- Grab --------------------------------------------------------------------------
    float grabReadyAt;
    bool WantsGrab => Skill == ElderboomZombieSkill.Grab && Time.time >= grabReadyAt && chaseAvatar == null && player != null;

    IEnumerator Grab()
    {
        int token = attackToken;
        melee = true; meleeAnimSpeed = 1.5f; nextAttack = Time.time + attackInterval + 1f;
        SetMotion("GrabLunge"); ElderboomAudio.Zombie("attack", ref attackVariant, transform.position);
        yield return new WaitForSeconds(.5f);
        bool held = token == attackToken && !dead && !IsFrozen && !lifted && !TargetDown && Vector3.Distance(transform.position, TargetPos) <= 1.75f && HasLineOfSight();
        if (held)
        {
            meleeAnimSpeed = 1f; SetMotion("GrabHold"); player.ApplyGrab(.32f, .35f, transform.position);
            float until = Time.time + 2.2f, biteAt = Time.time + .55f;
            while (Time.time < until && token == attackToken && !dead && !IsFrozen && !lifted && !TargetDown && !TargetIgnored)
            {
                var d = TargetPos - transform.position; d.y = 0;
                if (d.magnitude > 2.15f) break; // the player tore free
                Face(TargetPos, 12f);
                // Keep the bodies apart a little so the camera never ends up inside the zombie.
                player.ApplyGrab(.32f, .2f, transform.position);
                if (Time.time >= biteAt) { biteAt = Time.time + .75f; int c = -1; ElderboomAudio.Zombie("contact", ref c, transform.position); player.TakeDamage(damage * .45f, transform.position); }
                yield return null;
            }
        }
        grabReadyAt = Time.time + (held ? 7f : 3f);
        if (token != attackToken) yield break;
        meleeAnimSpeed = 1f; melee = false; if (!dead && Time.time >= reactionUntil) SetMotion(Locomotion());
    }

    // ---- Weapon swing ------------------------------------------------------------------
    IEnumerator WeaponStrike()
    {
        int token = attackToken; int pick = Random.Range(0, weapon.attackStates.Length);
        string state = weapon.attackStates[pick];
        float len = ClipLength(state, 2.3f), speed = weapon.swingSpeed, hitAt = len * weapon.hitFraction[Mathf.Min(pick, weapon.hitFraction.Length - 1)] / speed;
        melee = true; meleeAnimSpeed = speed; nextAttack = Time.time + Mathf.Max(attackInterval, len / speed + .15f);
        SetMotion(state); ElderboomAudio.Zombie("attack", ref attackVariant, transform.position);
        float start = Time.time;
        while (Time.time - start < hitAt) { if (token != attackToken) yield break; if (!TargetDown) Face(TargetPos, 6f); yield return null; }
        if (token == attackToken && !dead && Time.time >= frozenUntil && Time.time >= ignoreUntil && !TargetIgnored && !lifted && !InsideKeepAway() && player != null && !TargetDown
            && Vector3.Distance(transform.position, TargetPos) <= weapon.reach + .15f && HasLineOfSight())
        {
            int contact = -1; ElderboomAudio.Zombie("contact", ref contact, transform.position);
            ElderboomAudio.Play("kenney/impactMetal_light_00" + Random.Range(0, 5), TargetPos, .7f, false, .8f);
            ElderboomAudio.Play("kenney/impactPunch_heavy_00" + Random.Range(0, 5), TargetPos, .5f, false, .9f);
            if (chaseAvatar != null) ElderboomLan.DamageAvatar(chaseAvatar, damage * weapon.damageMultiplier, transform.position);
            else player.TakeDamage(damage * weapon.damageMultiplier, transform.position);
        }
        yield return new WaitForSeconds(Mathf.Max(.1f, len / speed - hitAt));
        if (token != attackToken) yield break;
        meleeAnimSpeed = 1f; melee = false; if (!dead && Time.time >= reactionUntil) SetMotion(Locomotion());
    }

    float ClipLength(string name, float fallback)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return fallback;
        foreach (var c in animator.runtimeAnimatorController.animationClips) if (c.name == name) return c.length;
        return fallback;
    }
}
