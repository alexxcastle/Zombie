using UnityEngine;

public static class ElderboomDisplaySettings
{
    public const string FullscreenKey="hdrp.settings.fullscreen";
    // "resolution2": the list gained 854x480 at the start, so the old saved index would point one size off.
    public const string ResolutionKey="hdrp.settings.resolution2";
    public const string QualityKey="hdrp.settings.quality";
    public static readonly Vector2Int[] Resolutions={new Vector2Int(854,480),new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1664)};
    // Quality: 0 LOW, 1 MEDIUM, 2 HIGH (Unity levels 0-2), 3 PERFORMANCE (level 0 plus ElderboomPerformanceMode).
    public const int Performance=3;
    public static int Quality => Mathf.Clamp(PlayerPrefs.GetInt(QualityKey,1),0,Performance);
    public static int UnityLevel(int quality) => quality>=Performance?0:Mathf.Clamp(quality,0,Mathf.Min(2,QualitySettings.names.Length-1));
    public static bool Fullscreen => PlayerPrefs.GetInt(FullscreenKey,1)==1;
    public static int Resolution => Mathf.Clamp(PlayerPrefs.GetInt(ResolutionKey,2),0,Resolutions.Length-1);
    public static void ApplySaved()
    {
        int quality=UnityLevel(Quality);
        if(QualitySettings.names.Length>0 && QualitySettings.GetQualityLevel()!=quality)QualitySettings.SetQualityLevel(quality,true);
        ElderboomPerformanceMode.Set(Quality==Performance);
        if(Application.isEditor)return;
        var size=Resolutions[Resolution];
        Screen.SetResolution(size.x,size.y,Fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
    }
    public static void SaveDisplay(bool fullscreen,int resolution,int quality)
    {
        PlayerPrefs.SetInt(FullscreenKey,fullscreen?1:0);
        PlayerPrefs.SetInt(ResolutionKey,Mathf.Clamp(resolution,0,Resolutions.Length-1));
        PlayerPrefs.SetInt(QualityKey,Mathf.Clamp(quality,0,Performance));PlayerPrefs.Save();
    }
}
