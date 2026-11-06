using UnityEngine;

// Hand-placed zombie spawn. 'area' ties it to an ElderboomArea by name; leave it
// empty to use the area whose box contains it.
public sealed class ElderboomSpawnPoint : MonoBehaviour
{
    public string area = "";

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(.9f, .1f, .1f, .9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .9f, .45f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
    }
}
