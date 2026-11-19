using UnityEngine;

// One interaction prompt for the whole game, drawn by the HUD (UI Toolkit) instead of every object's own IMGUI
// label: objects offer their text each frame, the nearest one to the player wins, so two prompts never overlap.
// Hint: a single line of controls at the bottom-left (e.g. riding the horse).
public static class ElderboomPrompt
{
    static int frame = -1, hintFrame = -1;
    static float best;
    static string text, hint;

    public static void Offer(Vector3 player, Vector3 at, string prompt)
    {
        int f = Time.frameCount;
        float d = (at - player).sqrMagnitude;
        if (f != frame || d < best) { frame = f; best = d; text = prompt; }
    }

    public static void Hint(string line) { hintFrame = Time.frameCount; hint = line; }

    // Offered this frame or the previous one (the HUD can update before or after the objects).
    public static string Current => frame >= Time.frameCount - 1 ? text : null;
    public static string CurrentHint => hintFrame >= Time.frameCount - 1 ? hint : null;
}
