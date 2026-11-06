using System.Collections.Generic;
using UnityEngine;

// Action bindings, shared by the player, the pause menu and any UI that shows key
// names. Bindings are per user (PlayerPrefs "hdrp.bind.<action>"), so this is a
// static service rather than per-object state.
public static class ElderboomInput
{
    public static readonly (string id, string label, KeyCode key)[] DefaultBindings =
    {
        ("move_forward", "Move forward", KeyCode.W),
        ("move_back", "Move backward", KeyCode.S),
        ("move_left", "Move left", KeyCode.A),
        ("move_right", "Move right", KeyCode.D),
        ("jump", "Jump", KeyCode.Space),
        ("sprint", "Sprint", KeyCode.LeftShift),
        ("crouch", "Crouch", KeyCode.LeftControl),
        ("interact", "Interact", KeyCode.E),
        ("use_syringe", "Use syringe", KeyCode.H),
        ("reload", "Reload", KeyCode.R),
        ("shockwave", "Use selected ability", KeyCode.Q),
        ("cycle_ability", "Cycle selected ability", KeyCode.C),
        ("flashlight", "Flashlight", KeyCode.F),
        ("toggle_view", "First / third person", KeyCode.V),
        ("fire", "Fire", KeyCode.Mouse0),
        ("aim", "Aim", KeyCode.Mouse1)
    };

    static Dictionary<string, KeyCode> bindings;
    static readonly Dictionary<KeyCode, ElderboomButtonState> buttons = new Dictionary<KeyCode, ElderboomButtonState>();
    static int ignoreThroughFrame = -1;
    public static bool AcceptsInput => Application.isFocused && Time.frameCount > ignoreThroughFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        bindings = null; buttons.Clear(); ignoreThroughFrame = -1;
#if UNITY_EDITOR
        HeldOverride = null;
#endif
        Application.focusChanged -= OnFocusChanged;
        Application.focusChanged += OnFocusChanged;
    }

    static void OnFocusChanged(bool focused) => ReleaseAll();

    // Flush legacy events as well as our latches across focus, menu and session changes.
    // A key already held on return cannot resume an action without a new press.
    public static void ReleaseAll()
    {
        buttons.Clear(); ignoreThroughFrame = Time.frameCount + 1;
        Input.ResetInputAxes();
    }

    static Dictionary<string, KeyCode> Bindings
    {
        get
        {
            if (bindings != null) return bindings;
            bindings = new Dictionary<string, KeyCode>();
            foreach (var entry in DefaultBindings)
                bindings[entry.id] = (KeyCode)PlayerPrefs.GetInt("hdrp.bind." + entry.id, (int)entry.key);
            return bindings;
        }
    }

    public static KeyCode Get(string action) => Bindings.TryGetValue(action, out var key) ? key : KeyCode.None;
    public static string Label(string action) => KeyName(Get(action));

    public static bool Down(string action)
    {
        return AcceptsInput && Read(Get(action)).Pressed;
    }

    // Tools (Editor/ElderboomClaudeWeaponAudit) can hold an action without a key press.
#if UNITY_EDITOR
    public static System.Func<string, bool> HeldOverride;
#endif

    public static bool Held(string action)
    {
#if UNITY_EDITOR
        if (HeldOverride != null && HeldOverride(action)) return true;
#endif
        return AcceptsInput && Read(Get(action)).Held;
    }

    static ElderboomButtonState Read(KeyCode key)
    {
        if (!buttons.TryGetValue(key, out var state)) buttons[key] = state = new ElderboomButtonState();
        if (state.Frame == Time.frameCount) return state;
        bool held = false, down = false, up = false;
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
        {
            int button = key - KeyCode.Mouse0;
            held = Input.GetMouseButton(button); down = Input.GetMouseButtonDown(button); up = Input.GetMouseButtonUp(button);
        }
        else if (key != KeyCode.None)
        { held = Input.GetKey(key); down = Input.GetKeyDown(key); up = Input.GetKeyUp(key); }
        state.Sample(Time.frameCount, held, down, up, ElderboomNativeKeys.Read(key));
        return state;
    }

    // A key used by another action is swapped rather than duplicated.
    public static void Assign(string action, KeyCode key)
    {
        var map = Bindings;
        KeyCode previous = map[action];
        string other = null;
        foreach (var entry in map) if (entry.Key != action && entry.Value == key) { other = entry.Key; break; }
        if (other != null) map[other] = previous;
        map[action] = key;
        ReleaseAll();
    }

    public static void Save()
    {
        foreach (var entry in Bindings) PlayerPrefs.SetInt("hdrp.bind." + entry.Key, (int)entry.Value);
        PlayerPrefs.Save();
    }

    public static void RestoreDefaults()
    {
        var map = Bindings;
        foreach (var entry in DefaultBindings)
        {
            map[entry.id] = entry.key;
            PlayerPrefs.SetInt("hdrp.bind." + entry.id, (int)entry.key);
        }
        PlayerPrefs.Save();
        ReleaseAll();
    }

    public static string KeyName(KeyCode key)
    {
        if (key == KeyCode.Mouse0) return "Mouse Left";
        if (key == KeyCode.Mouse1) return "Mouse Right";
        if (key == KeyCode.Mouse2) return "Mouse Middle";
        if (key == KeyCode.LeftShift || key == KeyCode.RightShift) return "Shift";
        if (key == KeyCode.LeftControl || key == KeyCode.RightControl) return "Ctrl";
        return key.ToString();
    }
}

// One sample per key per frame: all consumers observe the same edge. Native confirmation
// can veto a stale legacy held state but can never synthesize a press by itself.
internal sealed class ElderboomButtonState
{
    bool armed;
    public int Frame { get; private set; } = -1;
    public bool Held { get; private set; }
    public bool Pressed { get; private set; }
    public void Sample(int frame, bool held, bool down, bool up, bool? physical)
    {
        if (Frame == frame) return;
        Frame = frame;
        Pressed = down && (physical != false || up); // preserve a complete fast tap
        if (Pressed) armed = true;
        if (!held || up || physical == false) armed = false;
        Held = armed && held;
    }
}
