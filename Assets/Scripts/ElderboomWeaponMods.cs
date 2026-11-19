using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Weapon customization (CuiGames Accessory Kit on the A5 Carbine and K40 Battle Rifle).
// Loadouts are chosen in the menu (ElderboomMenu.Loadout) and saved in PlayerPrefs; every copy of the weapon the game
// builds (starting weapon, weapon stations, test-range dispenser) goes through ElderboomArsenal.Create -> Apply.
//
// Attachments are parented to the weapon's muzzle marker (MainWeapon_Muzzle): it is the one node that survives every way a
// weapon is held (prefab as is, or transplanted into an operator's hand by ElderboomAuthoredGrip), and its axes are known:
// +Z toward the muzzle, +Y up, +X right. Socket positions below are measured on the shipped GLBs (glTF space: Y up,
// muzzle -Z, right +X) and converted to the muzzle's space by Local().
public static class ElderboomWeaponMods
{
    public const string Optic = "optic", Muzzle = "muzzle", Top = "top", Right = "right", Under = "under", Charm = "charm",
        Stock = "stock", Grip = "grip", Mag = "mag", Slide = "slide";
    public static readonly string[] Slots = { Optic, Muzzle, Top, Right, Under, Charm, Stock, Grip, Mag, Slide };
    public static string SlotName(string slot) =>
        slot == Optic ? "OPTIC" : slot == Muzzle ? "MUZZLE" : slot == Top ? "TOP ACCESSORY" : slot == Right ? "RIGHT ACCESSORY" : slot == Under ? "UNDERBARREL"
        : slot == Stock ? "STOCK" : slot == Grip ? "PISTOL GRIP" : slot == Mag ? "MAGAZINE" : slot == Slide ? "SLIDE" : "CHARM";

    public sealed class Mod
    {
        public string id, name, slot, folder, file, description;
        public string[] builtinNodes;     // a part of the weapon model itself (K40 modules): shown instead of loaded
        public string onlyFor;            // weapon id, null = every customizable weapon
        public float zoom = 1f;           // optic magnification
        public int hipfire, precision, control, mobility;
        public float recoil = 1f, bloom = 1f, spread = 1f, range = 1f;
        public string light;              // laser_red, laser_green, flash
        public int capacity;              // magazine size (0 = the weapon's own)
        public bool suppressor;           // muffled shot (sound only)
        public bool Builtin => builtinNodes != null;
    }

    public sealed class Socket { public string slot; public Vector3 pos, normal = Vector3.up; }

    public sealed class Platform
    {
        public string id, rail;
        public Vector3 muzzle;
        public bool local;                // sockets already in the muzzle marker's space (measured in Unity)
        public HashSet<string> allow;     // mods that fit this weapon (null = every generic mod)
        public int hipfire, precision, control, mobility;
        public readonly Dictionary<string, string> defaults = new Dictionary<string, string>();
        public readonly HashSet<string> required = new HashSet<string>();   // slots that always hold a part (stock, magazine...)
        public readonly List<Link> links = new List<Link>();
        internal Socket[] sockets;
    }

    // Parts of the model that follow what is mounted (Meshy kits): the rail cover appears with an optic, the rail
    // handguard with a rail accessory... 'generic' = only for attachments, not for the weapon's own built-in parts.
    public sealed class Link { public string[] slots, show, hide; public bool generic; }

    static Mod M(string id, string name, string slot, string folder, string file, string description) =>
        new Mod { id = id, name = name, slot = slot, folder = folder, file = file, description = description };

