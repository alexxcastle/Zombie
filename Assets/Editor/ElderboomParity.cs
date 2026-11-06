using UnityEditor;

public static class ElderboomParity
{
    public static void Apply()
    {
        ElderboomPortBuilder.RebuildZombies();
        ElderboomViewmodelCalibrator.Run();
        DebugLog();
    }

    static void DebugLog()
    {
        UnityEngine.Debug.Log("ELDERBOOM_PARITY_APPLIED");
    }
}
