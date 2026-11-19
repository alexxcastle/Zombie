using System.IO;
using UnityEditor;
using UnityEngine;

// Writes the built-in operator and ability defaults as assets under
// Resources/Elderboom/Operators. From then on the assets are the source of truth:
// edit cooldowns, names, loadouts and ability lists in the Inspector.
public static class ElderboomDataTools
{
    const string Folder = "Assets/Resources/Elderboom/Operators";

    [MenuItem("Elderboom HDRP/Data/Create or Update Operator Assets")]
    public static void CreateOperatorAssets()
    {
        Directory.CreateDirectory(Folder + "/Abilities");
        AssetDatabase.Refresh();
        int created = 0, kept = 0;
        foreach (var op in ElderboomOperators.CreateDefaults())
        {
            string path = Folder + "/" + op.id + ".asset";
            var current = AssetDatabase.LoadAssetAtPath<ElderboomOperatorDefinition>(path);
            if (current != null)
            {
                // Existing assets keep their edits; only empty menu-card fields are filled in.
                if (string.IsNullOrEmpty(current.cardTitle)) { current.cardTitle = op.cardTitle; current.cardTag = op.cardTag; current.cardBody = op.cardBody; }
                if (current.portrait == null) current.portrait = op.portrait;
                if (string.IsNullOrEmpty(current.rightHandBone)) { current.rightHandBone = op.rightHandBone; current.weaponMuzzle = op.weaponMuzzle; current.stowedWeaponNodes = op.stowedWeaponNodes; }
                foreach (var ability in current.abilities)
                    if (ability != null && string.IsNullOrEmpty(ability.description))
                    { ability.description = ElderboomOperators.DefaultDescription(ability.id); EditorUtility.SetDirty(ability); }
                // Placeholders are replaced once a real ability exists for that slot.
                for (int i = 0; i < current.abilities.Length && i < op.abilities.Length; i++)
                {
                    if (!(current.abilities[i] is ElderboomUnavailableAbility) || op.abilities[i] is ElderboomUnavailableAbility) continue;
                    string abilityPath = Folder + "/Abilities/" + op.abilities[i].id + ".asset";
                    var existing = AssetDatabase.LoadAssetAtPath<ElderboomAbilityDefinition>(abilityPath);
                    if (existing == null) { AssetDatabase.CreateAsset(op.abilities[i], abilityPath); existing = op.abilities[i]; }
                    current.abilities[i] = existing;
                    Debug.Log("[Elderboom] " + current.id + " ability " + (i + 1) + " is now " + existing.displayName);
                }
                EditorUtility.SetDirty(current);
                kept++; continue;
            }
            for (int i = 0; i < op.abilities.Length; i++)
            {
                var ability = op.abilities[i];
                string abilityPath = Folder + "/Abilities/" + ability.id + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<ElderboomAbilityDefinition>(abilityPath);
                if (existing != null) op.abilities[i] = existing;
                else AssetDatabase.CreateAsset(ability, abilityPath);
            }
            AssetDatabase.CreateAsset(op, path);
            created++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Elderboom] Operator assets: " + created + " created, " + kept + " already present in " + Folder);
    }
}
