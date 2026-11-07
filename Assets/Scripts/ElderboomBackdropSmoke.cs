using UnityEngine;

// Smoke column far away: sways and breathes slowly so it does not look painted on.
public sealed class ElderboomBackdropSmoke : MonoBehaviour
{
    float seed; Vector3 baseScale; Quaternion baseRot;
    void Start() { seed = Random.value * 10; baseScale = transform.localScale; baseRot = transform.localRotation; }
    void Update()
    {
        float t = Time.time * .07f + seed;
        transform.localRotation = baseRot * Quaternion.Euler(0, Mathf.Sin(t) * 6f, Mathf.Sin(t * .7f) * 1.5f);
        transform.localScale = Vector3.Scale(baseScale, new Vector3(1 + Mathf.Sin(t * 1.3f) * .06f, 1 + Mathf.Sin(t * .9f) * .04f, 1));
    }
}
