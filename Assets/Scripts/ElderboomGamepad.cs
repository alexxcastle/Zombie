using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
#endif

// Gamepad support (Xbox, PlayStation, Switch Pro and generic pads, on Mac and Windows) through the Input System
// package. Keyboard and mouse keep using the legacy Input Manager: Player Settings > Active Input Handling = Both.
// ElderboomInput.Down/Held/Label already include the pad, so every existing action works with it.
//
// Two layouts, each rebindable in Pause > Controls (shown while a pad is connected), saved per user:
//
//   SHOOTER (gun operators)                      MELEE (Steel Sentinel, third person)
//   Left stick  move  ·  Right stick  look       same
//   L3  sprint (toggle)                          L3  sprint (toggle)
//   A / Cross   jump                             A / Cross     jump
//   B / Circle  crouch, slide while sprinting    B / Circle    dodge
//   X / Square  tap reload, hold interact        X / Square    hold: interact
//   Y / Triangle next weapon                     Y / Triangle  split / join the staff
//   LT / L2  aim     RT / R2  fire               RB / R1  light combo   RT / R2  heavy attack
//   LB / L1  cycle ability  RB / R1 use ability  LB / L1  use ability   D-pad right  cycle ability
//   D-pad up syringe · left flashlight · down view    D-pad up syringe · left flashlight
//   Start / Options  pause        Menus: D-pad or left stick to move, A select, B back.
//
// A button may carry "interact" and one other action: a tap does the other action, a hold interacts.
public static class ElderboomGamepad
{
    public enum PadButton { None, South, East, West, North, LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, LeftStick, RightStick, DpadUp, DpadDown, DpadLeft, DpadRight, Select }
    public enum Layout { Shooter, Melee }

    // Actions the pad can trigger, with their menu label per layout (null = not used by that layout).
    public static readonly (string id, string shooter, string melee)[] Actions =
    {
        ("fire", "Fire", "Light attack / combo"),
        ("aim", "Aim", "Heavy attack"),
        ("jump", "Jump", "Jump"),
        ("crouch", "Crouch / slide", "Dodge"),
        ("reload", "Reload", "Split / join the staff"),
        ("interact", "Interact (hold)", "Interact (hold)"),
        ("sprint", "Sprint (toggle)", "Sprint (toggle)"),
        ("shockwave", "Use ability", "Use ability"),
        ("cycle_ability", "Cycle ability", "Cycle ability"),
        ("next_weapon", "Next weapon", null),
        ("use_syringe", "Use syringe", "Use syringe"),
        ("flashlight", "Flashlight", "Flashlight"),
        ("toggle_view", "First / third person", null),
    };

    static readonly Dictionary<string, PadButton> ShooterDefaults = new Dictionary<string, PadButton>
    {
        { "fire", PadButton.RightTrigger }, { "aim", PadButton.LeftTrigger }, { "jump", PadButton.South }, { "crouch", PadButton.East },
        { "reload", PadButton.West }, { "interact", PadButton.West }, { "sprint", PadButton.LeftStick }, { "shockwave", PadButton.RightShoulder },
        { "cycle_ability", PadButton.LeftShoulder }, { "next_weapon", PadButton.North }, { "use_syringe", PadButton.DpadUp },
        { "flashlight", PadButton.DpadLeft }, { "toggle_view", PadButton.DpadDown },
    };
    static readonly Dictionary<string, PadButton> MeleeDefaults = new Dictionary<string, PadButton>
    {
        { "fire", PadButton.RightShoulder }, { "aim", PadButton.RightTrigger }, { "jump", PadButton.South }, { "crouch", PadButton.East },
        { "reload", PadButton.North }, { "interact", PadButton.West }, { "sprint", PadButton.LeftStick }, { "shockwave", PadButton.LeftShoulder },
        { "cycle_ability", PadButton.DpadRight }, { "next_weapon", PadButton.None }, { "use_syringe", PadButton.DpadUp },
        { "flashlight", PadButton.DpadLeft }, { "toggle_view", PadButton.None },
    };

    // Set by ElderboomSentinel while the melee operator is in play.
    public static Layout Active { get; set; } = Layout.Shooter;

    static readonly Dictionary<string, PadButton>[] maps = new Dictionary<string, PadButton>[2];
    static string Key(Layout layout, string action) => "hdrp.pad." + (layout == Layout.Melee ? "melee." : "shooter.") + action;
    static Dictionary<string, PadButton> Defaults(Layout layout) => layout == Layout.Melee ? MeleeDefaults : ShooterDefaults;

