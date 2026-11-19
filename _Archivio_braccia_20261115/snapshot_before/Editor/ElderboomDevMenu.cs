using System.Reflection;
using UnityEditor;
using UnityEngine;

// Play-mode shortcuts for reviewing screens without playing through them.
static class ElderboomDevMenu
{
    const string Root = "Elderboom HDRP/Dev/";

    [MenuItem(Root + "Start Session (Soph)")] static void StartSoph() => Start(ElderboomIds.Soph);
    [MenuItem(Root + "Start Session (Eli)")] static void StartEli() => Start(ElderboomIds.Eli);
    [MenuItem(Root + "Start Session (Tactical)")] static void StartTactical() => Start(ElderboomIds.Tactical);
    static void Start(string op) { if (ElderboomGame.Active != null) ElderboomGame.Active.BeginSession(op, "NORMAL"); }

    [MenuItem(Root + "Toggle Pause")]
    static void TogglePause()
    {
        var pause = Object.FindAnyObjectByType<ElderboomPauseMenu>();
        if (pause == null) return;
        typeof(ElderboomPauseMenu).GetMethod("SetSettingsOpen", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pause, new object[] { !pause.IsOpen });
    }

    [MenuItem(Root + "Pause Page/Game")] static void PageGame() => Page("pause");
    [MenuItem(Root + "Pause Page/Controls")] static void PageControls() => Page("controls");
    [MenuItem(Root + "Pause Page/Video")] static void PageVideo() => Page("video");
    [MenuItem(Root + "Pause Page/Audio")] static void PageAudio() => Page("audio");
    static void Page(string id)
    {
        var pause = Object.FindAnyObjectByType<ElderboomPauseMenu>();
        if (pause != null) typeof(ElderboomPauseMenu).GetField("pausePage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pause, id);
    }

    [MenuItem(Root + "Kill Player (Game Over)")]
    static void Kill() { var p = Object.FindAnyObjectByType<HDRPSlicePlayer>(); if (p != null) p.TakeDamage(1000f); }

    // Equips the next catalog weapon (skipping operator primaries) to review grips one by one.
    static int nextWeapon;
    [MenuItem(Root + "Give Next Weapon")]
    static void GiveNext()
    {
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>();
        var arsenal = player != null ? player.GetComponent<ElderboomArsenal>() : null;
        if (arsenal == null) return;
        var all = ElderboomWeapons.All;
        for (int tries = 0; tries < all.Length; tries++)
        {
            var w = all[nextWeapon++ % all.Length];
            if (w.IsOperatorPrimary || w.id == "knife" || arsenal.Contains(w.id)) continue;
            bool ok = arsenal.Add(w);
            Debug.Log("[Elderboom] Give " + w.id + " -> " + ok);
            return;
        }
    }

    [MenuItem(Root + "Toggle Invulnerable")]
    static void Invulnerable() { HDRPSlicePlayer.DevInvulnerable = !HDRPSlicePlayer.DevInvulnerable; Debug.Log("[Elderboom] Invulnerable: " + HDRPSlicePlayer.DevInvulnerable); }

    [MenuItem(Root + "Log Active Weapon Hierarchy")]
    static void LogWeapon()
    {
        var player = Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null || player.ActiveWeaponRoot == null) return;
        var sb = new System.Text.StringBuilder();
        var root = player.ActiveWeaponRoot;
        sb.AppendLine("ACTIVE " + root.name + " active=" + root.gameObject.activeInHierarchy + " pos=" + root.localPosition.ToString("F3") + " scale=" + root.lossyScale.ToString("F3"));
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            sb.AppendLine((r.enabled && r.gameObject.activeInHierarchy ? "  ON  " : "  off ") + r.name + " boundsCenter=" + player.ViewCamera.transform.InverseTransformPoint(r.bounds.center).ToString("F3") + " size=" + r.bounds.size.ToString("F3"));
        System.IO.File.WriteAllText("Logs/active_weapon.txt", sb.ToString());
        Debug.Log("[Elderboom] wrote Logs/active_weapon.txt");
    }

    [MenuItem(Root + "Turn Player 90°")]
    static void Turn() { var p = Object.FindAnyObjectByType<HDRPSlicePlayer>(); if (p != null) p.transform.Rotate(0f, 90f, 0f); }

    [MenuItem(Root + "Load Main Menu Scene")]
    static void LoadMenu() { if (Application.isPlaying) UnityEngine.SceneManagement.SceneManager.LoadScene("ZombieTest_Menu"); }

    [MenuItem(Root + "Main Menu Page/Home")] static void MHome() => MenuPage("Home");
    [MenuItem(Root + "Main Menu Page/Operators")] static void MOps() => MenuPage("Operators");
    [MenuItem(Root + "Main Menu Page/Difficulty")] static void MDiff() => MenuPage("Difficulty");
    [MenuItem(Root + "Main Menu Page/Settings")] static void MSet() => MenuPage("Settings");
    static void MenuPage(string page)
    {
        var menu = Object.FindAnyObjectByType<ElderboomMenu>();
        if (menu == null) return;
        var type = typeof(ElderboomMenu);
        var pageType = type.GetNestedType("Page", BindingFlags.NonPublic);
        type.GetMethod("GoTo", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, new[] { System.Enum.Parse(pageType, page) });
    }
}