    public static readonly Mod[] All =
    {
        // ---- optics (top rail, rear) ----
        Set(M("H02", "H02 HOLOGRAPHIC", Optic, "@rail", "H02", "Wide window, fast target pickup. 1x."), precision: 4, mobility: -1),
        Set(M("H02L", "H02L LONG HOLO", Optic, "@rail", "H02L", "Longer hood, steadier picture. 1.25x."), precision: 6, mobility: -2, zoom: 1.25f, range: 1.04f),
        Set(M("P42", "P42 4X PRISM", Optic, "@rail", "P42", "Compact prism sight for mid range. 4x."), precision: 10, hipfire: -2, mobility: -4, zoom: 4f, range: 1.15f),
        Set(M("L01", "L01 LPVO 1-6X", Optic, "@rail", "L01", "Variable power optic set at 3x."), precision: 9, hipfire: -2, mobility: -5, zoom: 3f, range: 1.12f),
        Set(M("R6", "R6 6X SCOPE", Optic, "@rail", "R6", "Long range scope. 6x."), precision: 14, hipfire: -4, mobility: -7, zoom: 6f, range: 1.22f),
        Set(M("R8", "R8 8X SCOPE", Optic, "@rail", "R8", "Marksman scope. 8x."), precision: 16, hipfire: -5, mobility: -8, zoom: 8f, range: 1.28f),
        Set(new Mod { id = "R8P", name = "R8 PRISM BLUE CRYSTAL", slot = Optic, folder = "A5Rail", file = "R8P", onlyFor = "m4",
            description = "Prism 8x with blue crystal glass." }, precision: 17, hipfire: -5, mobility: -8, zoom: 8f, range: 1.28f),
        Set(new Mod { id = "K40OPT", name = "K40 INTEGRATED OPTIC", slot = Optic, onlyFor = "k40", builtinNodes = new[] { "optic_module" },
            description = "The K40's own sight. 2.5x." }, precision: 8, mobility: -3, zoom: 2.5f, range: 1.1f),
        Set(new Mod { id = "MP7EOT", name = "MP7 EOTECH", slot = Optic, onlyFor = "mp7", builtinNodes = new[] { "G_Eotech", "G_Glass" },
            description = "The MP7's own holographic sight." }, precision: 4, mobility: -1),
        // ---- muzzle devices ----
        Set(new Mod { id = "HK53FH", name = "HK53 FLASH HIDER", slot = Muzzle, onlyFor = "hk53", builtinNodes = new[] { "Muzzle_Device" },
            description = "The HK53's own flash hider." }, control: 4, recoil: .96f),
        Set(M("MC01", "MC01 COMPENSATOR", Muzzle, "Muzzles", "MC01", "Vents gas upward: less muzzle climb."), control: 10, hipfire: -2, recoil: .88f),
        Set(M("MB01", "MB01 MUZZLE BRAKE", Muzzle, "Muzzles", "MB01", "Cut brake: strongest recoil reduction."), control: 14, hipfire: -4, mobility: -1, recoil: .84f),
        Set(M("FH01", "FH01 FLASH HIDER", Muzzle, "Muzzles", "FH01", "Petal flash hider: a lighter kick."), control: 4, precision: 2, recoil: .96f),
        Set(M("SC01", "SC01 SHORT SUPPRESSOR", Muzzle, "Muzzles", "SC01", "Short can: quieter, a little steadier."), control: 6, precision: 2, mobility: -3, recoil: .92f),
        Set(M("SL01", "SL01 LONG SUPPRESSOR", Muzzle, "Muzzles", "SL01", "Slim long can: steadier, reaches farther."), control: 9, precision: 5, mobility: -6, recoil: .88f, range: 1.06f),
        // ---- top accessory (top rail, front) ----
        Set(M("LM01R", "LM01 LASER · RED", Top, "@rail", "LM01R", "Visible red laser: tighter hip fire."), hipfire: 12, mobility: -1, bloom: .8f, spread: .85f, light: "laser_red"),
        Set(M("LM01G", "LM01 LASER · GREEN", Top, "@rail", "LM01G", "Visible green laser: tighter hip fire."), hipfire: 12, mobility: -1, bloom: .8f, spread: .85f, light: "laser_green"),
        Set(M("IM01", "IM01 IR MODULE", Top, "@rail", "IM01", "Infrared aiming module: invisible, steadier hip fire."), hipfire: 7, bloom: .88f, spread: .92f),
        // ---- right accessory (side rail) ----
        Set(M("FL01", "FL01 TACTICAL LIGHT", Right, "K1Rail", "FL01", "Weapon light: lights the way at night."), hipfire: 3, mobility: -2, bloom: .95f, light: "flash"),
        Set(new Mod { id = "K40LGT", name = "K40 RAIL LIGHT", slot = Right, onlyFor = "k40", builtinNodes = new[] { "rail_light_module" },
            description = "The K40's own rail light." }, hipfire: 2, mobility: -1, light: "flash"),
        // ---- underbarrel ----
        Set(M("FG01", "FG01 SHORT FOREGRIP", Under, "K1Rail", "FG01", "Short vertical grip: better control."), control: 10, hipfire: 4, mobility: -4, recoil: .88f, bloom: .9f),
        Set(new Mod { id = "K40GRP", name = "K40 RAIL GRIP", slot = Under, onlyFor = "k40", builtinNodes = new[] { "rail_grip_module" },
            description = "The K40's own rail grip." }, control: 7, mobility: -2, recoil: .92f),
        // ---- Meshy kits (CC0): shared accessories ----
        Set(M("VKHOLO", "VK HOLOGRAPHIC", Optic, "Meshy", "VKHOLO", "Open holographic window. 1x."), precision: 4, mobility: -1),
        Set(M("OARDS", "OA MINI RED DOT", Optic, "Meshy", "OARDS", "Low, light reflex sight. 1x."), precision: 3, hipfire: 1),
        Set(M("SOLAR", "SOLAR TAC LIGHT", Right, "Meshy", "SOLAR", "Compact rail light."), hipfire: 3, mobility: -2, bloom: .95f, light: "flash"),
        Set(M("TBL", "TBL LASER LIGHT", Under, "Meshy", "TBL", "Underbarrel red laser: tighter hip fire."), hipfire: 10, mobility: -1, bloom: .84f, spread: .88f, light: "laser_red"),
        Set(M("ANGLED", "ANGLED FOREGRIP", Under, "Meshy", "ANGLED", "Angled grip: steadier, barely slower."), control: 8, hipfire: 2, mobility: -2, recoil: .9f, bloom: .92f),
        Set(M("RV1", "RV-1 STUB GRIP", Under, "Meshy", "RV1", "Short stub grip."), control: 6, hipfire: 3, mobility: -1, recoil: .93f, bloom: .9f),
        Set(M("VGRIP", "VERTICAL GRIP", Under, "Meshy", "VGRIP", "Full vertical grip: best control."), control: 11, hipfire: 3, mobility: -4, recoil: .86f, bloom: .9f),
        Set(new Mod { id = "PLIGHT", name = "PISTOL TAC LIGHT", slot = Under, folder = "Meshy", file = "PLIGHT", onlyFor = "p45",
            description = "Pistol weapon light on the underbarrel rail." }, hipfire: 2, mobility: -1, light: "flash"),
        // ---- AK-74 (Meshy kit: parts of the model) ----
        Set(B("AK74SUP", "7.62 SUPPRESSOR", Muzzle, "ak74", "ak_supp_alt", "Long can over the muzzle brake.", sup: true), control: 7, precision: 3, mobility: -5, recoil: .9f, range: 1.05f),
        Set(B("AK74CQR", "CQR FOREGRIP", Under, "ak74", "ak_cqr_alt", "Ergonomic foregrip on the tri-rail handguard."), control: 9, hipfire: 3, mobility: -3, recoil: .89f, bloom: .9f),
        B("AK74STK", "AK-74M FOLDING STOCK", Stock, "ak74", "ak_stock_std", "The standard polymer folding stock."),
        Set(B("AK74STKC", "CHEEKPAD STOCK", Stock, "ak74", "ak_stock_cheek_alt", "Skeleton stock with a raised cheek rest."), precision: 4, control: 3, mobility: -2, recoil: .96f),
        B("AK74GRP", "AK-74M GRIP", Grip, "ak74", "ak_grip_std", "The standard pistol grip."),
        Set(B("AK74GRPG", "SKELETON GRIP", Grip, "ak74", "ak_grip_green_alt", "Anodized skeleton grip: lighter."), hipfire: 2, mobility: 2, control: -1),
        B("AK74MAG", "30-ROUND MAGAZINE", Mag, "ak74", "ak_mag30", "Standard 5.45 magazine."),
        Set(B("AK74DRUM", "100-ROUND DRUM", Mag, "ak74", "ak_drum_alt", "Huge drum: 100 rounds, heavy.", cap: 100), mobility: -10, hipfire: -3),
        // ---- Mosin-Nagant ----
        Set(B("MOPU", "PU 3.5X SCOPE", Optic, "mosin", "mo_pu_scope", "The classic side-mounted PU sniper scope. 3.5x."), precision: 14, hipfire: -4, mobility: -6, zoom: 3.5f, range: 1.2f),
        B("MOSTK", "WOODEN STOCK", Stock, "mosin", "mo_stock_wood", "The original birch stock."),
        Set(B("MOSTKP", "POLYMER STOCK", Stock, "mosin", "mo_stock_poly_alt", "Modern polymer stock: lighter, steadier."), mobility: 3, control: 2),
        // ---- MP5 (Vanguard Kinetics kit) ----
        Set(B("VKSUP", "VK SUPPRESSOR", Muzzle, "mp5vk", "vk_supp_alt", "Threaded can for the MP5.", sup: true), control: 6, precision: 3, mobility: -4, recoil: .92f),
        B("VKSTK", "FIXED STOCK", Stock, "mp5vk", "vk_stock_std", "The standard fixed stock."),
        Set(B("VKSTKSD", "RETRACTABLE STOCK", Stock, "mp5vk", "vk_stock_sd_alt", "Collapsed metal stock: lighter, less stable."), mobility: 4, control: -3, recoil: 1.04f),
        B("VKMAG", "30-ROUND MAGAZINE", Mag, "mp5vk", "vk_mag30", "Standard curved magazine."),
        Set(B("VKDRUM", "50-ROUND DRUM", Mag, "mp5vk", "vk_drum_alt", "Drum magazine: 50 rounds, heavy.", cap: 50), mobility: -7, hipfire: -2),
        // ---- .45 pistol (Obsidian Arms kit) ----
        Set(B("P45SUP", ".45 SUPPRESSOR", Muzzle, "p45", "p45_supp_alt", "Pistol can.", sup: true), control: 5, precision: 2, mobility: -4, recoil: .93f),
        B("P45SLD", "STANDARD SLIDE", Slide, "p45", "p45_slide_std", "The standard slide with its sights."),
        Set(B("P45SLDC", "CUSTOM SLIDE", Slide, "p45", "p45_slide_custom_alt", "Lightened competition slide with top rail cuts."), precision: 3, control: 2, hipfire: 1),
        // ---- M4 carbine: the birdcage is a node of the model, hidden when another muzzle device is chosen ----
        B("M4FH", "A2 FLASH HIDER", Muzzle, "m4", "m4_flashhider_std", "The standard birdcage flash hider."),
        // ---- charms ----
        M("CH_TERM", "MICRO TERMINAL", Charm, "Charms", "CharmTerminal", "A tiny terminal. Purely cosmetic."),
        M("CH_BEAR", "HONEY CUB", Charm, "Charms", "CharmHoneyCub", "A small amber bear. Purely cosmetic."),
        M("CH_STAR", "STARDROP", Charm, "Charms", "CharmStardrop", "Smoke teal glass star. Purely cosmetic."),
    };

