using System.Collections.Generic;
using UnityEngine;

// Registry of operators. Assets in Resources/Elderboom/Operators are the source of
// truth; when none exist (fresh checkout, before running the editor menu) the
// built-in defaults below are used, with the same values.
public static class ElderboomOperators
{
    public const string ResourcePath = "Elderboom/Operators";
    static ElderboomOperatorDefinition[] all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all = null;

    public static IReadOnlyList<ElderboomOperatorDefinition> All
    {
        get
        {
            if (all != null) return all;
            var loaded = Resources.LoadAll<ElderboomOperatorDefinition>(ResourcePath);
            all = loaded.Length > 0 ? loaded : CreateDefaults();
            System.Array.Sort(all, (a, b) => a.menuOrder.CompareTo(b.menuOrder));
            return all;
        }
    }

    public static ElderboomOperatorDefinition Find(string id)
    {
        foreach (var op in All) if (op != null && string.Equals(op.id, id, System.StringComparison.OrdinalIgnoreCase)) return op;
        return null;
    }

    public static ElderboomOperatorDefinition[] CreateDefaults()
    {
        var tactical = Operator(ElderboomIds.Tactical, 0, "STRIKER-12\nShockwave / Lift / Keep Away", "striker", "m1911", "", 2, "STRIKER-12", 12, 72, 13f, .43f);
        tactical.tacticalArms = true; tactical.sharedCooldown = false;
        Rig(tactical, "Hand.R", "MainWeapon_Muzzle", "output_unwrapped.003");
        Card(tactical, "TACTICAL SURVIVOR", "ARMORED OPERATOR / STANDARD ISSUE", "Original tactical character with the current combat gear.", "Elderboom/UI/tactical_survivor");
        tactical.abilities = new ElderboomAbilityDefinition[]
        {
            Ability<ElderboomShockwaveAbility>("shockwave", "Shockwave", 12f),
            Ability<ElderboomLiftSmashAbility>("lift_smash", "Lift & smash", 9f),
            Ability<ElderboomKeepAwayAbility>("keep_away", "Keep away", 25f)
        };
        var eli = Operator(ElderboomIds.Eli, 1, "SIG 550\nIgnore / Pulse / Freeze", "sig550", "rhino", "", 2, "SIG 550", 30, 120, 5f, .086f);
        // Eli: the SIG is output_unwrapped.001 + Caricatore; the acoustic device is an ability prop and stays.
        Rig(eli, "DEF_hand.R", "MainWeapon_Muzzle", "output_unwrapped.001", "Caricatore");
        Card(eli, "ELI · ACOUSTIC OPERATOR", "URBAN SURVIVOR / SIG 550", "Uses the acoustic signal to divert or immobilize nearby zombies.", "Elderboom/UI/female_urban_survivor");
        eli.abilities = new ElderboomAbilityDefinition[]
        {
            Ability<ElderboomAcousticIgnoreAbility>("ignore_signal", "Ignore signal", 15f),
            Ability<ElderboomAcousticPulseAbility>("acoustic_pulse", "Acoustic pulse", 15f),
            Ability<ElderboomAcousticFreezeAbility>("freeze_signal", "Freeze signal", 15f)
        };
        var soph = Operator(ElderboomIds.Soph, 2, "AKMN ZENITCO\nPlasma Volley / Blinding Flare", "akmn", "rhino", "hk53", 3, "AKMN ZENITCO", 30, 120, 6f, .10f);
        soph.abilityAnimationSeconds = 6.87f;
        Rig(soph, "DEF-hand.R", "Soph_Rifle_Muzzle", "AKMN_Rifle_ROOT", "AKMN_Magazine_CTRL", "AKMN_Magazine_Spare_CTRL");
        Card(soph, "SOPH · PLASMA OPERATOR", "PLASMA SPECIALIST / AKMN", "Carries an AKMN rifle and draws a plasma pistol to fire three blazing projectiles.", null);
        soph.abilities = new ElderboomAbilityDefinition[]
        {
            Ability<ElderboomPlasmaVolleyAbility>("plasma_volley", "Plasma volley", 30f),
            Ability<ElderboomPlasmaFlareAbility>("blinding_flare", "Blinding flare", 30f)
        };
        return new[] { tactical, eli, soph };
    }

    static ElderboomOperatorDefinition Operator(string id, int order, string description, string primary, string secondary, string extra, int slots,
        string label, int capacity, int reserve, float damage, float interval)
    {
        var op = ScriptableObject.CreateInstance<ElderboomOperatorDefinition>();
        op.name = id; op.id = id; op.displayName = id; op.menuOrder = order; op.menuDescription = description;
        op.primaryWeapon = primary; op.secondaryWeapon = secondary; op.extraWeapon = extra; op.weaponSlots = slots;
        op.weaponLabel = label; op.capacity = capacity; op.reserve = reserve; op.damage = damage; op.fireInterval = interval;
        return op;
    }

    // Bone and node names from Logs/rig_dump.txt (Elderboom HDRP > Dev > Dump Viewmodel Rigs).
    public static void Rig(ElderboomOperatorDefinition op, string hand, string muzzle, params string[] stowed)
    {
        op.rightHandBone = hand; op.weaponMuzzle = muzzle; op.stowedWeaponNodes = stowed;
    }

    public static void Card(ElderboomOperatorDefinition op, string title, string tag, string body, string portraitResource)
    {
        op.cardTitle = title; op.cardTag = tag; op.cardBody = body;
        if (!string.IsNullOrEmpty(portraitResource)) op.portrait = Resources.Load<Texture2D>(portraitResource);
    }

    static T Ability<T>(string id, string displayName, float cooldown) where T : ElderboomAbilityDefinition
    {
        var ability = ScriptableObject.CreateInstance<T>();
        ability.name = id; ability.id = id; ability.displayName = displayName; ability.cooldown = cooldown;
        ability.description = DefaultDescription(id);
        return ability;
    }

    // Menu text for the operator screen; edit it on the ability assets.
    public static string DefaultDescription(string abilityId)
    {
        switch (abilityId)
        {
            case "shockwave": return "A glove blast that throws back, stuns and damages every zombie in front of you.";
            case "lift_smash": return "Lifts the zombies ahead into the air, then slams them into the ground.";
            case "keep_away": return "A barrier around you holds the horde outside its radius for a few seconds.";
            case "ignore_signal": return "An acoustic signal that makes every zombie ignore you while it plays.";
            case "acoustic_pulse": return "Throws a sound decoy where you aim. Zombies near it stop chasing you and go to investigate.";
            case "freeze_signal": return "Freezes every zombie close to you for as long as the signal plays.";
            case "plasma_volley": return "Draws the plasma pistol and fires three blazing bolts.";
            case "blinding_flare": return "Charges a plasma flare that blinds the zombies in front of you.";
            default: return "";
        }
    }
}
