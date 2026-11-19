using System;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ElderboomAdsBuildProbe
{
    public static void VerifyAndBuild()
    {
        var root = new GameObject("ADS animation clock check");
        try
        {
            var view = root.AddComponent<ElderboomViewmodel>();
            var type = view.GetType();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var elapsed = type.GetField("elapsed", fields);
            var advance = type.GetMethod("AdvancePlayback", fields);
            void Frame(float delta) => advance.Invoke(view, new object[] { delta });
            void Expect(string step, float expected)
            {
                var actual = (float)elapsed.GetValue(view);
                if (Mathf.Abs(actual - expected) > 0.0001f)
                    throw new InvalidOperationException($"{step}: expected {expected}, got {actual}");
            }

            view.ResetToIdle();
            Frame(0.2f);
            view.SetAiming(true);
            for (int i = 0; i < 29; i++)
            {
                view.SetAiming(true);
                Frame(0.1f);
                Expect("hold keeps current idle frame", 0.2f);
            }
            Frame(0.25f);
            Expect("crossing three seconds resumes without jumping", 0.35f);
            view.SetAiming(true);
            Frame(0.1f);
            Expect("continuous ADS does not rearm the timer", 0.45f);

            view.SetAiming(false);
            view.SetAiming(true);
            Frame(0.5f);
            Expect("new ADS entry re-arms hold", 0.45f);
            view.SetAiming(false);
            Frame(0.3f);
            Expect("releasing aim resumes immediately", 0.75f);

            view.SetAiming(true);
            view.PlayReload();
            Frame(0.5f);
            Expect("reload still advances", 0.5f);
            view.PlayAbility();
            Frame(0.5f);
            Expect("ability still advances", 0.5f);

            view.ResetToIdle();
            Frame(0.3f);
            Expect("reset clears hold", 0.3f);
            view.SetAiming(true);
            Frame(0.2f);
            Expect("ADS works again after reset", 0.3f);
            Debug.Log("ADS_IDLE_HOLD_VERIFIED hold=3s, smooth resume, re-entry, release, actions, reset");
        }
        finally { Object.DestroyImmediate(root); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/mp5.prefab");
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 12 || renderers.Any(r => r.GetComponent<MeshFilter>()?.sharedMesh == null || r.sharedMaterial == null))
            throw new InvalidOperationException("MP5 prefab must retain all 12 model parts after the texture reimport");
        if (renderers.Select(r => r.sharedMaterial).Distinct().Count() != 5)
            throw new InvalidOperationException("MP5 must retain five imported materials");
        Debug.Log("MP5_REIMPORT_VERIFIED parts=12 materials=5");
        ElderboomNewWeaponsBuilder.BuildAll();
        ElderboomTestRangeBuild.Build();
    }
}