    // A part of a Meshy weapon model, shown when chosen.
    static Mod B(string id, string name, string slot, string weapon, string node, string description, bool sup = false, int cap = 0) =>
        new Mod { id = id, name = name, slot = slot, onlyFor = weapon, builtinNodes = new[] { node }, description = description, suppressor = sup, capacity = cap };

    static Mod Set(Mod m, int hipfire = 0, int precision = 0, int control = 0, int mobility = 0, float zoom = 1f,
        float recoil = 1f, float bloom = 1f, float spread = 1f, float range = 1f, string light = null)
    {
        m.hipfire = hipfire; m.precision = precision; m.control = control; m.mobility = mobility; m.zoom = zoom;
        m.recoil = recoil; m.bloom = bloom; m.spread = spread; m.range = range; m.light = light;
        return m;
    }

    static Platform[] platforms;
    static Platform[] Platforms => platforms ??= BuildPlatforms();

    static Platform[] BuildPlatforms()
    {
        var a5 = new Platform
        {
            id = "a5", rail = "A5Rail", muzzle = new Vector3(0f, 0f, -.50f), hipfire = 52, precision = 40, control = 60, mobility = 72,
            sockets = new[]
            {
                new Socket { slot = Optic, pos = new Vector3(0f, .035f, .06f) },
                new Socket { slot = Muzzle, pos = new Vector3(0f, 0f, -.50f) },
                new Socket { slot = Top, pos = new Vector3(0f, .035f, -.30f) },
                new Socket { slot = Right, pos = new Vector3(.032f, 0f, -.25f), normal = Vector3.right },
                new Socket { slot = Under, pos = new Vector3(0f, -.046f, -.30f) },
                new Socket { slot = Charm, pos = new Vector3(.027f, -.004f, .10f) },
            }
        };
        var k40 = new Platform
        {
            id = "k40", rail = "K1Rail", muzzle = new Vector3(-.022f, .098f, -.557f), hipfire = 38, precision = 56, control = 48, mobility = 55,
            sockets = new[]
            {
                new Socket { slot = Optic, pos = new Vector3(-.022f, .145f, .072f) },
                new Socket { slot = Muzzle, pos = new Vector3(-.022f, .098f, -.557f) },
                new Socket { slot = Top, pos = new Vector3(-.022f, .151f, -.185f) },
                new Socket { slot = Right, pos = new Vector3(.019f, .10f, -.19f), normal = Vector3.right },
                new Socket { slot = Under, pos = new Vector3(-.022f, .075f, -.155f) },
                new Socket { slot = Charm, pos = new Vector3(.016f, .09f, .22f) },
            }
        };
        // The K40 ships with its optic, rail grip and rail light (separate nodes of the model).
        k40.defaults[Optic] = "K40OPT"; k40.defaults[Under] = "K40GRP"; k40.defaults[Right] = "K40LGT";

        // The rest of the arsenal, measured in Unity in each muzzle marker's space (Editor/ElderboomModsAudit, Logs/
        // claude_moddump_result.txt; units = the weapon's own scale). Only what such a weapon really takes:
        // short optics on claw mounts or real rails, a light where a side rail fits, threaded barrels for muzzles.
        var holo = new[] { "H02", "H02L", "P42" };
        var muzzles = new[] { "MC01", "MB01", "FH01", "SC01", "SL01" };
        var hk53 = MakeLocal("hk53", 50, 46, 52, 68, Join(holo, muzzles, "FL01", Charms),
            S(Optic, 0f, .052f, -.34f), S(Muzzle, 0f, 0f, -.039f), S(Right, .028f, 0f, -.17f, Vector3.right), S(Charm, .029f, .01f, -.29f));
        hk53.defaults[Muzzle] = "HK53FH";
        // MP5: no side light (the 20 cm light covers its whole short handguard).
        var mp5 = MakeLocal("mp5", 64, 42, 66, 80, Join(holo, muzzles, Charms),
            S(Optic, 0f, .046f, -.27f), S(Muzzle, 0f, 0f, 0f), S(Charm, .02f, .005f, -.31f));
        var mp7 = MakeLocal("mp7", 66, 40, 62, 84, Join(holo, muzzles, "FL01", "FG01", Charms),
            S(Optic, 0f, .05f, -.255f), S(Muzzle, 0f, 0f, 0f), S(Right, .025f, .012f, -.12f, Vector3.right), S(Under, 0f, -.031f, -.13f), S(Charm, .021f, 0f, -.32f));
        mp7.defaults[Optic] = "MP7EOT";
        var benelli = MakeLocal("shotgun", 58, 20, 42, 62, Join(new[] { "H02", "H02L" }, "FL01", Charms),
            S(Optic, 0f, .029f, -.60f), S(Right, .026f, -.03f, -.27f, Vector3.right), S(Charm, .02f, -.03f, -.70f));
        var mp133 = MakeLocal("mp133", 54, 22, 38, 64, Join(Charms), S(Charm, .017f, -.02f, -.70f));
        var glock = MakeLocal("glock19", 66, 36, 56, 92, Join(new[] { "SC01" }, Charms), S(Muzzle, 0f, 0f, 0f), S(Charm, 0f, -.118f, -.175f));
        var m1911 = MakeLocal("m1911", 62, 38, 50, 90, Join(new[] { "SC01" }, Charms), S(Muzzle, 0f, -.012f, -.027f), S(Charm, 0f, -.13f, -.205f)); // the 1911 barrel ends behind its muzzle marker
        var rhino = MakeLocal("rhino", 60, 35, 40, 88, Join(Charms), S(Charm, 0f, -.148f, -.22f));

        // Meshy kits (assembled in Blender; Assets/GameAssets/Meshy). Sockets are given in the assembly's space
        // (x toward the stock, muzzle at -x; y = right; z up) times the export scale, converted to glTF space by K().
        var rifleOptics = new[] { "H02", "H02L", "P42", "L01", "R6", "R8", "VKHOLO", "OARDS" };
        var shortOptics = new[] { "H02", "H02L", "P42", "VKHOLO", "OARDS" };
        var grips = new[] { "FG01", "ANGLED", "VGRIP", "RV1", "TBL" };
        var lasers = new[] { "LM01R", "LM01G", "IM01" };

        const float ak = .3655f;
        var ak74 = new Platform
        {
            id = "ak74", rail = "K1Rail", muzzle = K(ak, -.97f, 0f, .035f), hipfire = 50, precision = 44, control = 50, mobility = 66,
            allow = Join(rifleOptics, "AK74SUP", lasers, "FL01", "SOLAR", grips, Charms),
            sockets = new[]
            {
                KS(ak, Optic, .50f, 0f, .19f), KS(ak, Muzzle, -.97f, 0f, .035f), KS(ak, Top, -.40f, 0f, .155f),
                KS(ak, Right, -.25f, .12f, .03f, Vector3.right), KS(ak, Under, -.30f, 0f, -.155f), KS(ak, Charm, .80f, .10f, -.02f),
                KS(ak, Stock, 1.30f, 0f, 0f), KS(ak, Grip, .90f, 0f, -.22f), KS(ak, Mag, .36f, 0f, -.33f),
            }
        };
        ak74.defaults[Stock] = "AK74STK"; ak74.defaults[Grip] = "AK74GRP"; ak74.defaults[Mag] = "AK74MAG";
        ak74.required.UnionWith(new[] { Stock, Grip, Mag });
        ak74.links.Add(new Link { slots = new[] { Optic }, show = new[] { "ak_cover_rail_alt" }, hide = new[] { "ak_cover_std" } });
        ak74.links.Add(new Link { slots = new[] { Top, Right, Under }, show = new[] { "ak_hg_rail_alt" }, hide = new[] { "ak_hg_std" } });

        const float mo = .59f;
        var mosin = new Platform
        {
            id = "mosin", rail = "K1Rail", muzzle = K(mo, -1.13f, 0f, .125f), hipfire = 20, precision = 80, control = 35, mobility = 50,
            allow = Join("P42", "L01", "R6", "R8", "H02L", Charms),
            sockets = new[] { KS(mo, Optic, .14f, 0f, .27f), KS(mo, Charm, .55f, .055f, 0f), KS(mo, Stock, .60f, 0f, 0f) }
        };
        mosin.defaults[Optic] = "MOPU"; mosin.defaults[Stock] = "MOSTK";
        mosin.required.Add(Stock);
        mosin.links.Add(new Link { slots = new[] { Optic }, generic = true, show = new[] { "mo_trirail_alt" }, hide = new[] { "mo_pu_mount" } });

        const float vk = .238f;
        var mp5vk = new Platform
        {
            id = "mp5vk", rail = "K1Rail", muzzle = K(vk, -.98f, 0f, 0f), hipfire = 64, precision = 44, control = 64, mobility = 80,
            allow = Join(shortOptics, "VKSUP", lasers, "FL01", "SOLAR", grips, Charms),
            sockets = new[]
            {
                KS(vk, Optic, .30f, 0f, .205f), KS(vk, Muzzle, -.98f, 0f, 0f), KS(vk, Top, -.42f, 0f, .087f),
                KS(vk, Right, -.40f, .122f, -.03f, Vector3.right), KS(vk, Under, -.40f, 0f, -.147f), KS(vk, Charm, .75f, .09f, -.10f),
                KS(vk, Stock, 1.40f, 0f, -.08f), KS(vk, Mag, .06f, 0f, -.42f),
            }
        };
        mp5vk.defaults[Stock] = "VKSTK"; mp5vk.defaults[Mag] = "VKMAG";
        mp5vk.required.UnionWith(new[] { Stock, Mag });
        mp5vk.links.Add(new Link { slots = new[] { Optic }, show = new[] { "vk_rail_alt" } });
        mp5vk.links.Add(new Link { slots = new[] { Top, Right, Under }, show = new[] { "vk_hg_rail_alt" }, hide = new[] { "vk_hg_std" } });

        const float px = .0995f;
        var p45 = new Platform
        {
            id = "p45", rail = "K1Rail", muzzle = K(px, -.97f, 0f, .54f), hipfire = 64, precision = 38, control = 54, mobility = 90,
            allow = Join("P45SUP", "PLIGHT", "TBL", Charms),
            sockets = new[] { KS(px, Muzzle, -.97f, 0f, .54f), KS(px, Under, -.60f, 0f, .13f), KS(px, Charm, .62f, 0f, -.62f), KS(px, Slide, 0f, 0f, .62f) }
        };
        p45.defaults[Slide] = "P45SLD";
        p45.required.Add(Slide);
        p45.links.Add(new Link { slots = new[] { Under }, show = new[] { "p45_rail_alt" } });

        // M4 carbine (GameAssets/M4_v6): sockets in glTF space (x right, y up, z toward the stock; bore on y = 0).
        var m4 = new Platform
        {
            id = "m4", rail = "K1Rail", muzzle = new Vector3(0f, 0f, -.4342f), hipfire = 48, precision = 58, control = 56, mobility = 58,
            allow = Join(rifleOptics, muzzles, "M4FH", lasers, "FL01", "SOLAR", grips, Charms),
            sockets = new[]
            {
                new Socket { slot = Optic, pos = new Vector3(0f, .0317f, .0820f) },         // receiver rail, ahead of the rear sight
                new Socket { slot = Muzzle, pos = new Vector3(0f, 0f, -.3852f) },          // where the flash hider starts
                new Socket { slot = Top, pos = new Vector3(0f, .0319f, -.2222f) },         // handguard top rail
                new Socket { slot = Right, pos = new Vector3(.0195f, 0f, -.1718f), normal = Vector3.right },
                new Socket { slot = Under, pos = new Vector3(0f, -.0169f, -.2618f) },      // handguard underside
                new Socket { slot = Charm, pos = new Vector3(.0112f, -.0190f, .0852f) },
            }
        };
        m4.defaults[Muzzle] = "M4FH";
        // With an optic on the rail the flip-up rear sight lies folded.
        m4.links.Add(new Link { slots = new[] { Optic }, show = new[] { "m4_rear_folded_alt" }, hide = new[] { "m4_rear_up" } });

        return new[] { a5, k40, ak74, mosin, hk53, mp5, mp5vk, mp7, benelli, mp133, glock, m1911, p45, rhino, m4 };
    }

