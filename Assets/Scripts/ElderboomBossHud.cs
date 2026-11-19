using UnityEngine;

// Boss fight presentation: name and health bar at the top of the screen (rage mark at half health) and the
// boss music, which takes over from the normal night music until the boss dies.
public sealed class ElderboomBossHud : MonoBehaviour
{
    public static bool MusicOn { get; private set; }
    ElderboomZombie boss;
    AudioSource music;
    float shown = 1f, fadeOut = -1f, deadAt = -1f;
    static Texture2D white;

    public static void Show(ElderboomZombie zombie)
    {
        var hud = FindFirstObjectByType<ElderboomBossHud>() ?? new GameObject("ElderboomBossHud").AddComponent<ElderboomBossHud>();
        hud.boss = zombie; hud.shown = 1f; hud.fadeOut = -1f; hud.deadAt = -1f;
        if (hud.music == null)
        {
            hud.music = hud.gameObject.AddComponent<AudioSource>();
            hud.music.loop = true; hud.music.spatialBlend = 0f; hud.music.playOnAwake = false;
            hud.music.clip = ElderboomAudio.Clip("boss/boss_music");
        }
        if (hud.music.clip != null) { hud.music.volume = 0f; hud.music.Play(); }
        MusicOn = true;
    }

    float Target => Mathf.Clamp01(PlayerPrefs.GetFloat("hdrp.settings.music_volume", .8f)) * .5f;

    void Update()
    {
        bool alive = boss != null && boss.IsAlive;
        if (!alive && deadAt < 0f) { deadAt = Time.time; fadeOut = Time.time + 2.5f; }
        if (music != null && music.isPlaying)
        {
            float want = fadeOut > 0f && Time.time > fadeOut ? 0f : Target;
            music.volume = Mathf.MoveTowards(music.volume, want, Time.unscaledDeltaTime * (want > music.volume ? .35f : .2f));
            if (want == 0f && music.volume <= .001f) { music.Stop(); MusicOn = false; }
        }
        if (boss != null && boss.MaxHealth > 0f) shown = Mathf.MoveTowards(shown, Mathf.Clamp01(boss.Health / boss.MaxHealth), Time.deltaTime * .6f);
        if (deadAt > 0f && Time.time - deadAt > 6f && (music == null || !music.isPlaying)) Destroy(gameObject);
    }

    void OnDestroy() { MusicOn = false; }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || boss == null && deadAt < 0f) return;
        if (deadAt > 0f && Time.time - deadAt > 2f) return;
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        float s = Screen.height / 1080f;
        float w = 760f * s, h = 12f * s, x = (Screen.width - w) * .5f, y = 64f * s;
        var keep = GUI.color;
        var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
        title.normal.textColor = new Color(.93f, .9f, .86f);
        GUI.Label(new Rect(x, y - 38f * s, w, 32f * s), ElderboomZombie.BossName, title);
        GUI.color = new Color(0f, 0f, 0f, .6f); GUI.DrawTexture(new Rect(x - 2f * s, y - 2f * s, w + 4f * s, h + 4f * s), white);
        bool rage = boss != null && boss.BossEnraged;
        float frac = boss != null && boss.IsAlive ? Mathf.Clamp01(boss.Health / Mathf.Max(1f, boss.MaxHealth)) : 0f;
        GUI.color = new Color(1f, 1f, 1f, .5f); GUI.DrawTexture(new Rect(x, y, w * shown, h), white);
        GUI.color = rage ? new Color(.95f, .45f, .1f) : new Color(.84f, .1f, .18f); GUI.DrawTexture(new Rect(x, y, w * frac, h), white);
        GUI.color = new Color(1f, 1f, 1f, .8f); GUI.DrawTexture(new Rect(x + w * .5f - 1f, y - 3f * s, 2f, h + 6f * s), white); // rage threshold
        GUI.color = keep;
    }
}
