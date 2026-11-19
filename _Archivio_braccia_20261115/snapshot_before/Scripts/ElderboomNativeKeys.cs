using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Stuck keys guard. Unity's legacy input keeps a key "held" when its release event is lost, which happens on
// heavy frames and when the window or graphics settings change (the player kept walking backwards, the gun kept
// firing). A held key is now confirmed against the real keyboard/mouse state from the operating system
// (macOS CGEventSourceKeyState, Windows GetAsyncKeyState). A supported OS release is authoritative,
// including before the first press. Unsupported keys use the fresh-press latch in ElderboomInput.
public static class ElderboomNativeKeys
{
    static bool broken;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => broken = false;
    static readonly Dictionary<KeyCode, int> mac = new Dictionary<KeyCode, int>
    {
        {KeyCode.A,0},{KeyCode.S,1},{KeyCode.D,2},{KeyCode.F,3},{KeyCode.H,4},{KeyCode.G,5},{KeyCode.Z,6},{KeyCode.X,7},
        {KeyCode.C,8},{KeyCode.V,9},{KeyCode.B,11},{KeyCode.Q,12},{KeyCode.W,13},{KeyCode.E,14},{KeyCode.R,15},{KeyCode.Y,16},
        {KeyCode.T,17},{KeyCode.Alpha1,18},{KeyCode.Alpha2,19},{KeyCode.Alpha3,20},{KeyCode.Alpha4,21},{KeyCode.Alpha6,22},
        {KeyCode.Alpha5,23},{KeyCode.Alpha9,25},{KeyCode.Alpha7,26},{KeyCode.Alpha8,28},{KeyCode.Alpha0,29},{KeyCode.O,31},
        {KeyCode.U,32},{KeyCode.I,34},{KeyCode.P,35},{KeyCode.L,37},{KeyCode.J,38},{KeyCode.K,40},{KeyCode.N,45},{KeyCode.M,46},
        {KeyCode.Tab,48},{KeyCode.Space,49},{KeyCode.LeftShift,56},{KeyCode.CapsLock,57},{KeyCode.LeftAlt,58},
        {KeyCode.LeftControl,59},{KeyCode.RightShift,60},{KeyCode.RightAlt,61},{KeyCode.RightControl,62},{KeyCode.LeftCommand,55},
        {KeyCode.UpArrow,126},{KeyCode.DownArrow,125},{KeyCode.LeftArrow,123},{KeyCode.RightArrow,124}
    };
    static readonly Dictionary<KeyCode, int> win = new Dictionary<KeyCode, int>
    {
        {KeyCode.Space,0x20},{KeyCode.Tab,0x09},{KeyCode.LeftShift,0xA0},{KeyCode.RightShift,0xA1},{KeyCode.LeftControl,0xA2},
        {KeyCode.RightControl,0xA3},{KeyCode.LeftAlt,0xA4},{KeyCode.RightAlt,0xA5},{KeyCode.CapsLock,0x14},
        {KeyCode.UpArrow,0x26},{KeyCode.DownArrow,0x28},{KeyCode.LeftArrow,0x25},{KeyCode.RightArrow,0x27}
    };

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    // CoreGraphics returns C bool (one byte), not Win32 BOOL (four bytes).
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGEventSourceKeyState(int stateId, ushort key);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGEventSourceButtonState(int stateId, int button);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
#endif

    // null: no OS answer for this key (unknown key or platform).
    static bool? Os(KeyCode key)
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (key == KeyCode.Mouse0) return CGEventSourceButtonState(0, 0);
        if (key == KeyCode.Mouse1) return CGEventSourceButtonState(0, 1);
        if (key == KeyCode.Mouse2) return CGEventSourceButtonState(0, 2);
        return mac.TryGetValue(key, out var k) ? CGEventSourceKeyState(0, (ushort)k) : (bool?)null;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        int vk;
        if (key == KeyCode.Mouse0) vk = 0x01; else if (key == KeyCode.Mouse1) vk = 0x02; else if (key == KeyCode.Mouse2) vk = 0x04;
        else if (key >= KeyCode.A && key <= KeyCode.Z) vk = 'A' + (key - KeyCode.A);
        else if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) vk = '0' + (key - KeyCode.Alpha0);
        else if (!win.TryGetValue(key, out vk)) return null;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
#else
        return null;
#endif
    }

    // Unity says the key is held: false only when the OS says it was released.
    public static bool? Read(KeyCode key)
    {
        if (broken || key == KeyCode.None) return null;
        try { return Os(key); }
        catch (Exception e)
        {
            broken = true;
            Debug.LogWarning("[ElderboomInput] Native key confirmation unavailable; using focus-safe press/release tracking. " + e.GetType().Name);
            return null;
        }
    }

    public static bool StillDown(KeyCode key) => Read(key) != false;
}