    static readonly string[] Charms = { "CH_TERM", "CH_BEAR", "CH_STAR" };

    // Assembly space (Meshy kits) -> glTF space: x' = y, y' = z, z' = x (muzzle -Z), times the export scale.
    static Vector3 K(float scale, float x, float y, float z) => new Vector3(y, z, x) * scale;
    static Socket KS(float scale, string slot, float x, float y, float z) => new Socket { slot = slot, pos = K(scale, x, y, z) };
    static Socket KS(float scale, string slot, float x, float y, float z, Vector3 normal) => new Socket { slot = slot, pos = K(scale, x, y, z), normal = normal };

    static Socket S(string slot, float x, float y, float z) => new Socket { slot = slot, pos = new Vector3(x, y, z) };
    static Socket S(string slot, float x, float y, float z, Vector3 normal) => new Socket { slot = slot, pos = new Vector3(x, y, z), normal = normal };

    static HashSet<string> Join(params object[] groups)
    {
        var set = new HashSet<string>();
        foreach (var g in groups)
        {
            if (g is string s) set.Add(s);
            else if (g is string[] a) foreach (var x in a) set.Add(x);
        }
        return set;
    }

    static Platform MakeLocal(string id, int hipfire, int precision, int control, int mobility, HashSet<string> allow, params Socket[] sockets) =>
        new Platform { id = id, rail = "K1Rail", local = true, allow = allow, sockets = sockets, hipfire = hipfire, precision = precision, control = control, mobility = mobility };

