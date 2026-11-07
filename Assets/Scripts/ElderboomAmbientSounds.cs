using UnityEngine;

// World ambience from the generated effects: generator in the church, fountains, the crashed helicopter,
// night wind over the valley and distant crows. Each emitter is skipped when its clip is not there.
public sealed class ElderboomAmbientSounds : MonoBehaviour
{
    float crowsAt;
    static ElderboomAmbientSounds instance;
    public static void Ensure() { if (instance == null) instance = new GameObject("Elderboom_Ambient_Sounds").AddComponent<ElderboomAmbientSounds>(); }

    void Start()
    {
        instance = this;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string n = t.name.ToLowerInvariant();
            if (n.StartsWith("village_fountain_") || n.StartsWith("wall_fountain_")) ElderboomAudio.Loop("ambience/fountain_water", t.position + Vector3.up, .55f, 1.5f, 14f, transform);
            else if (n == "cemetery_helicopter") ElderboomAudio.Loop("ambience/helicopter_cooling", t.position + Vector3.up * 1.5f, .45f, 2f, 16f, transform);
            // The generator hum belongs to ElderboomPowerGrid: it runs only once the generator is started.
        }
        ElderboomAudio.Loop("ambience/night_wind", Vector3.zero, .22f, 1f, 0f, transform);
        crowsAt = Time.time + Random.Range(15f, 30f);
    }

    void Update()
    {
        if (Time.time < crowsAt) return;
        crowsAt = Time.time + Random.Range(25f, 55f);
        var p = ElderboomGame.Active != null && ElderboomGame.Active.Player != null ? ElderboomGame.Active.Player.transform.position : Vector3.zero;
        var dir = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward;
        ElderboomAudio.PlayAny("ambience/crows", 1, p + dir * 35f + Vector3.up * 12f, .6f, true, .08f);
    }
}
