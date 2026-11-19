CuiGames — SCALE / ORIENTATION FIX (2026-11-07)
CURRENT FILES: metric meters; glTF/FBX Y-up, muzzle -Z.
Blender source: metric meters, Z-up, muzzle +Y (verified glTF round-trip).
Uniform size factor vs original published GLB: 1. Other packs keep original size.
Calibration root scale is 1. Original pivot intent retained; lateral centering applied.
All published GLB clips retained; sampled animation checks completed.
IMPORTANT: old embedded Blender production/playback setup scripts contain original-unit
literal coordinates. Use the preserved Action/NLA clips directly; do not run those legacy
setup scripts. Unassigned historical helper Actions are retained, not all are verified.
M338 floating magazine in rest pose is intentionally NOT repaired.
Prior README follows for credits/license/history; old arbitrary-unit / axis statements
below are superseded by the calibration notes above.
----------------------------------------

A5 Carbine – Base (Graphite)
by @GodTrigger0  ·  https://cuigames.itch.io

CONTENTS
  A5_Base.glb   A5_Base.blend   textures/
  README.txt, LICENSE.txt
Each skin is a complete standalone model (you do not need the base model).

FORMATS
- .glb  glTF 2.0 binary, PBR materials and textures embedded, 12 named animation clips.
- .blend  editable Blender source, textures packed inside the file
  (the textures/ folder holds PNG copies for convenience).
- ~102k triangles per model, real-world scale, Z-up in Blender / Y-up in glTF.

ANIMATIONS (in every GLB)
  Idle, Equip, Holster, Inspect, Recoil_Stylized, Fire_Showcase, Eject_Showcase,
  Charge_Showcase, Reload_Showcase, Fire_Auto_Start, Fire_Auto_Loop, Fire_Auto_End  (12 clips, 30 fps)
In Blender: Scripting tab -> text "R07_Activate_Clip.py" -> set CLIP at the top -> Run Script,
then press Space to play. Set CLIP = 'REST' to return to the neutral pose.

BLENDER VERSION
The .blend files were saved with Blender 5.0. Use Blender 5.0 or newer to open them.
The .glb files work in any glTF 2.0 viewer/engine.

KNOWN LIMITS
- Some older, untouched surface areas keep minor UV stretching/degradation; there is no
  single unified texture atlas.
- Visual exterior prop for games/renders: no functional internals.
- Not tested inside a specific game engine (Unity/Unreal/Godot import should work via glTF,
  but materials, mipmaps and animation controllers are your responsibility).
- Fictional design, not affiliated with or endorsed by any real firearm manufacturer.
- Created with AI-assisted tools and refined by the creator.