    public static Platform Find(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;
        foreach (var p in Platforms) if (string.Equals(p.id, weaponId, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static bool Customizable(string weaponId) => Find(weaponId) != null;
    public static IEnumerable<string> CustomizableIds { get { foreach (var p in Platforms) yield return p.id; } }

    public static Mod FindMod(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var m in All) if (m.id == id) return m;
        return null;
    }

    // Choices for a slot on a weapon (null first = nothing mounted).
    public static List<Mod> Options(string weaponId, string slot)
    {
        var list = new List<Mod> { null };
        var p = Find(weaponId);
        if (p == null) return list;
        if (p.required.Contains(slot)) list.Clear();
        foreach (var m in All)
            if (m.slot == slot && Fits(p, m) && (m.Builtin || Resources.Load<GameObject>(PathOf(p, m)) != null))
                list.Add(m);
        return list;
    }

    static bool Fits(Platform p, Mod m)
    {
        if (!HasSlot(p, m.slot)) return false;
        if (m.onlyFor != null) return m.onlyFor == p.id;
        return p.allow == null || p.allow.Contains(m.id);
    }

    static bool HasSlot(Platform p, string slot)
    {
        foreach (var s in p.sockets) if (s.slot == slot) return true;
        return false;
    }

    public static bool HasSlot(string weaponId, string slot) { var p = Find(weaponId); return p != null && HasSlot(p, slot); }

    static string PathOf(Platform p, Mod m) => "Elderboom/Attachments/" + (m.folder == "@rail" ? p.rail : m.folder) + "/" + m.file;

    // ---- saved loadouts -----------------------------------------------------------------------
    const string PrefsKey = "elderboom.loadout.";
    static readonly Dictionary<string, Effect> effects = new Dictionary<string, Effect>();

    public static Dictionary<string, string> Defaults(string weaponId)
    {
        var d = new Dictionary<string, string>();
        var p = Find(weaponId);
        if (p != null) foreach (var kv in p.defaults) d[kv.Key] = kv.Value;
        return d;
    }

    public static Dictionary<string, string> Load(string weaponId)
    {
        var p = Find(weaponId);
        if (p == null) return new Dictionary<string, string>();
        string saved = PlayerPrefs.GetString(PrefsKey + p.id, null);
        if (string.IsNullOrEmpty(saved)) return Defaults(p.id);
        var d = new Dictionary<string, string>();
        foreach (var part in saved.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string slot = part.Substring(0, eq), id = part.Substring(eq + 1);
            var m = FindMod(id);
            if (m != null && m.slot == slot && Fits(p, m)) d[slot] = id;
        }
        foreach (var slot in p.required) if (!d.ContainsKey(slot) && p.defaults.TryGetValue(slot, out var def)) d[slot] = def;
        return d;
    }

    public static void Save(string weaponId, Dictionary<string, string> loadout)
    {
        var p = Find(weaponId);
        if (p == null) return;
        var sb = new StringBuilder("v1");
        foreach (var slot in Slots) sb.Append(';').Append(slot).Append('=').Append(loadout != null && loadout.TryGetValue(slot, out var id) ? id : "");
        PlayerPrefs.SetString(PrefsKey + p.id, sb.ToString());
        PlayerPrefs.Save();
        effects.Remove(p.id);
    }

    public static void Reset(string weaponId)
    {
        var p = Find(weaponId);
        if (p == null) return;
        PlayerPrefs.DeleteKey(PrefsKey + p.id);
        PlayerPrefs.Save();
        effects.Remove(p.id);
    }

    public static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        foreach (var slot in Slots)
        {
            a.TryGetValue(slot, out var x); b.TryGetValue(slot, out var y);
            if ((x ?? "") != (y ?? "")) return false;
        }
        return true;
    }

    public static bool IsModified(string weaponId) => Find(weaponId) != null && !Same(Load(weaponId), Defaults(weaponId));

    public static string Get(Dictionary<string, string> loadout, string slot) => loadout != null && loadout.TryGetValue(slot, out var id) && !string.IsNullOrEmpty(id) ? id : null;

    // ---- stats ---------------------------------------------------------------------------------
    public struct Stats { public int hipfire, precision, control, mobility; }

    public static Stats StatsOf(string weaponId, Dictionary<string, string> loadout)
    {
        var p = Find(weaponId);
        var s = new Stats();
        if (p == null) return s;
        s.hipfire = p.hipfire; s.precision = p.precision; s.control = p.control; s.mobility = p.mobility;
        foreach (var slot in Slots)
        {
            var m = FindMod(Get(loadout, slot));
            if (m == null) continue;
            s.hipfire += m.hipfire; s.precision += m.precision; s.control += m.control; s.mobility += m.mobility;
        }
        s.hipfire = Mathf.Clamp(s.hipfire, 0, 100); s.precision = Mathf.Clamp(s.precision, 0, 100);
        s.control = Mathf.Clamp(s.control, 0, 100); s.mobility = Mathf.Clamp(s.mobility, 0, 100);
        return s;
    }

    // Gameplay effect of the saved loadout (HDRPSlicePlayer: recoil, bloom, aim zoom).
    sealed class Effect { public float recoil = 1f, bloom = 1f, spread = 1f, range = 1f, zoom = 1f; public int mobility, capacity; public bool suppressed; }

    static Effect EffectOf(string weaponId)
    {
        var p = Find(weaponId);
        if (p == null) return null;
        if (effects.TryGetValue(p.id, out var e)) return e;
        e = new Effect();
        var loadout = Load(p.id);
        foreach (var slot in Slots)
        {
            var m = FindMod(Get(loadout, slot));
            if (m == null) continue;
            e.recoil *= m.recoil; e.bloom *= m.bloom; e.spread *= m.spread; e.range *= m.range;
            if (m.slot == Optic) e.zoom = m.zoom;
            e.mobility += m.mobility;
            if (m.suppressor || m.id == "SC01" || m.id == "SL01") e.suppressed = true;
            if (m.capacity > 0) e.capacity = m.capacity;
        }
        effects[p.id] = e;
        return e;
    }

    public static float RecoilScale(ElderboomWeaponDefinition d) => d != null ? EffectOf(d.id)?.recoil ?? 1f : 1f;
    public static float BloomScale(ElderboomWeaponDefinition d) => d != null ? EffectOf(d.id)?.bloom ?? 1f : 1f;
    public static float Zoom(ElderboomWeaponDefinition d) => d != null ? EffectOf(d.id)?.zoom ?? 1f : 1f;
    // Suppressor: a muffled shot (sound only, the zombies hear it the same).
    public static bool Suppressed(ElderboomWeaponDefinition d) => d != null && (EffectOf(d.id)?.suppressed ?? false);

    // ---- aim time ------------------------------------------------------------------------------
    // Time to raise the sights (seconds to ~95% of the way), by weapon: light and short is fast, long and heavy is slow.
    static readonly Dictionary<string, float> AimTimes = new Dictionary<string, float>
    {
        { "glock19", .13f }, { "m1911", .14f }, { "rhino", .17f },                       // handguns
        { "mp7", .17f }, { "mp5", .19f },                                                // SMGs
        { "hk53", .22f }, { "a5", .24f }, { "akmn", .27f }, { "k40", .30f },             // rifles
        { "ak74", .26f }, { "mosin", .38f }, { "mp5vk", .19f }, { "p45", .145f },          // Meshy kits
        { "shotgun", .27f }, { "mp133", .29f }, { "striker", .33f },                     // shotguns
        { "grenade", .34f }, { "flame", .40f }, { "minigun", .55f },                     // special drops
    };

    public static float BaseAimTime(ElderboomWeaponDefinition d)
    {
        if (d == null) return .15f;
        if (d.id != null && AimTimes.TryGetValue(d.id, out var t)) return t;
        switch (d.category)
        {
            case ElderboomIds.Category.Handgun: return .14f;
            case "smg": return .18f;
            case ElderboomIds.Category.Shotgun: return .28f;
            case ElderboomIds.Category.AssaultRifle: return .25f;
            default: return .15f;
        }
    }

    // Each point of mobility the attachments take away makes aiming 1.5% slower (an 8x scope: +12%).
    public static float AimTimeFor(string weaponId, int mobilityDelta) =>
        BaseAimTime(ElderboomWeapons.Find(weaponId)) * Mathf.Clamp(1f - .015f * mobilityDelta, .7f, 1.6f);

    public static float AimTime(ElderboomWeaponDefinition d) =>
        BaseAimTime(d) * Mathf.Clamp(1f - .015f * (d != null ? EffectOf(d.id)?.mobility ?? 0 : 0), .7f, 1.6f);

    // Magazine size with this loadout (0 = the weapon's own).
    public static int CapacityOf(Dictionary<string, string> loadout)
    {
        int cap = 0;
        foreach (var slot in Slots) { var m = FindMod(Get(loadout, slot)); if (m != null && m.capacity > 0) cap = m.capacity; }
        return cap;
    }

    public static int MobilityDelta(Dictionary<string, string> loadout)
    {
        int sum = 0;
        foreach (var slot in Slots) { var m = FindMod(Get(loadout, slot)); if (m != null) sum += m.mobility; }
        return sum;
    }

    // The catalog entry with the loadout's spread and range (a copy: the catalog is shared).
    public static ElderboomWeaponDefinition Modified(ElderboomWeaponDefinition d)
    {
        var e = d != null ? EffectOf(d.id) : null;
        if (e == null || (Mathf.Approximately(e.spread, 1f) && Mathf.Approximately(e.range, 1f) && e.capacity <= 0)) return d;
        var copy = JsonUtility.FromJson<ElderboomWeaponDefinition>(JsonUtility.ToJson(d));
        copy.spread = d.spread * e.spread;
        copy.range = d.range * e.range;
        copy.falloffStart = d.falloffStart * e.range;
        if (e.capacity > 0) { copy.reserve = Mathf.Max(d.reserve, e.capacity * 2); copy.capacity = e.capacity; }
        return copy;
    }

    // ---- building ------------------------------------------------------------------------------
    public const string MuzzleMarker = "MainWeapon_Muzzle", HolderName = "WeaponMods";

    static Vector3 Local(Platform p, Vector3 v) { if (p.local) return v; var d = v - p.muzzle; return new Vector3(d.x, d.y, -d.z); }
    static Vector3 LocalDir(Platform p, Vector3 v) => p.local ? v : new Vector3(v.x, v.y, -v.z);

    // The muzzle marker: MainWeapon_Muzzle, else the first node named like a muzzle (the same node the sight calibration
    // in GripSights.json is stored against, so its axes are: +Z toward the muzzle, +Y up).
    public static Transform FindMuzzle(Transform root)
    {
        if (root == null) return null;
        foreach (var name in new[] { MuzzleMarker, "HK53_Muzzle", "Soph_Rifle_Muzzle" })
        {
            var exact = FindNamed(root, name);
            if (exact != null) return exact;
        }
        // A marker, not a part named like one (the HK53's Muzzle_Device mesh).
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.IndexOf("muzzle", StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponent<Renderer>() == null
                && t.GetComponentInParent<ElderboomModTag>(true) == null) return t;
        return null;
    }

    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
    }

