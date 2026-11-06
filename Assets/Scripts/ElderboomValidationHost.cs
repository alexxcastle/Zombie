#if UNITY_EDITOR
using UnityEngine;

// Editor-only test coroutines must outlive cancellation of a gameplay session.
public sealed class ElderboomValidationHost : MonoBehaviour
{
    public static ElderboomValidationHost Create()
    {
        var go=new GameObject("EditorValidationHost");DontDestroyOnLoad(go);
        return go.AddComponent<ElderboomValidationHost>();
    }
}
#endif
