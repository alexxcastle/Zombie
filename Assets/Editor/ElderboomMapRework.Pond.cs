using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/11 Pond Shots")]
    static void PondShots()
    {
        var restore = Daylight();
        try
        {
            Shot("Logs/rework_shots/pond_ground.png", new Vector3(-18, 0, 22), new Vector3(-30, .2f, 29.4f));
            Shot("Logs/rework_shots/pond_high.png", new Vector3(-16, 0, 16), new Vector3(-30, .1f, 29.4f), 60f, 14f);
        }
        finally { restore(); }
    }
}