    public static Transform FindNamed(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // The light the player flashes on each shot: never an attachment's light.
    public static Light ShotLight(Transform weapon)
    {
        if (weapon == null) return null;
        foreach (var l in weapon.GetComponentsInChildren<Light>(true))
            if (l.GetComponentInParent<ElderboomModTag>(true) == null) return l;
        return null;
    }

    // In game: the saved loadout.
    public static void Apply(Transform weaponRoot, string weaponId) => Apply(weaponRoot, weaponId, Load(weaponId), true);

    // Mounts the loadout on a weapon (prefab instance or held copy). Calling it again replaces the previous attachments.
    // Returns the sockets in world space (for the menu's callout lines), by slot.
    public static Dictionary<string, Transform> Apply(Transform weaponRoot, string weaponId, Dictionary<string, string> loadout, bool inGame)
    {
        var sockets = new Dictionary<string, Transform>();
        var p = Find(weaponId);
        if (p == null || weaponRoot == null) return sockets;
        var muzzle = FindMuzzle(weaponRoot);
        if (muzzle == null) { Debug.LogWarning("[ElderboomWeaponMods] " + weaponId + ": no muzzle marker"); return sockets; }
        // Slots that always hold a part (stock, magazine, slide): the standard one when nothing is chosen.
        if (p.required.Count > 0)
        {
            loadout = loadout != null ? new Dictionary<string, string>(loadout) : new Dictionary<string, string>();
            foreach (var slot in p.required)
                if (Get(loadout, slot) == null && p.defaults.TryGetValue(slot, out var def)) loadout[slot] = def;
        }

        var old = muzzle.Find(HolderName);
        if (old != null) { old.SetParent(null, false); Kill(old.gameObject); }
        var holder = new GameObject(HolderName).transform;
        holder.SetParent(muzzle, false);
        holder.gameObject.AddComponent<ElderboomModTag>();
        int layer = muzzle.gameObject.layer;
        holder.gameObject.layer = layer;
        var shadows = ShadowCastingMode.On;
        foreach (var r in weaponRoot.GetComponentsInChildren<MeshRenderer>(true)) { shadows = r.shadowCastingMode; break; }

        // Built-in parts of the model (K40): visible only when chosen.
        foreach (var m in All)
        {
            if (!m.Builtin || m.onlyFor != p.id) continue;
            bool on = Get(loadout, m.slot) == m.id;
            foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
                foreach (var node in m.builtinNodes)
                    if (t.GetComponent<Renderer>() != null && t.name.IndexOf(node, StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(on);
        }

        // Model parts that follow the mounted attachments (rail covers, rail handguards).
        foreach (var link in p.links)
        {
            bool any = false;
            foreach (var slot in link.slots)
            {
                var chosen = FindMod(Get(loadout, slot));
                if (chosen != null && (!link.generic || !chosen.Builtin)) any = true;
            }
            foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Renderer>() == null) continue;
                if (link.show != null) foreach (var node in link.show) if (t.name.IndexOf(node, StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(any);
                if (link.hide != null) foreach (var node in link.hide) if (t.name.IndexOf(node, StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(!any);
            }
        }

        Transform opticSight = null;
        foreach (var socket in p.sockets)
        {
            var point = new GameObject("Socket_" + socket.slot).transform;
            point.SetParent(holder, false);
            point.localPosition = Local(p, socket.pos);
            point.gameObject.layer = layer;
            sockets[socket.slot] = point;

            var m = FindMod(Get(loadout, socket.slot));
            if (m == null) continue;
            if (m.Builtin)
            {
                if (m.light != null && inGame) AddBuiltinLight(weaponRoot, m, muzzle, layer);
                // A Meshy kit's own scope (Mosin PU): aim through it like a mounted optic.
                if (m.slot == Optic && m.zoom > 1f)
                    foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
                        if (t.name == m.builtinNodes[0]) { opticSight = t; break; }
                continue;
            }
            var prefab = Resources.Load<GameObject>(PathOf(p, m));
            if (prefab == null) { Debug.LogWarning("[ElderboomWeaponMods] missing Resources/" + PathOf(p, m)); continue; }
            var inst = Object.Instantiate(prefab, point, false).transform;
            inst.name = "Mod_" + m.id;
            inst.localRotation = Align(LocalDir(p, socket.normal));
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = shadows;
            if (m.slot == Charm) inst.gameObject.AddComponent<ElderboomCharmSway>();
            if (m.slot == Optic) opticSight = inst;
            if (m.light != null && inGame) AddLight(inst, m.light, muzzle, layer);
        }

        // Aim through the optic: the sight line moves up to the optic's glass, the rear sight to its eyepiece.
        if (opticSight != null) AimThrough(weaponRoot, muzzle, opticSight, FindMod(Get(loadout, Optic)), inGame);
        return sockets;
    }

    // Rotation (in the muzzle's space) that turns an imported attachment so its length runs toward the muzzle
    // and its seat faces the rail. The kit's attachments run along glTF +X; the importer may mirror an axis,
    // so the sign is read once from a muzzle device (it grows from its root toward the muzzle).
    static float lengthSign;
    static Quaternion Align(Vector3 normal)
    {
        if (lengthSign == 0f)
        {
            lengthSign = -1f; // glTFast mirrors X
            var probe = Resources.Load<GameObject>("Elderboom/Attachments/Muzzles/MB01");
            if (probe != null && MeshBounds(probe.transform, probe.transform, out var b)) lengthSign = b.center.x >= 0f ? 1f : -1f;
        }
        var from = Quaternion.LookRotation(Vector3.right * lengthSign, Vector3.up);
        var to = Quaternion.LookRotation(Vector3.forward, normal);
        return to * Quaternion.Inverse(from);
    }

    // Bounds of the meshes under 'root', in 'space' (works on inactive objects and prefab assets).
    public static bool MeshBounds(Transform root, Transform space, out Bounds bounds, Func<Component, bool> filter = null)
    {
        bounds = default;
        bool any = false;
        var toSpace = space.worldToLocalMatrix;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || (filter != null && !filter(mf))) continue;
            var mb = mf.sharedMesh.bounds;
            var m = toSpace * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = m.MultiplyPoint3x4(c);
                if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; } else bounds.Encapsulate(w);
            }
        }
        foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (sk.sharedMesh == null || (filter != null && !filter(sk))) continue;
            var mb = sk.sharedMesh.bounds;
            var m = toSpace * sk.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = m.MultiplyPoint3x4(c);
                if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; } else bounds.Encapsulate(w);
            }
        }
        return any;
    }

    static bool IsGlass(Component mf)
    {
        var r = mf.GetComponent<Renderer>();
        if (r == null) return false;
        foreach (var mat in r.sharedMaterials)
        {
            if (mat == null) continue;
            string n = mat.name.ToLowerInvariant();
            if (n.Contains("glass") || n.Contains("pane") || n.Contains("lens") || n.Contains("crystal")) return true;
        }
        string node = mf.name.ToLowerInvariant();
        return node.Contains("glass") || node.Contains("pane") || node.Contains("lens");
    }

    static void AimThrough(Transform weaponRoot, Transform muzzle, Transform optic, Mod mod, bool inGame)
    {
        if (!MeshBounds(optic, muzzle, out var all)) return;
        Vector3 centre;
        if (MeshBounds(optic, muzzle, out var glass, IsGlass)) centre = glass.center;
        else { centre = all.center; centre.y = all.max.y - all.size.y * .4f; }
        Transform rear = null, front = null, up = null;
        foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "MainWeapon_ADS_Rear") rear = t;
            else if (t.name == "MainWeapon_ADS_Front") front = t;
            else if (t.name == "MainWeapon_ADS_Up") up = t;
        }
        if (rear == null) { rear = new GameObject("MainWeapon_ADS_Rear").transform; rear.SetParent(muzzle, false); }
        if (front == null) { front = new GameObject("MainWeapon_ADS_Front").transform; front.SetParent(muzzle, false); }
        rear.position = muzzle.TransformPoint(new Vector3(centre.x, centre.y, all.min.z));
        front.position = muzzle.TransformPoint(new Vector3(centre.x, centre.y, all.max.z));
        if (up != null) up.position = muzzle.TransformPoint(new Vector3(centre.x, centre.y + .05f, all.min.z));
        if (inGame && mod != null)
        {
            var sight = optic.gameObject.AddComponent<ElderboomOpticSight>();
            sight.magnification = mod.zoom;
            sight.glass = new List<Renderer>();
            foreach (var mf in optic.GetComponentsInChildren<MeshFilter>(true))
                if (IsGlass(mf)) { var r = mf.GetComponent<Renderer>(); if (r != null) sight.glass.Add(r); }
        }
    }

    // Arms of a weapon pack's prefab (shown only by the menu preview, which hides them).
    public static bool IsArmPart(string name)
    {
        string n = name.ToLowerInvariant();
        // Weapon parts named like body parts (A5/MP5 handguard, MP7 handle, K40 cover sleeve) are not arms.
        if (n.Contains("handguard") || n.Contains("handle") || n.Contains("cover") || n.Contains("charm")) return false;
        return n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("finger");
    }

    // ---- lights --------------------------------------------------------------------------------
    static void AddLight(Transform attachment, string kind, Transform muzzle, int layer)
    {
        Vector3 at = attachment.position;
        if (MeshBounds(attachment, muzzle, out var b)) at = muzzle.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z));
        MakeLight(attachment, kind, at, muzzle.rotation, layer);
    }

    static void AddBuiltinLight(Transform weaponRoot, Mod m, Transform muzzle, int layer)
    {
        foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponent<Renderer>() == null || t.name.IndexOf(m.builtinNodes[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
            var at = t.position;
            if (MeshBounds(t, muzzle, out var b)) at = muzzle.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z));
            var holder = muzzle.Find(HolderName);
            MakeLight(holder != null ? holder : muzzle, m.light, at, muzzle.rotation, layer);
            return;
        }
    }

    static void MakeLight(Transform parent, string kind, Vector3 position, Quaternion rotation, int layer)
    {
        var go = new GameObject("ModLight_" + kind);
        go.layer = layer;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);
        if (go.GetComponentInParent<ElderboomModTag>(true) == null) go.AddComponent<ElderboomModTag>();
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        go.AddComponent<HDAdditionalLightData>();
        var unit = typeof(Light).GetProperty("lightUnit");
        if (unit != null) { try { unit.SetValue(light, Enum.Parse(unit.PropertyType, "Candela")); } catch { } }
        light.shadows = LightShadows.None;
        if (kind == "flash")
        {
            // Night exposure (EV 7.2, like the village): a strong torch.
            light.color = new Color(1f, .95f, .86f);
            light.spotAngle = 42f; light.innerSpotAngle = 16f; light.range = 32f;
            light.intensity = 90000f;
        }
        else
        {
            // Laser: a very narrow spot that leaves a dot where it lands.
            light.color = kind == "laser_green" ? new Color(.25f, 1f, .3f) : new Color(1f, .12f, .08f);
            light.spotAngle = 1f; light.innerSpotAngle = .4f; light.range = 60f;
            light.intensity = 600000f;
        }
        var hd = go.GetComponent<HDAdditionalLightData>();
        if (hd != null) hd.volumetricDimmer = kind == "flash" ? .35f : .9f;
    }
}

