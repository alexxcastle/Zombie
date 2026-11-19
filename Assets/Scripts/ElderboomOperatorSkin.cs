using System.Collections.Generic;
using UnityEngine;

// Operators' first-person arms: the glTF materials (flat salmon skin, no normal map) are swapped for HDRP/Lit materials
// with a natural skin tone, pores and fine creases in a normal map and skin-like smoothness
// (Tools/operator_skin_textures.py, built by ElderboomClaudeSkin "apply"). The mapping lives in
// Resources/Elderboom/OperatorSkin; the swap runs once on the loaded scene and on every Grips model instantiated later.
[CreateAssetMenu(menuName = "Elderboom/Operator Skin Map")]
public sealed class ElderboomOperatorSkin : ScriptableObject
{
    public Material[] from = new Material[0];
    public Material[] to = new Material[0];

    static Dictionary<Material, Material> map;

    static Dictionary<Material, Material> Map()
    {
        if (map != null) return map;
        map = new Dictionary<Material, Material>();
        var asset = Resources.Load<ElderboomOperatorSkin>("Elderboom/OperatorSkin");
        if (asset != null)
            for (int i = 0; i < Mathf.Min(asset.from.Length, asset.to.Length); i++)
                if (asset.from[i] != null && asset.to[i] != null) map[asset.from[i]] = asset.to[i];
        return map;
    }

    public static void Apply(Transform root)
    {
        if (root == null || Map().Count == 0) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) Swap(r);
    }

    static void Swap(Renderer r)
    {
        var mats = r.sharedMaterials; bool changed = false;
        for (int i = 0; i < mats.Length; i++) if (mats[i] != null && map.TryGetValue(mats[i], out var m)) { mats[i] = m; changed = true; }
        if (changed) r.sharedMaterials = mats;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnSceneLoaded()
    {
        if (Map().Count == 0) return;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Swap(r);
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Loaded; UnityEngine.SceneManagement.SceneManager.sceneLoaded += Loaded;
    }
    static void Loaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    { foreach (var go in s.GetRootGameObjects()) Apply(go.transform); }
}
