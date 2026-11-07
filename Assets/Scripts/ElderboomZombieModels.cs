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
        netModel = Mathf.Min(i + 1, 15);
        return s[i];
    }

    public static GameObject ByNet(int netModel)
    {
        var s = Set;
        return netModel >= 1 && netModel <= s.Length ? s[netModel - 1] : null;
    }
}
