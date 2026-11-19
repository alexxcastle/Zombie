using System.Collections.Generic;
using UnityEngine;

// Hold editor (ElderboomGripAdjust) available everywhere, in matches too, while the holds are being tuned:
// + (main keyboard or keypad) opens/closes it. Move the weapon in the hands, each hand on the weapon, the aim and
// single arm bones; Enter saves to GripOffsets.json (in a build: the user file that overrides the shipped one).
// Set Enabled = false when the tuning is done.
public sealed class ElderboomGripEditor : MonoBehaviour
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Enabled || FindFirstObjectByType<ElderboomGripEditor>() != null) return;
        var go = new GameObject("ElderboomGripEditor");
        DontDestroyOnLoad(go);
        go.AddComponent<ElderboomGripEditor>();
    }

    HDRPSlicePlayer cachedPlayer;
    HDRPSlicePlayer Player
    {
        get
        {
            if (cachedPlayer == null || !cachedPlayer.isActiveAndEnabled) cachedPlayer = FindFirstObjectByType<HDRPSlicePlayer>();
            return cachedPlayer;
        }
    }

    static bool TogglePressed()
    {
        if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Plus)) return true;
        string typed = Input.inputString;   // the + key wherever the layout puts it (Italian keyboard included)
        return !string.IsNullOrEmpty(typed) && typed.IndexOf('+') >= 0;
    }

    void Update()
    {
        if (!Enabled) return;
        var game = ElderboomGame.Active;
        var player = game != null ? Player : null;
        if (TogglePressed())
        {
            editing = !editing && game != null && player != null;
            if (!editing) ShowBoneMark(null);
        }
        if (!editing) return;
        if (game == null || player == null || !player.enabled || player.SettingsOpen) { editing = false; ShowBoneMark(null); return; }
        EditGrip(game, player);
    }

    // ---- hold editor ----
    bool editing; int target; string status = "";
    static readonly string[] BaseTargets = { "ARMA (scorre nelle mani)", "MANO DESTRA", "MANO SINISTRA", "MIRA (tieni il tasto destro)", "OSSA SINGOLE (braccio, avambraccio, mano, dita)", "CAMERA (sposta la visuale; braccia e arma restano ferme; in mira si annulla)", "CARICATORE (i caricatori separati dell'arma; resta in ogni animazione)", "BRACCIO DESTRO (tutto il braccio, mano compresa; l'arma resta ferma)", "BRACCIO SINISTRO (tutto il braccio, mano compresa; l'arma resta ferma)" };
    const int ArmRight = 7, ArmLeft = 8;
    readonly List<string> labels = new List<string>();
    readonly List<string> gearNames = new List<string>();
    readonly List<ElderboomGripAdjust.GearPiece> gear = new List<ElderboomGripAdjust.GearPiece>();
    int boneIndex; Transform boneMark; string boneLine = "";

    void RebuildTargets(ElderboomGripAdjust adj)
    {
        labels.Clear(); gearNames.Clear();
        foreach (var t in BaseTargets) { labels.Add(t); gearNames.Add(null); }
        gear.Clear();
        if (adj != null) adj.ListGear(gear);
        foreach (var g in gear) { labels.Add(g.label); gearNames.Add(g.name); }
        if (labels.Count == 0) return;
        if (target >= labels.Count) target = 0;
    }

    static Transform ActiveRoot(HDRPSlicePlayer player)
    {
        var ars = player.GetComponent<ElderboomArsenal>(); if (ars == null) return null;
        var f = typeof(ElderboomArsenal).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var slot = f?.GetValue(ars);
        return slot?.GetType().GetField("root")?.GetValue(slot) as Transform;
    }

    ElderboomGripAdjust Current(HDRPSlicePlayer player)
    {
        var root = ActiveRoot(player);
        return root != null ? root.GetComponent<ElderboomGripAdjust>() : null;
    }

    void EditGrip(ElderboomGame game, HDRPSlicePlayer player)
    {
        var adj = Current(player);
        RebuildTargets(adj);
        if (labels.Count == 0) return;
        if (Input.GetKeyDown(KeyCode.Tab)) target = (target + 1) % labels.Count;
        ShowBoneMark(null);
        if (gearNames[target] != null) { EditGear(player, adj); return; }
        if (target == 4) { EditBones(game, player, adj); return; }
        if (target == 5)
        {
            if (string.IsNullOrEmpty(player.AdsKey)) { status = "Nessuna arma attiva."; return; }
            var c = ElderboomGripAdjust.Load(player.AdsKey);
            bool f3 = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float cm = (f3 ? .01f : .05f) * Time.deltaTime, ct = (f3 ? 4f : 20f) * Time.deltaTime;
            c.camPos += new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * cm;
            c.camEuler += new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * ct;
            if (Input.GetKeyDown(KeyCode.Backspace)) { c.camPos = Vector3.zero; c.camEuler = Vector3.zero; }
            player.ApplyAdsOffset();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ElderboomGripAdjust.Store(c); status = "Salvata la camera " + c.key + " (GripOffsets.json)."; }
            else status = c.key + "  camera pos " + c.camPos.ToString("F3") + " rot " + c.camEuler.ToString("F1");
            return;
        }
        if (target == 3)
        {
            // Aim: works on every weapon, own primaries included (keyed operator/weapon like the holds).
            if (string.IsNullOrEmpty(player.AdsKey)) { status = "Nessuna arma attiva."; return; }
            var a = ElderboomGripAdjust.Load(player.AdsKey);
            bool f2 = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float mv = (f2 ? .005f : .03f) * Time.deltaTime, tn = (f2 ? 2f : 12f) * Time.deltaTime;
            a.adsPos += new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * mv;
            a.adsEuler += new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * tn;
            if (Input.GetKeyDown(KeyCode.Backspace)) { a.adsPos = Vector3.zero; a.adsEuler = Vector3.zero; }
            player.ApplyAdsOffset();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ElderboomGripAdjust.Store(a); status = "Salvata la mira " + a.key + " (GripOffsets.json)."; }
            else status = a.key + "  mira pos " + a.adsPos.ToString("F3") + " rot " + a.adsEuler.ToString("F1");
            return;
        }
        if (target == 6)
        {
            if (adj == null || adj.offset == null || adj.MagazineCount == 0) { status = "Questa arma non ha un caricatore separato."; return; }
            var m = adj.offset; var parent = adj.MagazineParent;
            bool f6 = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Vector3 mp = new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * ((f6 ? .005f : .03f) * Time.deltaTime);
            Vector3 me = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * ((f6 ? 6f : 30f) * Time.deltaTime);
            // Keys in view space; the correction is stored in the magazine's parent space (models carry their own scale).
            var view = player.GetComponentInChildren<Camera>();
            if (view != null && parent != null)
            {
                if (mp != Vector3.zero) m.magPos += parent.InverseTransformVector(view.transform.TransformDirection(mp));
                m.magEuler = ViewTurn(parent, view.transform, m.magEuler, me);
            }
            else { m.magPos += mp; m.magEuler += me; }
            m.magScale = BumpScale(m.magScale);
            if (Input.GetKeyDown(KeyCode.Backspace)) { m.magPos = Vector3.zero; m.magEuler = Vector3.zero; m.magScale = 0f; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ElderboomGripAdjust.Store(m); status = "Salvato il caricatore " + m.key + " (GripOffsets.json)."; }
            else status = m.key + "  caricatori: " + adj.MagazineCount + "  pos " + m.magPos.ToString("F4") + " rot " + m.magEuler.ToString("F1") + "  scala " + ScaleText(m.magScale);
            return;
        }
        if (target == ArmRight || target == ArmLeft) { EditArm(game, player, adj, target == ArmRight); return; }
        if (adj == null || adj.offset == null) { status = "Questa arma usa la presa originale dell'operatore: si regola solo la mira (Tab)."; return; }
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float move = (fine ? .01f : .05f) * Time.deltaTime, turn = (fine ? 6f : 30f) * Time.deltaTime;
        Vector3 dp = new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * move;
        Vector3 de = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * turn;
        var o = adj.offset;
        // Keys work in view space (I/K toward/away from the eye, J/L and U/O along the screen); the offsets are stored
        // in the viewmodel root's space, which the operator rigs turn away from the camera.
        var cam = player.GetComponentInChildren<Camera>();
        if (cam != null)
        {
            if (dp != Vector3.zero) dp = adj.transform.InverseTransformVector(cam.transform.TransformDirection(dp));
            if (target == 0) o.weaponEuler = ViewTurn(adj.transform, cam.transform, o.weaponEuler, de);
            else if (target == 1) o.rightEuler = ViewTurn(adj.transform, cam.transform, o.rightEuler, de);
            else o.leftEuler = ViewTurn(adj.transform, cam.transform, o.leftEuler, de);
            de = Vector3.zero;
        }
        if (target == 0) { o.weaponPos += dp; o.weaponEuler += de; o.weaponScale = BumpScale(o.weaponScale); }
        else if (target == 1) { o.rightPos += dp; o.rightEuler += de; o.rightScale = BumpScale(o.rightScale); }
        else { o.leftPos += dp; o.leftEuler += de; o.leftScale = BumpScale(o.leftScale); }
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (target == 0) { o.weaponPos = o.weaponEuler = Vector3.zero; o.weaponScale = 0f; }
            else if (target == 1) { o.rightPos = o.rightEuler = Vector3.zero; o.rightScale = 0f; }
            else { o.leftPos = o.leftEuler = Vector3.zero; o.leftScale = 0f; }
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ElderboomGripAdjust.Store(o);
            status = "Salvato " + o.key + (ElderboomTestRange.Active ? " (GripOffsets.json). Mira ricalcolata." : " (GripOffsets.json). La linea di mira si aggiorna dalla prossima arma presa o partita.");
            if (ElderboomTestRange.Active) game.BeginSession(player.OperatorDisplayName, "NORMAL"); // rebuilds the hold, so aiming uses the new sights
        }
        else status = o.key + (o.IsZero ? "  (nessuna correzione)" : "");
    }

    void EditArm(ElderboomGame game, HDRPSlicePlayer player, ElderboomGripAdjust adj, bool rightSide)
    {
        if (adj == null || adj.offset == null) { status = "Questa arma usa la presa originale dell'operatore: si regola solo la mira (Tab)."; return; }
        var wrist = adj.ArmWrist(rightSide);
        ShowBoneMark(wrist);
        if (wrist == null) { status = rightSide ? "Questo braccio destro non è regolabile." : "Questo braccio sinistro non è regolabile."; return; }
        var o = adj.offset;
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float move = (fine ? .01f : .05f) * Time.deltaTime, turn = (fine ? 6f : 30f) * Time.deltaTime;
        Vector3 dp = new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * move;
        Vector3 de = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * turn;
        var cam = player.GetComponentInChildren<Camera>();
        if (cam != null)
        {
            if (dp != Vector3.zero) dp = adj.transform.InverseTransformVector(cam.transform.TransformDirection(dp));
            if (rightSide) o.rightArmEuler = ViewTurn(adj.transform, cam.transform, o.rightArmEuler, de);
            else o.leftArmEuler = ViewTurn(adj.transform, cam.transform, o.leftArmEuler, de);
            de = Vector3.zero;
        }
        if (rightSide) { o.rightArmPos += dp; o.rightArmEuler += de; }
        else { o.leftArmPos += dp; o.leftArmEuler += de; }
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (rightSide) o.rightArmPos = o.rightArmEuler = Vector3.zero;
            else o.leftArmPos = o.leftArmEuler = Vector3.zero;
        }
        Vector3 pos = rightSide ? o.rightArmPos : o.leftArmPos;
        Vector3 euler = rightSide ? o.rightArmEuler : o.leftArmEuler;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ElderboomGripAdjust.Store(o);
            status = "Salvato il braccio " + (rightSide ? "destro" : "sinistro") + " di " + o.key + " (GripOffsets.json).";
            if (ElderboomTestRange.Active) game.BeginSession(player.OperatorDisplayName, "NORMAL");
        }
        else status = o.key + "  braccio " + (rightSide ? "destro" : "sinistro") + "  pos " + pos.ToString("F3") + " rot " + euler.ToString("F1");
    }

    void EditBones(ElderboomGame game, HDRPSlicePlayer player, ElderboomGripAdjust adj)
    {
        boneLine = "";
        if (adj == null || adj.offset == null || adj.RigBones.Count == 0) { status = "Questa arma non ha un rig regolabile (si regola solo la mira)."; return; }
        var list = adj.RigBones;
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.PageDown)) boneIndex += fine ? 5 : 1;
        if (Input.GetKeyDown(KeyCode.Comma) || Input.GetKeyDown(KeyCode.PageUp)) boneIndex -= fine ? 5 : 1;
        boneIndex = ((boneIndex % list.Count) + list.Count) % list.Count;
        var bone = list[boneIndex];
        float turn = (fine ? 8f : 45f) * Time.deltaTime;
        Vector3 de = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * turn;
        de += new Vector3(Axis(KeyCode.I, KeyCode.K), Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U)) * turn;
        float boneScale = ScaleDelta();
        var tw = adj.Tweak(bone.name, de != Vector3.zero || boneScale != 0f);
        if (tw != null) { tw.euler += de; tw.scale = BumpScale(tw.scale); }
        if (Input.GetKeyDown(KeyCode.Backspace) && tw != null) { tw.euler = Vector3.zero; tw.scale = 0f; }
        if (Input.GetKeyDown(KeyCode.Delete)) foreach (var b in adj.offset.bones) { b.euler = Vector3.zero; b.scale = 0f; }
        ShowBoneMark(bone);
        int changed = 0; foreach (var b in adj.offset.bones) if (b.euler != Vector3.zero || ElderboomGripAdjust.Offset.Scaled(b.scale)) changed++;
        boneLine = "\nOsso " + (boneIndex + 1) + "/" + list.Count + ": <b>" + bone.name + "</b>\nrotazione " + (tw != null ? tw.euler.ToString("F1") : "(0, 0, 0)") + "  scala " + ScaleText(tw != null ? tw.scale : 0f) + "   ossa modificate: " + changed;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ElderboomGripAdjust.Store(adj.offset);
            status = "Salvato " + adj.offset.key + " con le ossa.";
        }
        else status = adj.offset.key;
    }

    void EditGear(HDRPSlicePlayer player, ElderboomGripAdjust adj)
    {
        string name = gearNames[target];
        Transform node = null;
        foreach (var g in gear) if (g.name == name) { node = g.node; break; }
        ShowBoneMark(node);
        if (adj == null || adj.offset == null || node == null) { status = "Questo accessorio non è più montato."; return; }
        var tw = adj.GearTweak(name, false);
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        Vector3 mp = new Vector3(Axis(KeyCode.L, KeyCode.J), Axis(KeyCode.O, KeyCode.U), Axis(KeyCode.I, KeyCode.K)) * ((fine ? .005f : .03f) * Time.deltaTime);
        Vector3 me = new Vector3(Axis(KeyCode.DownArrow, KeyCode.UpArrow), Axis(KeyCode.RightArrow, KeyCode.LeftArrow), Axis(KeyCode.Z, KeyCode.X)) * ((fine ? 6f : 30f) * Time.deltaTime);
        float sd = ScaleDelta();
        if (mp != Vector3.zero || me != Vector3.zero || sd != 0f) tw = adj.GearTweak(name, true);
        if (tw != null)
        {
            var parent = node.parent != null ? node.parent : node;
            var view = player.GetComponentInChildren<Camera>();
            if (view != null)
            {
                if (mp != Vector3.zero) tw.pos += parent.InverseTransformVector(view.transform.TransformDirection(mp));
                tw.euler = ViewTurn(parent, view.transform, tw.euler, me);
            }
            else { tw.pos += mp; tw.euler += me; }
            tw.scale = BumpScale(tw.scale);
        }
        if (Input.GetKeyDown(KeyCode.Backspace) && tw != null) { tw.pos = Vector3.zero; tw.euler = Vector3.zero; tw.scale = 0f; }
        string label = labels[target];
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ElderboomGripAdjust.Store(adj.offset); status = "Salvato " + label + " (" + adj.offset.key + ")."; }
        else status = adj.offset.key + "  " + label + "  pos " + (tw != null ? tw.pos.ToString("F4") : Vector3.zero.ToString("F4")) + " rot " + (tw != null ? tw.euler.ToString("F1") : Vector3.zero.ToString("F1")) + "  scala " + ScaleText(tw != null ? tw.scale : 0f);
    }

    float ScaleDelta()
    {
        float dir = Axis(KeyCode.N, KeyCode.M);
        if (dir == 0f) return 0f;
        bool fine = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        return dir * (fine ? .04f : .2f) * Time.deltaTime;
    }

    float BumpScale(float current)
    {
        float d = ScaleDelta();
        if (d == 0f) return current;
        float s = ElderboomGripAdjust.Offset.Scaled(current) ? current : 1f;
        return Mathf.Clamp(s + d, .05f, 4f);
    }

    static string ScaleText(float s) => ElderboomGripAdjust.Offset.Scaled(s) ? s.ToString("F3") : "1 (originale)";

    void ShowBoneMark(Transform bone)
    {
        if (bone == null) { if (boneMark != null) boneMark.gameObject.SetActive(false); return; }
        if (boneMark == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "GripEditor_BoneMark";
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("HDRP/Unlit")); m.SetColor("_UnlitColor", new Color(1f, .25f, .1f));
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boneMark = g.transform;
        }
        boneMark.gameObject.SetActive(true);
        boneMark.gameObject.layer = bone.gameObject.layer;
        boneMark.SetParent(bone, false);
        boneMark.localPosition = Vector3.zero;
        float s = .007f / Mathf.Max(1e-4f, bone.lossyScale.x);
        boneMark.localScale = Vector3.one * s;
    }

    // A turn about the view's axes, added to an offset kept about the root's axes (as ElderboomGripAdjust applies it).
    static Vector3 ViewTurn(Transform root, Transform cam, Vector3 euler, Vector3 viewDelta)
    {
        if (viewDelta == Vector3.zero) return euler;
        var world = cam.rotation * Quaternion.Euler(viewDelta) * Quaternion.Inverse(cam.rotation);
        var local = Quaternion.Inverse(root.rotation) * world * root.rotation;
        var e = (local * Quaternion.Euler(euler)).eulerAngles;
        return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
    }

    static float Axis(KeyCode plus, KeyCode minus) => (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f);


    void OnGUI()
    {
        if (!Enabled) return;
        if (editing)
        {
            var r = new Rect(16f, Screen.height * .5f - 190f, 520f, 380f);
            GUI.color = new Color(1f, 1f, 1f, .92f);
            GUI.Box(r, "");
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
            var pl = Player; var adj = pl != null ? Current(pl) : null;
            string vals = "";
            if (target == 4) vals = boneLine;
            else if (gearNames.Count > target && gearNames[target] != null) vals = "";
            else if (target == 5 || target == 3 || target == 6 || target == ArmRight || target == ArmLeft) vals = "";
            else if (adj != null && adj.offset != null)
            {
                var o = adj.offset;
                vals = "\nArma   pos " + o.weaponPos.ToString("F3") + " rot " + o.weaponEuler.ToString("F1") + "  scala " + ScaleText(o.weaponScale)
                     + "\nDestra pos " + o.rightPos.ToString("F3") + " rot " + o.rightEuler.ToString("F1") + "  scala " + ScaleText(o.rightScale)
                     + "\nSinistra pos " + o.leftPos.ToString("F3") + " rot " + o.leftEuler.ToString("F1") + "  scala " + ScaleText(o.leftScale);
            }
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width - 24f, r.height - 16f),
                "<b>REGOLA IMPUGNATURA</b>   (+ per uscire)\n<b>Tab</b>: " + (target < labels.Count ? labels[target] : "")
                + "\n<b>J/L</b> sinistra/destra · <b>U/O</b> giù/su · <b>K/I</b> indietro/avanti"
                + "\n<b>frecce</b> inclina/gira · <b>Z/X</b> rolla · <b>Shift</b> fine"
                + "\n<b>N/M</b> rimpicciolisce/ingrandisce il componente selezionato"
                + "\n<b>tasto destro</b> mira · <b>Backspace</b> azzera · <b>Invio</b> salva"
                + (target == 4 ? "\n<b>, / .</b> osso precedente/successivo (Shift: 5) · <b>frecce Z/X</b> o <b>IJKL U/O</b> ruota · <b>Canc</b> azzera tutte" : "")
                + "\n\n" + status + vals, st);
        }
    }
}
