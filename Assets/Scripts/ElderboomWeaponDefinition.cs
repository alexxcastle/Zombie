using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class ElderboomWeaponDefinition
{
    public string id,name,category,asset,resource,mode,operatorId;
    public bool IsOperatorPrimary => !string.IsNullOrEmpty(operatorId);
    public bool upgraded;
    public ElderboomFireMode FireMode => category==ElderboomIds.Category.Melee ? ElderboomFireMode.Melee : mode=="explosive" ? ElderboomFireMode.Explosive : mode=="flame" ? ElderboomFireMode.Flame : mode=="pulse" ? ElderboomFireMode.Pulse : ElderboomFireMode.Hitscan;
    public int capacity,reserve,pellets,pierce;
    public float damage,range,falloffStart,minimumDamage,spread,fireInterval;
}

public static class ElderboomWeapons
{
    [Serializable] class Catalog { public ElderboomWeaponDefinition[] weapons; }
    static ElderboomWeaponDefinition[] weapons;
    public static ElderboomWeaponDefinition[] All => weapons ?? (weapons=JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("Elderboom/Weapons").text).weapons);
    public static ElderboomWeaponDefinition Find(string name)=>All.FirstOrDefault(w=>string.Equals(w.name,name,StringComparison.OrdinalIgnoreCase)||string.Equals(w.id,name,StringComparison.OrdinalIgnoreCase));
}
