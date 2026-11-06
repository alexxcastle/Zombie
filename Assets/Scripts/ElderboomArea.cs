using System.Collections.Generic;
using UnityEngine;

// A named, hand-placed zone of the level (village, farm, timber yard...).
// Spawn points belong to an area by name, or by being inside its box; spawns in
// locked areas are skipped. Gates and triggers unlock areas by name.
// With no areas in the scene, every spawn point stays available (previous behaviour).
public sealed class ElderboomArea : MonoBehaviour
{
    public string areaName = "Village";
    public bool unlockedAtStart = true;
    public Vector3 size = new Vector3(40f, 12f, 40f);
    // Optional outline on the ground (world x,z). When set it replaces the box: blocks of the map are irregular.
    public Vector2[] polygon;

    static readonly List<ElderboomArea> all = new List<ElderboomArea>();
    public static IReadOnlyList<ElderboomArea> All => all;
    public bool Unlocked { get; private set; }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); Unlocked = unlockedAtStart; }
    void OnDisable() { all.Remove(this); }

    public bool Contains(Vector3 position)
    {
        if (polygon != null && polygon.Length >= 3)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > position.z) != (b.y > position.z) && position.x < (b.x - a.x) * (position.z - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
        Vector3 local = transform.InverseTransformPoint(position);
        return Mathf.Abs(local.x) <= size.x * .5f && Mathf.Abs(local.y) <= size.y * .5f && Mathf.Abs(local.z) <= size.z * .5f;
    }

    public static ElderboomArea Find(string name)
    {
        foreach (var area in all) if (area != null && string.Equals(area.areaName, name, System.StringComparison.OrdinalIgnoreCase)) return area;
        return null;
    }

    public static ElderboomArea At(Vector3 position)
    {
        foreach (var area in all) if (area != null && area.Contains(position)) return area;
        return null;
    }

    public static void Unlock(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        bool changed = false;
        foreach (var area in all)
            if (area != null && !area.Unlocked && string.Equals(area.areaName, name, System.StringComparison.OrdinalIgnoreCase)) { area.Unlocked = true; changed = true; }
        if (changed) ElderboomEvents.RaiseAreaUnlocked(name);
    }

    public static bool IsSpawnAllowed(ElderboomSpawnPoint spawn)
    {
        if (spawn == null || all.Count == 0) return true;
        var owner = !string.IsNullOrEmpty(spawn.area) ? Find(spawn.area) : At(spawn.transform.position);
        return owner == null || owner.Unlocked;
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Color color = unlockedAtStart ? new Color(.2f, .85f, .35f) : new Color(.95f, .45f, .1f);
        Gizmos.color = new Color(color.r, color.g, color.b, .08f); Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(color.r, color.g, color.b, .8f); Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