// Marks attachment objects (the player's muzzle flash is never one of their lights).
public sealed class ElderboomModTag : MonoBehaviour { }

// A charm swinging on its chain when the weapon moves.
public sealed class ElderboomCharmSway : MonoBehaviour
{
    Vector3 last, angle, speed;
    Quaternion rest;
    bool ready;

    void OnEnable() { ready = false; }

    void LateUpdate()
    {
        float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
        if (dt <= 0f || transform.parent == null) return;
        var parent = transform.parent;
        if (!ready) { last = parent.position; rest = transform.localRotation; ready = true; return; }
        var velocity = parent.InverseTransformDirection((parent.position - last) / dt);
        last = parent.position;
        var target = new Vector3(Mathf.Clamp(-velocity.z * 9f, -40f, 40f), 0f, Mathf.Clamp(velocity.x * 9f, -40f, 40f));
        speed += (target - angle) * (70f * dt);
        speed *= Mathf.Exp(-5f * dt);
        angle += speed * dt * 10f;
        transform.localRotation = rest * Quaternion.Euler(angle);
    }
}

// Aiming through an optic: the glass (opaque in Unity) is hidden while aiming; a reticle is drawn at the centre of the
// view. Magnified optics (3x and up) show a scope picture: the weapon is hidden behind a black ring.
public sealed class ElderboomOpticSight : MonoBehaviour
{
    public float magnification = 1f;
    public List<Renderer> glass;
    HDRPSlicePlayer player;
    float aimedSince = -1f;
    bool scoped;
    readonly List<Renderer> hidden = new List<Renderer>();
    static Texture2D ring, white;

