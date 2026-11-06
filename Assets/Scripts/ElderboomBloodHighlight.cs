using UnityEngine;
using UnityEngine.UIElements;

// Port of the Godot menu's ui/blood_highlight.gdshader + blood_highlight.gd:
// animated red liquid that fills a button from the left with an irregular front
// when it becomes active (hover, keyboard focus or selected), and drains out.
// UI Toolkit has no per-element shaders, so the same math runs per vertex on a
// grid mesh drawn behind the button's content; it only animates while visible.
public sealed class ElderboomBloodHighlight : VisualElement
{
    // Set by ElderboomUi: focus only counts as "active" while navigating with keys,
    // as in the original (a clicked button keeps focus but should drain).
    public static bool KeyboardNavigation;

    const int GridX = 56, GridY = 10;
    const float FillSeconds = .32f, DrainSeconds = .26f;
    readonly VisualElement owner;
    float strength, from, target, tweenAt, tweenSeconds = FillSeconds;
    bool hovered, focused, drewIdle;

    public float Opacity = 1f;

    public ElderboomBloodHighlight(VisualElement owner)
    {
        this.owner = owner;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
        generateVisualContent += Draw;
        owner.RegisterCallback<PointerEnterEvent>(_ => hovered = true);
        owner.RegisterCallback<PointerLeaveEvent>(_ => hovered = false);
        owner.RegisterCallback<FocusInEvent>(_ => focused = true);
        owner.RegisterCallback<FocusOutEvent>(_ => focused = false);
        schedule.Execute(Tick).Every(16);
    }

    bool Active => owner.enabledInHierarchy &&
        (hovered || (focused && KeyboardNavigation) || owner.ClassListContains("eb-button--selected") || owner.ClassListContains("eb-menu-item--selected"));

    void Tick()
    {
        float now = Time.unscaledTime;
        float goal = Active ? 1f : 0f;
        if (!Mathf.Approximately(goal, target))
        {
            from = strength; target = goal; tweenAt = now;
            tweenSeconds = goal > 0f ? FillSeconds : DrainSeconds;
        }
        if (!Mathf.Approximately(strength, target))
        {
            float k = Mathf.Clamp01((now - tweenAt) / tweenSeconds);
            k = -(Mathf.Cos(Mathf.PI * k) - 1f) * .5f; // sine in-out, as the Godot tween
            strength = Mathf.Lerp(from, target, k);
            if (k >= 1f) strength = target;
        }
        if (strength > .0001f) { drewIdle = false; MarkDirtyRepaint(); }
        else if (!drewIdle) { drewIdle = true; MarkDirtyRepaint(); }
    }

    // ---- Shader math (same constants as blood_highlight.gdshader) ----
    static float Hash(float x, float y)
    {
        float v = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    static float Noise(float x, float y)
    {
        float cx = Mathf.Floor(x), cy = Mathf.Floor(y);
        float fx = x - cx, fy = y - cy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Mathf.Lerp(Hash(cx, cy), Hash(cx + 1f, cy), fx);
        float b = Mathf.Lerp(Hash(cx, cy + 1f), Hash(cx + 1f, cy + 1f), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    Color Shade(float u, float v, float time)
    {
        float fx = u * 6f - time * .14f, fy = v * 2.7f + time * .07f;
        float liquid = Noise(fx, fy) * .65f + Noise(fx * 2.1f, fy * 2.1f) * .35f;
        const float feather = .03f;
        float vein = strength > 0f ? Smooth(.42f - feather, .78f + feather, liquid) : 0f;
        var blood = Color.Lerp(new Color(.21f, .003f, .014f), new Color(.63f, .018f, .045f), vein);
        float pulse = .97f + .03f * Mathf.Sin(time * 2.2f + liquid * 4f);
        // A soft, irregular front advances across the button as it activates.
        float front = u + (liquid - .5f) * .16f;
        float fill = Smooth(front - .13f, front + .13f, strength * 1.35f - .15f) * strength;
        var baseColor = new Color(.012f, .010f, .012f);
        var color = Color.Lerp(baseColor, blood * pulse, fill);
        color.a = Mathf.Lerp(.24f, .96f, fill) * Opacity;
        return color;
    }

    void Draw(MeshGenerationContext context)
    {
        var rect = contentRect;
        if (rect.width < 1f || rect.height < 1f) return;
        float time = Time.unscaledTime;
        int gx = strength > .0001f ? GridX : 1, gy = strength > .0001f ? GridY : 1;
        int vertexCount = (gx + 1) * (gy + 1), indexCount = gx * gy * 6;
        var mesh = context.Allocate(vertexCount, indexCount);
        for (int y = 0; y <= gy; y++)
            for (int x = 0; x <= gx; x++)
            {
                float u = x / (float)gx, v = y / (float)gy;
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(rect.x + u * rect.width, rect.y + v * rect.height, Vertex.nearZ),
                    tint = Shade(u, v, time)
                });
            }
        for (int y = 0; y < gy; y++)
            for (int x = 0; x < gx; x++)
            {
                ushort i = (ushort)(y * (gx + 1) + x);
                ushort right = (ushort)(i + 1), down = (ushort)(i + gx + 1), diagonal = (ushort)(i + gx + 2);
                mesh.SetNextIndex(i); mesh.SetNextIndex(right); mesh.SetNextIndex(down);
                mesh.SetNextIndex(right); mesh.SetNextIndex(diagonal); mesh.SetNextIndex(down);
            }
    }
}
