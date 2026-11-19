using UnityEngine;
using UnityEngine.SceneManagement;

// The church's stained-glass windows had no collider, so players could walk through them.
// When a scene loads, every glass mesh of the church gets an invisible blocker with its own shape.
// Blockers sit on layer 19 (like the zombies): shots ignore it (ElderboomCombat.WorldMask), so windows
// can still be shot through, but the player's CharacterController stops against it.
public static class ElderboomChurchWindows
{
    const string ChurchRoot = "FallenCity_Church_HumanScale";
    const int BlockerLayer = 19;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Seal();
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode) => Seal();

    static void Seal()
    {
        var church = GameObject.Find(ChurchRoot);
        if (church == null) return;
        int sealedCount = 0;
        foreach (var filter in church.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !IsGlass(filter)) continue;
            if (filter.transform.Find("WindowBlocker") != null) continue;
            var blocker = new GameObject("WindowBlocker") { layer = BlockerLayer };
            blocker.transform.SetParent(filter.transform, false);
            var collider = blocker.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            sealedCount++;
        }
        if (sealedCount > 0) Debug.Log("[ChurchWindows] sealed " + sealedCount + " windows");
    }

    static bool IsGlass(MeshFilter filter)
    {
        string n = filter.name.ToLowerInvariant();
        if (n.Contains("stainedglass") || n.Contains("vitrail") || n.Contains("window")) return true;
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null) return false;
        foreach (var m in renderer.sharedMaterials)
        {
            if (m == null) continue;
            string mn = m.name.ToLowerInvariant();
            if (mn.Contains("vitrail") || mn.Contains("verre") || mn.Contains("glass")) return true;
        }
        return false;
    }
}