    static Dictionary<string, PadButton> Map(Layout layout)
    {
        int i = (int)layout;
        if (maps[i] != null) return maps[i];
        var map = new Dictionary<string, PadButton>();
        foreach (var pair in Defaults(layout)) map[pair.Key] = (PadButton)PlayerPrefs.GetInt(Key(layout, pair.Key), (int)pair.Value);
        return maps[i] = map;
    }

    public static PadButton Binding(Layout layout, string action) => Map(layout).TryGetValue(action, out var b) ? b : PadButton.None;
    static PadButton Bound(string action) => Binding(Active, action);

    public static string ActionLabel(Layout layout, string action)
    {
        foreach (var a in Actions) if (a.id == action) return layout == Layout.Melee ? a.melee : a.shooter;
        return null;
    }

    // A button already used by another action is swapped; "interact" may share a button with one tap action.
    public static void Assign(Layout layout, string action, PadButton button)
    {
        var map = Map(layout);
        PadButton previous = Binding(layout, action);
        if (button != PadButton.None)
            foreach (var other in new List<string>(map.Keys))
            {
                if (other == action || map[other] != button) continue;
                if (other == "interact" || action == "interact")
                {
                    // sharing is allowed once: a second tap action on the interact button gets swapped
                    bool tapTaken = false;
                    foreach (var pair in map) if (pair.Key != "interact" && pair.Key != action && pair.Key != other && pair.Value == button) tapTaken = true;
                    if (!tapTaken) continue;
                }
                map[other] = previous;
            }
        map[action] = button;
        Save(layout);
    }

    public static void Save(Layout layout)
    {
        foreach (var pair in Map(layout)) PlayerPrefs.SetInt(Key(layout, pair.Key), (int)pair.Value);
        PlayerPrefs.Save();
    }

    public static void RestoreDefaults(Layout layout)
    {
        maps[(int)layout] = new Dictionary<string, PadButton>(Defaults(layout));
        Save(layout);
    }

    const float StickDeadzone = .18f, LookDeadzone = .12f, HoldToInteract = .3f;
    const float YawSpeed = 210f, PitchSpeed = 150f; // degrees per second at full tilt, sensitivity 1.8

    // True once the pad was used, false again after keyboard or mouse input: drives the button prompts.
    public static bool LastUsed { get; private set; }

    public static string ButtonName(PadButton b)
    {
#if ENABLE_INPUT_SYSTEM
        bool ps = IsPlayStation;
#else
        bool ps = false;
#endif
        switch (b)
        {
            case PadButton.South: return ps ? "CROSS" : "A";
            case PadButton.East: return ps ? "CIRCLE" : "B";
            case PadButton.West: return ps ? "SQUARE" : "X";
            case PadButton.North: return ps ? "TRIANGLE" : "Y";
            case PadButton.LeftShoulder: return ps ? "L1" : "LB";
            case PadButton.RightShoulder: return ps ? "R1" : "RB";
            case PadButton.LeftTrigger: return ps ? "L2" : "LT";
            case PadButton.RightTrigger: return ps ? "R2" : "RT";
            case PadButton.LeftStick: return "L3";
            case PadButton.RightStick: return "R3";
            case PadButton.DpadUp: return "D-PAD UP";
            case PadButton.DpadDown: return "D-PAD DOWN";
            case PadButton.DpadLeft: return "D-PAD LEFT";
            case PadButton.DpadRight: return "D-PAD RIGHT";
            case PadButton.Select: return ps ? "SHARE" : "VIEW";
        }
        return "—";
    }

#if ENABLE_INPUT_SYSTEM
    static int frame = -1;
    static bool sprintLatched;
    const int ButtonCount = 16;
    static readonly float[] downAt = new float[ButtonCount];
    static readonly bool[] holdFired = new bool[ButtonCount], tapPulse = new bool[ButtonCount], holdPulse = new bool[ButtonCount];
    static float fullTiltSince = -1f, rumbleUntil;

