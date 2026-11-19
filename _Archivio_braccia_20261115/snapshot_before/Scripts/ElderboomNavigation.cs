using UnityEngine;
using UnityEngine.AI;

public sealed class ElderboomNavigation : MonoBehaviour
{
    public NavMeshData data;
    NavMeshDataInstance instance;
    void OnEnable() { if(data!=null) instance=NavMesh.AddNavMeshData(data); }
    void OnDisable() { if(instance.valid) instance.Remove(); }
}
