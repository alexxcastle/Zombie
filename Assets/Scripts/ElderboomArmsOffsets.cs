using System.Collections.Generic;
using UnityEngine;

// Hand-tuned corrections for the Tactical arms (edited in game with ElderboomArmsEditor, key +).
// Key = weapon id, "*" = every weapon (hip framing). Positions in metres, angles in degrees, in the arms' root space
// (the view): weapon = the gun slides/turns in the hands around the right fist; hip / ads = the whole arms + gun;
// mag = the magazine meshes on the clips' Magazine / MagazineSpare nodes.
// Saved in Resources/Elderboom/ArmsOffsets.json (in a build: a user file that overrides the shipped one).
public static class ElderboomArmsOffsets
{
    [System.Serializable]
    public sealed class Entry
    {
        public string key;
        public Vector3 weaponPos, weaponEuler, hipPos, hipEuler, adsPos, adsEuler, magPos, magEuler;
        public bool IsZero => weaponPos == Vector3.zero && weaponEuler == Vector3.zero && hipPos == Vector3.zero && hipEuler == Vector3.zero
                              && adsPos == Vector3.zero && adsEuler == Vector3.zero && magPos == Vector3.zero && magEuler == Vector3.zero;
    }
    [System.Serializable] sealed class Table { public List<Entry> entries = new List<Entry>(); }
    const string Resource = "Elderboom/ArmsOffsets";
    static Table table;
    static string UserPath => System.IO.Path.Combine(Application.persistentDataPath, "ArmsOffsets.json");

    public static Entry Get(string key)
    {
        if (table == null)
        {
            var text = Resources.Load<TextAsset>(Resource);
            table = text != null ? JsonUtility.FromJson<Table>(text.text) ?? new Table() : new Table();
#if !UNITY_EDITOR
            try
            {
                if (System.IO.File.Exists(UserPath))
                {
                    var user = JsonUtility.FromJson<Table>(System.IO.File.ReadAllText(UserPath));
                    if (user != null) foreach (var e in user.entries) { table.entries.RemoveAll(x => x.key == e.key); table.entries.Add(e); }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[ArmsOffsets] " + ex.Message); }
#endif
        }
        foreach (var e in table.entries) if (e.key == key) return e;
        var created = new Entry { key = key };
        table.entries.Add(created);
        return created;
    }

    public static void Save()
    {
        if (table == null) Get("*");
        table.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        var saved = new Table();
        foreach (var e in table.entries) if (!e.IsZero) saved.entries.Add(e);
        string json = JsonUtility.ToJson(saved, true);
#if UNITY_EDITOR
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "Resources/" + Resource + ".json"), json);
#else
        try { System.IO.File.WriteAllText(UserPath, json); } catch (System.Exception ex) { Debug.LogWarning("[ArmsOffsets] " + ex.Message); }
        try
        {
            var project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../Assets/Resources/" + Resource + ".json"));
            if (System.IO.File.Exists(project)) System.IO.File.WriteAllText(project, json);
        }
        catch (System.Exception ex) { Debug.LogWarning("[ArmsOffsets] " + ex.Message); }
#endif
    }
}
