using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/18 Relocation Shots")]
    static void RelocationShots()
    {
        var restore = Daylight();
        try
        {
            Shot("Logs/rework_shots/reloc_A_west.png", new Vector3(-26, 0, -1), new Vector3(-10, 3, -15), 75f);
            Shot("Logs/rework_shots/reloc_B_south.png", new Vector3(12, 0, -24), new Vector3(20, 2, -36), 80f);
            Shot("Logs/rework_shots/reloc_C_farm.png", new Vector3(-45, 0, -8), new Vector3(-58, 1, -8), 75f);
        }
        finally { restore(); }
    }
}
