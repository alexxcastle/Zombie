using UnityEngine;

// A small flock that circles far away now and then.
public sealed class ElderboomBackdropBirds : MonoBehaviour
{
    public int count = 14; public float radius = 45f, height = 75f, speed = 9f;
    Transform[] birds; float[] phase;
    public Mesh birdMesh; public Material birdMaterial;
    void Start()
    {
        birds = new Transform[count]; phase = new float[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Bird"); go.layer = gameObject.layer; go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = birdMesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = birdMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            birds[i] = go.transform; phase[i] = Random.value * Mathf.PI * 2;
        }
    }
    void Update()
    {
        float t = Time.time * speed / radius;
        for (int i = 0; i < birds.Length; i++)
        {
            float a = t + i * .32f + Mathf.Sin(Time.time * .3f + phase[i]) * .15f;
            var p = new Vector3(Mathf.Cos(a) * (radius + (i % 4) * 4), height + Mathf.Sin(Time.time * .7f + phase[i]) * 3 + (i % 3) * 2, Mathf.Sin(a) * (radius + (i % 4) * 4));
            var tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            birds[i].localPosition = p; birds[i].localRotation = Quaternion.LookRotation(tangent);
            float flap = Mathf.Sin(Time.time * 9f + phase[i]); birds[i].localScale = new Vector3(1.6f, .6f + flap * .9f, 1.6f);
        }
    }
}
