using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/26 Final Shots")]
    static void FinalShots()
    {
        var restore = Daylight();
        try
        {
            Shot("Logs/rework_shots/fin_1_bridge_south.png", new Vector3(7, 0, -40), new Vector3(10, 1, -62), 80f);
            Shot("Logs/rework_shots/fin_2_via_di_mezzo.png", new Vector3(-36, 0, -73), new Vector3(10, 3, -78), 75f);
            Shot("Logs/rework_shots/fin_3_weavers.png", new Vector3(-3, 0, -73), new Vector3(-13, 4, -87), 80f);
            Shot("Logs/rework_shots/fin_4_woods_street.png", new Vector3(41, 0, 50.5f), new Vector3(80, 2, 52), 75f);
            Shot("Logs/rework_shots/fin_5_square_south.png", new Vector3(-2, 0, -24), new Vector3(28, 2, -38), 85f);
            Shot("Logs/rework_shots/fin_6_maestra.png", new Vector3(-36, 0, 1), new Vector3(0, 3, 8), 80f);
        }
        finally { restore(); }
    }
}
