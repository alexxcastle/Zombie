using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/32 Aesthetic Shots")]
    static void AesShots()
    {
        var restore = Daylight();
        try
        {
            Shot("Logs/rework_shots/aes_1_front_square.png", new Vector3(-22, 0, -12.5f), new Vector3(-2, 3, -12.5f), 85f, 3f);
            Shot("Logs/rework_shots/aes_2_courtyard.png", new Vector3(47, 0, -1), new Vector3(47, 1, -18), 80f, 3f);
            Shot("Logs/rework_shots/aes_3_north_wing.png", new Vector3(-2, 0, 9), new Vector3(30, 1, 2), 80f, 2.2f);
            Shot("Logs/rework_shots/aes_4_south_wing.png", new Vector3(-3, 0, -34), new Vector3(30, 1, -27), 80f, 2.2f);
            Shot("Logs/rework_shots/aes_5_bridge.png", new Vector3(2, 0, -32), new Vector3(7, 0, -55), 75f, 2.5f);
            Shot("Logs/rework_shots/aes_6_maestra.png", new Vector3(-36, 0, 0), new Vector3(0, 2, 8), 80f, 2f);
            Shot("Logs/rework_shots/aes_7_creek_street.png", new Vector3(-36, 0, -73), new Vector3(10, 2, -76), 75f, 2f);
            Shot("Logs/rework_shots/aes_8_top.png", new Vector3(5, 0, -5), new Vector3(5.1f, 0, -4.9f), 70f, 190f, 1600, 1600);
        }
        finally { restore(); }
    }
}
