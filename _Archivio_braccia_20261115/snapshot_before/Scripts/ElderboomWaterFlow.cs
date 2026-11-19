using UnityEngine;

// Scrolls the water material along the stream (texture offset), so the creek looks like it is flowing.
public sealed class ElderboomWaterFlow : MonoBehaviour
{
    public Vector2 speed = new Vector2(.22f, .05f);
    Material mat; Vector2 offset;
    void Start() { var r = GetComponent<Renderer>(); if (r) mat = r.material; }
    void Update()
    {
        if (!mat) return;
        offset += speed * Time.deltaTime; offset.x = Mathf.Repeat(offset.x, 1f); offset.y = Mathf.Repeat(offset.y, 1f);
        mat.SetTextureOffset("_BaseColorMap", offset);
    }
}
