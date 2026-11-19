using System.Collections;
using UnityEngine;

// Separate hinge transforms retain their posts when a purchased gate opens.
public sealed class ElderboomGateLeaves : MonoBehaviour
{
    public Transform leftHinge,rightHinge;
    public Collider[] leafColliders;
    public float openingDegrees=105,openingSeconds=1.2f;
    public IEnumerator Open()
    {
        var left=leftHinge.localRotation;var right=rightHinge.localRotation;
        float duration=Mathf.Max(.1f,openingSeconds);
        for(float t=0;t<duration;t+=Time.deltaTime)
        {Pose(Mathf.SmoothStep(0,1,t/duration),left,right);yield return null;}
        Pose(1,left,right);
        // Leaves stay visible, outside the original clear passage.
    }
    void Pose(float t,Quaternion left,Quaternion right)
    {
        leftHinge.localRotation=left*Quaternion.Euler(0,-openingDegrees*t,0);
        rightHinge.localRotation=right*Quaternion.Euler(0,openingDegrees*t,0);
    }
}
