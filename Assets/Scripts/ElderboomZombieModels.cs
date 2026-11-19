using System.Linq;
using UnityEngine;

// The village's zombies: every prefab in Resources/Elderboom/Zombies (the rural set). Network id = index + 1 in name
// order (0 = the old Zombie / ZombieFemale prefabs, used only when the set is missing).
public static class ElderboomZombieModels
{
    static GameObject[] set;
    public static GameObject[] Set => set ??= Resources.LoadAll<GameObject>("Elderboom/Zombies").Where(g => g.GetComponent<ElderboomZombie>() != null).OrderBy(g => g.name).ToArray();

    public static GameObject Pick(out int netModel)
    {
        var s = Set;
        if (s.Length == 0) { netModel = 0; return null; }
        int i = Random.Range(0, s.Length);
        // The obese farmer is the boss (every 10th wave): not in the normal horde.
        for (int k = 0; k < 8 && s.Length > 1 && IsBossModel(s[i]); k++) i = Random.Range(0, s.Length);
        netModel = Mathf.Min(i + 1, 15);
        return s[i];
    }

    public static bool IsBossModel(GameObject g) => g != null && g.name.IndexOf("heavy_farmer", System.StringComparison.OrdinalIgnoreCase) >= 0;

    public static GameObject Boss(out int netModel)
    {
        var s = Set;
        for (int i = 0; i < s.Length; i++) if (IsBossModel(s[i])) { netModel = Mathf.Min(i + 1, 15); return s[i]; }
        return Pick(out netModel);
    }

    public static GameObject ByNet(int netModel)
    {
        var s = Set;
        return netModel >= 1 && netModel <= s.Length ? s[netModel - 1] : null;
    }
}
