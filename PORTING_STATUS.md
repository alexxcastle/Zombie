# ZombieTest → Unity: current preview

Source project: `/Users/alessandrocastellani/zombie-test`.
Unity project: `UnityHDRPSlice`, Unity 6000.6.7f1, HDRP 17.6.
Entry scene: `Assets/Scenes/ZombieTest_Menu.unity`.
Gameplay scene: `Assets/Scenes/Elderboom_Unity_Port.unity`.

This preview is a work in progress, not a declaration of complete feature parity.
The original Godot assets and scripts remain the reference implementation.

## Included in the current preview

- English start menu, three operators, difficulty selection and saved settings.
- Loading video at 0.75× and 32 randomized English tips.
- Elderboom geometry, 55 reconstructed materials, chapel at 1.25×, reduced trees and cemetery stones, 18 gravel routes and perimeter building extensions.
- Static navigation bake from 1,796 collision sources; reachable spawn points, escalating waves, spawn-distance and visibility checks.
- FPS movement, sprint stamina, health, shooting, ADS, reload and mouse-wheel switching; Soph also has the additional HK53 alongside AKMN and Rhino.
- 18 weapon profiles including permanent knife and HK53; four weapon stations, medical station, third-slot upgrade station and paid courtyard gates.
- Soph volley and gradual orange-to-white flare; permanent zombie blindness with proximity aggression. Eli ignore/freeze effects timed to the button gesture. Tactical push, lift and keep-away gameplay states.
- Weapon-category gunshots/foley/reload audio, zombie spatial voices, footsteps and player exhaustion.
- Unified BFUI HUD with wave, remaining hostiles, ability/cooldown, notices, hitmarker, score, currency, slot count and temporary weapon timer.
- HK53 with authored 4-second idle and 3.5-second reload, animated charging handle/magazine, preserved PBR maps and iron-sight ADS.
- Denser blood spray and mist at the raycast impact, authored non-lethal ShotHit reactions on both zombies, and red hitmarker shards that fall and fade with Godot timing.

## Verification performed

- Saved-scene validation: no missing serialized scripts; required prefabs/navigation/stations present.
- Targeted review regression: 88 checks passed with zero errors, including three consecutive blind waves, cover, special weapons, inventory, ability cancellation, material bindings and display persistence.
- Runtime assertions: blindness is permanent, a kill awards credits exactly once, healing/damage transitions work, inventory initializes.
- Receipts: `Logs/elderboom_saved_validation.json`, `Logs/review/regression.txt`, `Logs/review/material_restore.txt` and `Logs/integration/verification.txt`.

- Standalone visual validation: all 18 weapon hip views, five ADS views, authored reload movement, stable FBX idle poses after ten seconds, and visible blood at gameplay distance. 36 checks passed with zero errors; receipts and captures in `Logs/visual_fix/player`.

## Still requiring implementation or parity verification

- Procedural reload fallbacks where the source lacks a dedicated clip; full artistic parity of the imported animation library.
- Cloud/backdrop presentation and restored foliage instances. Authored PBR bindings are preserved; FBX materials now transfer normal/metallic/AO/smoothness instead of color alone.
- Plasma-pistol-only recoil/light pulse details, authored Tactical ability overlays and full original VFX parity.
- Full artistic parity of special-weapon effects and original UI presentation. Gameplay now includes 3% special drops, 30-second temporary weapons, explosive/flame/pulse modes, bounded slots, replacement and third-slot purchase.
- Original multiplayer/lobby transport and remote-player replication.
- Original non-Elderboom Godot maps and their mission-specific systems.
- Full end-to-end playtest, performance measurement and loading-transition review.

Eli ability 2 remains unavailable because it was not authored in the source game;
the Unity preview does not invent a replacement ability.

## Storage

Godot `.godot` cache and identified project backups were deleted as requested.
Intermediate standalone builds and generated Unity cache were also removed during
the migration when the disk approached capacity. These caches are regenerable;
deleted backups are not retained in the Trash.

Embedded textures above 2048 px were downsampled only in the Unity copies of the
operator/sidearm GLBs. Geometry and animation buffers are retained. Original source
GLBs remain intact; each normalized copy has a `.normalization.json` receipt.

## Code review, 30 October 2026

All nine code-review findings are addressed. See `Docs/REVISIONE_UNITY_GODOT_2026-10-30.md` for the original diagnoses and resolution table; `Logs/review/regression.txt` contains the targeted runtime checks.
Integration evidence is in `Logs/integration/verification.txt` and its rendered captures.
The Blender source remains unchanged; only the Unity export removes the headless bust and bakes Rigify constraints into a simple deformation skeleton.

Visual corrections on 30 October preserve AKMN/HK53 framing, repair other imported weapon orientations and sight bindings, fix non-legacy animation sampling in the compiled Player, freeze demonstration-only FBX clips as in Godot, correct the brightness control, and spread eleven street lamps at a 4.2 m fixture height. See `Docs/CORREZIONI_VISIVE_2026-10-30.md`.

## Hit feedback and map export, 30 October 2026

Non-lethal hit reactions interrupt melee windups, pause navigation and return to locomotion; ability/death priority is preserved. Blood emission is denser and rapid hits are no longer suppressed for 180 ms. Falling red hitmarkers match `crosshair_hud.gd` and respect HUD/HiDPI scaling. Native Player verification passed 42 checks with zero errors (`Logs/hit_feedback/final/verification.txt`), followed by 88 passing gameplay regression checks. See `Docs/FEEDBACK_COLPI_2026-10-30.md`.

Full-terrain and village orthographic PNGs (4096 × 4096) are in `Exports/`, with smaller JPEG previews and capture parameters. Daylight capture settings were not saved into gameplay.

Sprint comfort adjustment: camera translation and roll/lean amplitudes during running are halved. Walking amplitudes, step timing and audio cadence are preserved. The standalone was rebuilt successfully.

HK53 ADS framing adjustment: lowered the aimed weapon view by one degree around the eye origin. Front/rear sights remain aligned on the same screen ray; hip framing and authored animations are retained. The builder stores this adjustment for future regeneration.
