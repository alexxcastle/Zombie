using UnityEngine;

// One playable operator: menu text, starting loadout and abilities.
[CreateAssetMenu(menuName = "Elderboom/Operator", fileName = "Operator")]
public sealed class ElderboomOperatorDefinition : ScriptableObject
{
    public string id = ElderboomIds.Soph;
    public string displayName = ElderboomIds.Soph;
    public int menuOrder;
    [TextArea] public string menuDescription;

    [Header("Main menu card")]
    public string cardTitle;
    public string cardTag;
    [TextArea] public string cardBody;
    public Texture2D portrait;

    [Header("Weapons (ids from ElderboomWeapons)")]
    public string primaryWeapon = "akmn";
    public string secondaryWeapon = "rhino";
    public string extraWeapon = "";
    [Range(2, 3)] public int weaponSlots = 2;

    [Header("Starting weapon stats")]
    public string weaponLabel = "AKMN ZENITCO";
    public int capacity = 30;
    public int reserve = 120;
    public float damage = 6f;
    public float fireInterval = .1f;
    public bool tacticalArms;

    [Header("Own arms with every weapon (ElderboomOperatorGrip)")]
    [Tooltip("Right-hand bone of the operator's arms rig; other weapons are attached to it.")]
    public string rightHandBone;
    [Tooltip("Muzzle marker of the operator's own weapon; its direction aligns the attached barrel.")]
    public string weaponMuzzle;
    [Tooltip("Nodes of the operator's own weapon to hide when holding another weapon. Ability props stay visible.")]
    public string[] stowedWeaponNodes = new string[0];

    [Header("Abilities")]
    public ElderboomAbilityDefinition[] abilities = new ElderboomAbilityDefinition[0];
    [Tooltip("One cooldown for all abilities (Soph, Eli) instead of one per ability (Tactical).")]
    public bool sharedCooldown = true;
    public float abilityAnimationSeconds = 3.1f;
}
