using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Ability selection and cooldowns. Ability assets (Scripts/Data) own their tuning and
// effects; the internal members here are the shared services they use. Their coroutines
// run on this component, so CancelGameplay stops them.
public sealed partial class ElderboomGame
{
    public void SelectAbility(int ability)
    {
        if (Time.time < abilityAnimationUntil) return;
        selectedAbility = Mathf.Clamp(ability, 0, AbilityNames().Length - 1);
        ShowNotice(AbilityNames()[selectedAbility] + " SELECTED");
    }

    public void UseSelectedAbility(HDRPSlicePlayer source)
    {
        if (state != State.Playing || Time.time < abilityAnimationUntil || source == null || source.IsDead || source.Reloading || source.WeaponDeposited) return;
        var op = Operator;
        if (op.abilities == null || op.abilities.Length == 0) return;
        int index = Mathf.Clamp(selectedAbility, 0, op.abilities.Length - 1);
        var ability = op.abilities[index];
        if (ability == null) return;
        int slot = CooldownSlot(index);
        if (Time.time < abilityReady[slot])
        {
            // Shared-cooldown operators stay silent, as before; per-ability ones say how long.
            if (!op.sharedCooldown) ShowNotice(ability.displayName + " ready in " + Mathf.CeilToInt(abilityReady[slot] - Time.time) + " s");
            return;
        }
        if (!ability.Activate(this, source, op)) return;
        abilityReady[slot] = Time.time + ability.cooldown;
        abilityCooling[slot] = true;
        ElderboomEvents.RaiseAbilityUsed(ability.id);
    }

    // ---- Ability host -----------------------------------------------------------------
    // Ability assets (Scripts/Data) own their tuning and effects; these are the shared
    // services they use. Coroutines run on this component, so CancelGameplay stops them.
    internal Camera AbilityCamera(HDRPSlicePlayer source = null)
    {
        if (source != null) playerCamera = source.GetComponentInChildren<Camera>();
        if (playerCamera == null && player != null) playerCamera = player.ViewCamera;
        return playerCamera;
    }

    internal ElderboomTacticalArms TacticalArms(HDRPSlicePlayer source)
    {
        AbilityCamera(source);
        return source.GetComponent<ElderboomTacticalArms>() ?? source.gameObject.AddComponent<ElderboomTacticalArms>();
    }

    internal void IgnorePlayerFor(float seconds) { ignoredUntil = Mathf.Max(ignoredUntil, Time.time + seconds); ElderboomLan.SendIgnore(seconds); }

    readonly List<ElderboomZombie> abilityTargets = new List<ElderboomZombie>();

    // Living zombies within range of the player, inside the view cone and not behind cover.
    // The returned list is reused: copy it if you keep it across frames.
    internal List<ElderboomZombie> FindAbilityTargets(float range, float coneDot, Vector3 origin)
    {
        abilityTargets.Clear();
        var camera = AbilityCamera();
        if (camera == null || player == null) return abilityTargets;
        Vector3 forward = camera.transform.forward;
        foreach (var zombie in zombies)
        {
            if (zombie == null || !zombie.IsAlive) continue;
            Vector3 point = zombie.transform.position + Vector3.up * 0.9f;
            Vector3 offset = point - origin;
            float distance = Vector3.Distance(player.transform.position, zombie.transform.position);
            if (distance > range || distance < 0.1f || Vector3.Dot(offset.normalized, forward) < coneDot) continue;
            if (Physics.Raycast(origin, offset.normalized, out var hit, offset.magnitude, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<ElderboomZombie>() != zombie) continue;
            abilityTargets.Add(zombie);
        }
        return abilityTargets;
    }

    string[] AbilityNames()
    {
        var abilities = Operator.abilities;
        if (abilities == null || abilities.Length == 0) return new[] { "None" };
        var names = new string[abilities.Length];
        for (int i = 0; i < names.Length; i++) names[i] = abilities[i] != null ? abilities[i].displayName : "None";
        return names;
    }

    int CooldownSlot(int ability) => Operator.sharedCooldown ? 0 : Mathf.Clamp(ability, 0, abilityReady.Length - 1);

    public void BeginAbilityAnimation(float seconds) { abilityAnimationUntil = Time.time + seconds; }
}
