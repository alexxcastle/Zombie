using UnityEngine;

// In-game editor for the Tactical arms (ElderboomArmsOffsets): + (main keyboard or keypad) opens/closes it, everywhere.
// Tab picks what to move: the weapon in the hands, the whole arms + weapon for this weapon or for all of them, the aim,
// the magazine. Keys work in view space; Enter saves (Resources/Elderboom/ArmsOffsets.json).
public sealed class ElderboomArmsEditor : MonoBehaviour
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Enabled || FindFirstObjectByType<ElderboomArmsEditor>() != null) return;
        var go = new GameObject("ElderboomArmsEditor");
        DontDestroyOnLoad(go);
        go.AddComponent<ElderboomArmsEditor>();
    }

    static readonly string[] Targets =
    {
        "ARMA NELLE MANI (scorre e gira attorno alla mano destra)",
        "INQUADRATURA DI QUEST'ARMA (braccia + arma)",
        "INQUADRATURA DI TUTTE LE ARMI (braccia + arma)",
        "MIRA (tieni il tasto destro)",
        "CARICATORE",
    };
    bool editing; int target; string status = "";
    HDRPSlicePlayer cachedPlayer;
    HDRPSlicePlayer Player
    {
        get
        {
            if (cachedPlayer == null || !cachedPlayer.isActiveAndEnabled) cachedPlayer = FindFirstObjectByType<HDRPSlicePlayer>();
            return cachedPlayer;
        }
    }

    static bool TogglePressed()
    {
        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Plus)) return true;
        string typed = Input.inputString;   // the + key wherever the layout puts it (Italian keyboard included)
        return !string.IsNullOrEmpty(typed) && typed.IndexOf('+') >= 0;
    }

    static ElderboomArmsDriver ActiveArms(HDRPSlicePlayer player)
    {
        var ars = player.GetComponent<ElderboomArsenal>(); if (ars == null) return null;
        var f = typeof(ElderboomArsenal).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slot = f?.GetValue(ars);
        var root = slot?.GetType().GetField("root")?.GetValue(slot) as Transform;
        return root != null ? root.GetComponent<ElderboomArmsDriver>() : null;
    }

    void Update()
    {
        if (!Enabled) return;
        var player = Player;
        if (TogglePressed()) editing = !editing && player != null;
        if (!editing) return;
        if (player == null || !player.enabled || player.SettingsOpen) { editing = false; return; }
        Edit(player);
    }

    void Edit(HDRPSlicePlayer player)
    {
        if (Input.GetKeyDown(KeyCode.Tab)) target = (target + 1) % Targets.Length;
        var drv = ActiveArms(player);
        if (drv == null || drv.Arms == null) { status = "Quest'arma non usa le braccia nuove."; return; }
        var cam = player.GetComponentInChildren<Camera>();
        var space = drv.Arms.parent != null ? drv.Arms.parent : drv.Arms; // the weapon root, under the camera
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float move = (fine ? .01f : .05f) * Time.deltaTime, turn = (fine ? 6f : 30f) * Time.deltaTime;
        Vector3 dp = new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * move;
        Vector3 de = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * turn;
        if (cam != null && dp != Vector3.zero) dp = space.InverseTransformDirection(cam.transform.TransformDirection(dp));
        var e = ElderboomArmsOffsets.Get(target == 2 ? "*" : drv.weaponId);
        string what;
        switch (target)
        {
            case 0: e.weaponPos += dp; e.weaponEuler = Turn(space, cam, e.weaponEuler, de); if (Reset()) { e.weaponPos = e.weaponEuler = Vector3.zero; } what = "arma pos " + e.weaponPos.ToString("F3") + " rot " + e.weaponEuler.ToString("F1"); break;
            case 1: case 2: e.hipPos += dp; e.hipEuler = Turn(space, cam, e.hipEuler, de); if (Reset()) { e.hipPos = e.hipEuler = Vector3.zero; } what = "inquadratura pos " + e.hipPos.ToString("F3") + " rot " + e.hipEuler.ToString("F1"); break;
            case 3: e.adsPos += dp; e.adsEuler = Turn(space, cam, e.adsEuler, de); if (Reset()) { e.adsPos = e.adsEuler = Vector3.zero; } what = "mira pos " + e.adsPos.ToString("F3") + " rot " + e.adsEuler.ToString("F1"); break;
            default: e.magPos += dp; e.magEuler = Turn(space, cam, e.magEuler, de); if (Reset()) { e.magPos = e.magEuler = Vector3.zero; } what = "caricatore pos " + e.magPos.ToString("F3") + " rot " + e.magEuler.ToString("F1"); break;
        }
        string key = target == 2 ? "tutte le armi" : drv.weaponId;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) status = "Salvato (" + key + ") in ArmsOffsets.json.";
        else status = key + "   " + what;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ElderboomArmsOffsets.Save();
    }

    static bool Reset() => Input.GetKeyDown(KeyCode.Backspace);

    // a turn about the view's axes, added to an offset kept about the weapon root's axes
    static Vector3 Turn(Transform space, Camera cam, Vector3 euler, Vector3 viewDelta)
    {
        if (viewDelta == Vector3.zero) return euler;
        Quaternion world = cam != null ? cam.transform.rotation * Quaternion.Euler(viewDelta) * Quaternion.Inverse(cam.transform.rotation) : Quaternion.Euler(viewDelta);
        var local = Quaternion.Inverse(space.rotation) * world * space.rotation;
        var r = (local * Quaternion.Euler(euler)).eulerAngles;
        return new Vector3(Mathf.DeltaAngle(0f, r.x), Mathf.DeltaAngle(0f, r.y), Mathf.DeltaAngle(0f, r.z));
    }

    static float Axis(KeyCode plus, KeyCode minus) => (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f);

    void OnGUI()
    {
        if (!Enabled || !editing) return;
        var r = new Rect(16f, Screen.height * .5f - 150f, 540f, 300f);
        GUI.color = new Color(1f, 1f, 1f, .92f);
        GUI.Box(r, "");
        var st = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
        GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width - 24f, r.height - 16f),
            "<b>REGOLA BRACCIA E ARMA</b>   (+ per uscire)\n<b>Tab</b>: " + Targets[target]
            + "\n<b>J/L</b> sinistra/destra · <b>U/O</b> giù/su · <b>K/I</b> indietro/avanti"
            + "\n<b>frecce</b> inclina/gira · <b>Z/X</b> rolla · <b>Shift</b> fine"
            + "\n<b>tasto destro</b> mira · <b>Backspace</b> azzera · <b>Invio</b> salva"
            + "\n\n" + status, st);
    }
}
