using System.Collections.Generic;
using UnityEngine;

public sealed partial class ElderboomArsenal
{
    readonly HashSet<Slot> upgradedSlots = new HashSet<Slot>();
    Slot deposited;
    public bool WeaponDeposited => deposited != null;
    public bool ActiveUpgraded => IsUpgraded(active);
    public bool CanUpgradeActive => CanSwitch && active != null && slots.Contains(active)
        && active.definition != null && active.definition.FireMode != ElderboomFireMode.Melee && !ActiveUpgraded;
    bool IsUpgraded(Slot slot) => slot != null && upgradedSlots.Contains(slot);

    public static ElderboomWeaponDefinition UpgradedDefinition(ElderboomWeaponDefinition source)
    {
        var copy = JsonUtility.FromJson<ElderboomWeaponDefinition>(JsonUtility.ToJson(source));
        copy.upgraded = true;
        copy.damage *= 2f;
        copy.capacity = Mathf.CeilToInt(source.capacity * 1.5f);
        copy.reserve = Mathf.CeilToInt(source.reserve * 1.5f);
        return copy;
    }

    public GameObject BeginAltarDeposit(Transform socket)
    {
        if (!CanUpgradeActive || socket == null) return null;
        SaveActive();
        var display = ElderboomUpgradedWeapon.CreateDisplay(active.root, active.definition, player.OperatorDisplayName, socket);
        if (display == null) return null;
        deposited = active; player.CancelGameplayActions();
        player.WeaponDeposited = true;
        active.root.gameObject.SetActive(false);
        return display;
    }

    public bool CompleteAltarUpgrade()
    {
        if (deposited == null || player == null || player.IsDead) return false;
        var slot = deposited;
        slot.definition = UpgradedDefinition(slot.definition);
        slot.ammo = slot.definition.capacity; slot.reserve = slot.definition.reserve;
        upgradedSlots.Add(slot);
        deposited = null; player.WeaponDeposited = false;
        // Equip normally snapshots the outgoing gun. Here it is the same slot:
        // do not overwrite its freshly enlarged magazine with the old counts.
        if (active == slot) active = null;
        EquipUnchecked(slot);
        return true;
    }

    public void CancelAltarDeposit()
    {
        if (deposited == null) return;
        var slot = deposited; deposited = null;
        if (player != null) player.WeaponDeposited = false;
        if (slot.root != null && player != null) EquipUnchecked(slot);
    }
}