    static Gamepad Pad => Gamepad.current;
    public static bool Connected => Pad != null;
    static bool IsPlayStation => Pad is DualShockGamepad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        frame = -1; sprintLatched = false; maps[0] = maps[1] = null; Active = Layout.Shooter;
        for (int i = 0; i < ButtonCount; i++) { downAt[i] = -1f; holdFired[i] = tapPulse[i] = holdPulse[i] = false; }
        fullTiltSince = -1f; rumbleUntil = 0f; LastUsed = false;
        Application.focusChanged -= OnFocus; Application.focusChanged += OnFocus;
        Application.quitting -= StopRumble; Application.quitting += StopRumble;
    }

    static void OnFocus(bool focused)
    {
        if (focused) return;
        StopRumble(); sprintLatched = false;
        for (int i = 0; i < ButtonCount; i++) downAt[i] = -1f;
    }

    static ButtonControl Control(Gamepad p, PadButton b)
    {
        switch (b)
        {
            case PadButton.South: return p.buttonSouth;
            case PadButton.East: return p.buttonEast;
            case PadButton.West: return p.buttonWest;
            case PadButton.North: return p.buttonNorth;
            case PadButton.LeftShoulder: return p.leftShoulder;
            case PadButton.RightShoulder: return p.rightShoulder;
            case PadButton.LeftTrigger: return p.leftTrigger;
            case PadButton.RightTrigger: return p.rightTrigger;
            case PadButton.LeftStick: return p.leftStickButton;
            case PadButton.RightStick: return p.rightStickButton;
            case PadButton.DpadUp: return p.dpad.up;
            case PadButton.DpadDown: return p.dpad.down;
            case PadButton.DpadLeft: return p.dpad.left;
            case PadButton.DpadRight: return p.dpad.right;
            case PadButton.Select: return p.selectButton;
        }
        return null;
    }

    // The button carries interact plus a tap action: tap and hold are told apart.
    static bool Shared(PadButton b)
    {
        if (b == PadButton.None || Bound("interact") != b) return false;
        foreach (var pair in Map(Active)) if (pair.Key != "interact" && pair.Value == b) return true;
        return false;
    }

    // One update per frame, whoever asks first.
    static void Tick()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        for (int i = 0; i < ButtonCount; i++) tapPulse[i] = holdPulse[i] = false;
        float now = Time.unscaledTime;
        if (rumbleUntil > 0f && now >= rumbleUntil) StopRumble();
        var kb = Keyboard.current; var mouse = Mouse.current;
        if ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame)))
            LastUsed = false;
        var pad = Pad;
        if (pad == null) { sprintLatched = false; for (int i = 0; i < ButtonCount; i++) downAt[i] = -1f; return; }
        if (pad.leftStick.ReadValue().sqrMagnitude > .25f || pad.rightStick.ReadValue().sqrMagnitude > .25f || PressedButton() != PadButton.None) LastUsed = true;

        // interact button: a tap fires the shared action on release, a hold interacts once the hold time is reached
        var ib = Bound("interact");
        var ic = ib != PadButton.None ? Control(pad, ib) : null;
        if (ic != null)
        {
            int i = (int)ib; bool shared = Shared(ib);
            if (ic.wasPressedThisFrame) { downAt[i] = now; holdFired[i] = false; }
            if (ic.isPressed && downAt[i] >= 0f && !holdFired[i] && (!shared || now - downAt[i] >= HoldToInteract)) { holdPulse[i] = true; holdFired[i] = true; }
            if (ic.wasReleasedThisFrame) { if (shared && downAt[i] >= 0f && !holdFired[i]) tapPulse[i] = true; downAt[i] = -1f; }
        }

        // sprint on a stick click toggles; it ends when the stick is let go or pulled back, or on crouch / dodge
        var sb = Bound("sprint");
        if (sb == PadButton.LeftStick || sb == PadButton.RightStick)
        {
            var sc = Control(pad, sb);
            if (sc.wasPressedThisFrame) sprintLatched = !sprintLatched;
            var cb = Bound("crouch"); var cc = cb != PadButton.None ? Control(pad, cb) : null;
            if (Move.y < .1f || (cc != null && cc.wasPressedThisFrame)) sprintLatched = false;
        }
        else sprintLatched = false;
    }

    // First button pressed this frame (Start excluded: it pauses). Used by the rebinding page.
    public static PadButton PressedButton()
    {
        var p = Pad; if (p == null) return PadButton.None;
        for (int b = 1; b < ButtonCount; b++) { var c = Control(p, (PadButton)b); if (c != null && c.wasPressedThisFrame) return (PadButton)b; }
        return PadButton.None;
    }

    public static bool AnyButtonHeld()
    {
        var p = Pad; if (p == null) return false;
        for (int b = 1; b < ButtonCount; b++) { var c = Control(p, (PadButton)b); if (c != null && c.isPressed) return true; }
        return p.startButton.isPressed;
    }

    // Left stick with a radial deadzone, rescaled so movement starts smoothly at the edge of the deadzone.
    public static Vector2 Move
    {
        get
        {
            var pad = Pad; if (pad == null) return Vector2.zero;
            Vector2 v = pad.leftStick.ReadValue(); float m = v.magnitude;
            if (m < StickDeadzone) return Vector2.zero;
            return v / m * Mathf.Clamp01((m - StickDeadzone) / (1f - StickDeadzone));
        }
    }

    // Degrees to turn this frame: x = yaw (right +), y = pitch (up +, like the mouse's Y axis).
    public static Vector2 Look(float sensitivity, bool aiming)
    {
        Tick();
        var pad = Pad; if (pad == null) return Vector2.zero;
        Vector2 v = pad.rightStick.ReadValue(); float m = v.magnitude;
        if (m < LookDeadzone) { fullTiltSince = -1f; return Vector2.zero; }
        float t = Mathf.Clamp01((m - LookDeadzone) / (1f - LookDeadzone));
        Vector2 dir = v / m * Mathf.Pow(t, 1.8f); // fine control near the centre
        // Held at full tilt: turn faster after a moment (quick 180s), as console shooters do.
        float boost = 1f;
        if (t > .95f) { if (fullTiltSince < 0f) fullTiltSince = Time.unscaledTime; boost = 1f + .7f * Mathf.Clamp01((Time.unscaledTime - fullTiltSince - .2f) / .35f); }
        else fullTiltSince = -1f;
        float scale = Mathf.Max(.2f, sensitivity / 1.8f) * (aiming ? .5f : 1f) * Time.unscaledDeltaTime;
        return new Vector2(dir.x * YawSpeed * boost * scale, dir.y * PitchSpeed * scale);
    }

    public static bool Down(string action)
    {
        Tick();
        var p = Pad; if (p == null) return false;
        var b = Bound(action); if (b == PadButton.None) return false;
        int i = (int)b;
        if (action == "interact") return holdPulse[i];
        if (Shared(b)) return tapPulse[i];
        return Control(p, b).wasPressedThisFrame;
    }

    public static bool Held(string action)
    {
        Tick();
        var p = Pad; if (p == null) return false;
        switch (action)
        {
            case "move_forward": return Move.y > .5f;
            case "move_back": return Move.y < -.5f;
            case "move_left": return Move.x < -.5f;
            case "move_right": return Move.x > .5f;
        }
        var b = Bound(action); if (b == PadButton.None) return false;
        if (action == "sprint" && (b == PadButton.LeftStick || b == PadButton.RightStick)) return sprintLatched;
        if (action == "interact") return Control(p, b).isPressed && holdFired[(int)b];
        if (Shared(b)) return false; // the tap action of a shared button is a press, not a hold
        return Control(p, b).isPressed;
    }

    public static bool NextWeaponDown => Down("next_weapon");
    public static bool PauseDown { get { Tick(); return Pad != null && Pad.startButton.wasPressedThisFrame; } }
    public static bool BackDown { get { Tick(); return Pad != null && Pad.buttonEast.wasPressedThisFrame; } }

    public static string Label(string action)
    {
        switch (action)
        {
            case "move_forward": case "move_back": case "move_left": case "move_right": return "LEFT STICK";
        }
        var b = Bound(action);
        if (b == PadButton.None) return null;
        string name = ButtonName(b);
        if (action == "interact" && Shared(b)) return "HOLD " + name;
        return name;
    }

    // Short vibration, e.g. on a shot or when hurt. Both motors in 0..1.
    public static void Rumble(float low, float high, float seconds)
    {
        var pad = Pad;
        if (pad == null || !LastUsed || !Application.isFocused) return;
        pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
        rumbleUntil = Mathf.Max(rumbleUntil, Time.unscaledTime + seconds);
    }

    public static void StopRumble()
    {
        rumbleUntil = 0f;
        try { Pad?.SetMotorSpeeds(0f, 0f); } catch { }
    }
#else
    public static bool Connected => false;
    public static Vector2 Move => Vector2.zero;
    public static Vector2 Look(float sensitivity, bool aiming) => Vector2.zero;
    public static bool Down(string action) => false;
    public static bool Held(string action) => false;
    public static bool NextWeaponDown => false;
    public static bool PauseDown => false;
    public static bool BackDown => false;
    public static PadButton PressedButton() => PadButton.None;
    public static bool AnyButtonHeld() => false;
    public static string Label(string action) => null;
    public static void Rumble(float low, float high, float seconds) { }
    public static void StopRumble() { }
#endif
}
