using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

// Logs/claude_skilltest.txt -> "cook" | "grab" | "ambush": play the map (player invulnerable), put one zombie with that
// skill near the player and photograph it from the side every 0.3 s; log what it does. Logs/SkillTest/<mode>_*.jpg
static class ElderboomSkillPlayTest
{
    const string Flag = "Logs/claude_skilltest.txt", Result = "Logs/claude_skilltest_result.txt", Dir = "Logs/SkillTest", Key = "claude_skilltest";
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double start, nextShot; static int shot; static ElderboomZombie z; static Camera cam; static RenderTexture rt; static Vector3 spot;
    static void Poll()
    {
        if (File.Exists(Flag) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
        {
            string m = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
            if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty) { File.WriteAllText(Result, "FAIL unsaved scene"); return; }
            Directory.CreateDirectory(Dir); foreach (var f in Directory.GetFiles(Dir, m + "_*")) File.Delete(f);
            File.WriteAllText(Result, "started " + m + "\n"); SessionState.SetString(Key, m); SessionState.SetInt(Key + "_stage", 1); start = 0; EditorApplication.isPlaying = true; return;
        }
        string mode = SessionState.GetString(Key, ""); int stage = SessionState.GetInt(Key + "_stage", 0);
        if (stage == 0 || !EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup; if (start <= 0) { start = now; return; }
        var game = ElderboomGame.Active; var pl = game != null ? game.Player : null; if (pl == null) return;
        if (stage == 1 && now - start > 4)
        {
            HDRPSlicePlayer.DevInvulnerable = true;
            var list = (System.Collections.Generic.List<ElderboomZombie>)typeof(ElderboomGame).GetField("zombies", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game);
            foreach (var other in list.ToArray()) if (other != null) { other.gameObject.SetActive(false); Object.Destroy(other.gameObject); }
            list.Clear();
            // Freeze the waves: no new spawns while testing.
            game.StopAllCoroutines();
            pl.enabled = false;
            var fwd = Vector3.ProjectOnPlane(pl.transform.forward, Vector3.up).normalized;
            float dist = mode == "ambush" ? 22f : 5f;
            NavMesh.SamplePosition(pl.transform.position + fwd * dist, out var h, 6f, NavMesh.AllAreas);
            var prefab = mode == "cook" ? Resources.Load<GameObject>("Elderboom/Zombies/07_inn_cook") : Resources.Load<GameObject>("Elderboom/Zombies/02_male_villager");
            var go = Object.Instantiate(prefab, h.position, Quaternion.LookRotation(-fwd)); z = go.GetComponent<ElderboomZombie>();
            z.Initialize(game, pl, 9999f, 5f, 2.5f, 1f);
            if (mode == "grab") z.AssignSkill(ElderboomZombieSkill.Grab);
            if (mode == "ambush") z.AssignSkill(ElderboomZombieSkill.Ambush);
            list.Add(z);
            cam = Camera.main; cam.transform.SetParent(null, true); cam.fieldOfView = 50; spot = pl.transform.position;
            foreach (var r in pl.GetComponentsInChildren<Renderer>()) r.enabled = false;
            File.AppendAllText(Result, $"spawned {go.name} at {h.position} player {pl.transform.position} weapon {(z.GetComponent<ElderboomZombieWeapon>()?.Held != null)}\n");
            shot = 0; nextShot = now + .3; SessionState.SetInt(Key + "_stage", 2); return;
        }
        if (stage == 2)
        {
            double t = now - start - 4;
            if (mode == "ambush" && t > 14 && t < 14.2 && SessionState.GetInt(Key + "_walk", 0) == 0)
            {
                // Walk the player up to the zombie's hiding place.
                SessionState.SetInt(Key + "_walk", 1);
            }
            if (mode == "ambush" && SessionState.GetInt(Key + "_walk", 0) == 1 && z != null)
            {
                var d = z.transform.position - pl.transform.position; d.y = 0;
                if (d.magnitude > 3f) { var cc = pl.GetComponent<CharacterController>(); cc.Move(d.normalized * 3.2f * Time.deltaTime + Vector3.down * .5f * Time.deltaTime); }
            }
            if (now >= nextShot && z != null)
            {
                nextShot = now + .3;
                var zp = z.transform.position; var pp = pl.transform.position; var mid = (zp + pp) * .5f; var dir = (zp - pp); dir.y = 0; var side = Vector3.Cross(Vector3.up, dir.normalized);
                if (dir.sqrMagnitude < .01f) side = Vector3.right;
                cam.transform.position = mid + side * (mode == "ambush" && t < 14 ? 9f : 4.2f) + Vector3.up * 1.4f; cam.transform.LookAt(mid + Vector3.up * .9f);
                                var anim = z.GetComponentInChildren<Animator>(); var st = anim.GetCurrentAnimatorStateInfo(0);
                string name = new[] { "WalkA", "WalkB", "AttackA", "AttackPunch", "CleaverDown", "CleaverSide", "GrabLunge", "GrabHold", "AmbushCover", "AmbushIdle", "AmbushPounce", "Sprint", "ShotHit", "Turn" }.FirstOrDefault(n => st.IsName(n)) ?? "?";
                if ((shot % 2 == 0 && t < 26) || name.StartsWith("Ambush") || name.StartsWith("Grab") || name.StartsWith("Cleaver")) ScreenCapture.CaptureScreenshot($"{Dir}/{mode}_{shot:00}_{name}.png");
                File.AppendAllText(Result, $"t {t:F1} state {z.State} anim {name} {st.normalizedTime:F2} speed {anim.speed:F2} dist {Vector3.Distance(zp, pp):F1} grabbed {pl.Grabbed} hp {pl.Health:F0} ts {Time.timeScale} frame {Time.frameCount} agentStopped {z.GetComponent<NavMeshAgent>().isStopped} vel {z.GetComponent<NavMeshAgent>().velocity.magnitude:F2} hasPath {z.GetComponent<NavMeshAgent>().hasPath} {(mode == "ambush" ? ElderboomZombie.LastCoverReport : "")}\n");
                shot++;
            }
            if (t > (mode == "ambush" ? 30 : 14)) { SessionState.SetInt(Key + "_stage", 0); SessionState.SetInt(Key + "_walk", 0); HDRPSlicePlayer.DevInvulnerable = false; File.AppendAllText(Result, "DONE\n"); EditorApplication.isPlaying = false; }
        }
    }
}