    void OnEnable() { player = GetComponentInParent<HDRPSlicePlayer>(); aimedSince = -1f; }
    void OnDisable() { SetGlass(true); SetScoped(false); }

    float SettleTime => player != null ? ElderboomWeaponMods.AimTime(player.CurrentWeaponDefinition) * .85f : .16f;
    bool Aiming => player != null && player.isActiveAndEnabled && player.IsAiming && !player.SettingsOpen && !player.IsDead;

    void LateUpdate()
    {
        if (player == null) player = GetComponentInParent<HDRPSlicePlayer>();
        if (Aiming) { if (aimedSince < 0f) aimedSince = Time.time; }
        else aimedSince = -1f;
        bool settled = aimedSince >= 0f && Time.time - aimedSince > SettleTime;
        SetGlass(!settled);
        SetScoped(settled && magnification >= 3f);
    }

    void SetGlass(bool on)
    {
        if (glass == null) return;
        foreach (var r in glass) if (r != null) r.forceRenderingOff = !on;
    }

    void SetScoped(bool on)
    {
        if (on == scoped) return;
        scoped = on;
        if (on)
        {
            hidden.Clear();
            var root = player != null ? player.ActiveWeaponTransform : null;
            if (root != null)
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
        }
        else
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || aimedSince < 0f || Time.time - aimedSince <= SettleTime) return;
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        float cx = Screen.width * .5f, cy = Screen.height * .5f;
        var keep = GUI.color;
        if (scoped)
        {
            if (ring == null) ring = MakeRing(512);
            float d = Screen.height * .94f;
            var c = new Rect(cx - d * .5f, cy - d * .5f, d, d);
            GUI.color = Color.black;
            GUI.DrawTexture(c, ring);
            GUI.DrawTexture(new Rect(0, 0, c.xMin + 1, Screen.height), white);
            GUI.DrawTexture(new Rect(c.xMax - 1, 0, Screen.width - c.xMax + 1, Screen.height), white);
            GUI.DrawTexture(new Rect(c.xMin, 0, d, c.yMin + 1), white);
            GUI.DrawTexture(new Rect(c.xMin, c.yMax - 1, d, Screen.height - c.yMax + 1), white);
            // Duplex reticle: thick outer posts, thin centre cross, red dot.
            float t = Mathf.Max(1f, Screen.height / 540f), gap = d * .06f;
            GUI.color = new Color(0f, 0f, 0f, .92f);
            GUI.DrawTexture(new Rect(c.xMin, cy - t * 1.5f, cx - gap - c.xMin, t * 3f), white);
            GUI.DrawTexture(new Rect(cx + gap, cy - t * 1.5f, c.xMax - cx - gap, t * 3f), white);
            GUI.DrawTexture(new Rect(cx - t * 1.5f, cy + gap, t * 3f, c.yMax - cy - gap), white);
            GUI.DrawTexture(new Rect(cx - t * .5f, c.yMin, t, d * .5f), white);
            GUI.DrawTexture(new Rect(cx - gap, cy - t * .5f, gap * 2f, t), white);
            GUI.DrawTexture(new Rect(cx - t * .5f, cy, t, gap), white);
            GUI.color = new Color(1f, .15f, .1f, .95f);
            GUI.DrawTexture(new Rect(cx - t * 1.5f, cy - t * 1.5f, t * 3f, t * 3f), white);
        }
        else
        {
            // Holographic / prism at low power: a red ring and dot over the (hidden) glass.
            if (ring == null) ring = MakeRing(512);
            float t = Mathf.Max(1f, Screen.height / 540f);
            GUI.color = new Color(1f, .2f, .14f, .95f);
            GUI.DrawTexture(new Rect(cx - t * 1.5f, cy - t * 1.5f, t * 3f, t * 3f), white);
            GUI.color = new Color(1f, .2f, .14f, .55f);
            float r = Screen.height * .03f;
            DrawCircle(cx, cy, r, t);
        }
        GUI.color = keep;
    }

    static void DrawCircle(float cx, float cy, float r, float t)
    {
        const int n = 48;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            GUI.DrawTexture(new Rect(cx + Mathf.Cos(a) * r - t * .5f, cy + Mathf.Sin(a) * r - t * .5f, t, t), white);
        }
    }

    // Black outside a circle, transparent inside, soft edge.
    static Texture2D MakeRing(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float h = size * .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + .5f - h) * (x + .5f - h) + (y + .5f - h) * (y + .5f - h)) / h;
                byte a = (byte)(Mathf.Clamp01((d - .9f) / .1f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
