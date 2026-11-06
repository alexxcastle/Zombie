# Main weapon audio

Source: the user-provided `FreeWeaponSFX_v1.1` folder, exclusively `Handgun`,
`Shotgun`, and `AssaultRifle`. No special ability or grenade launcher sounds are
used. Source WAV files remain unchanged. This note records provenance rather
than asserting a new license; retain the source pack's original licensing terms.

One `gunshot_01` and one full `tail_01` define each category's character. Both
1911 and Rhino use Handgun; Striker12 uses Shotgun; HK417, SIG550, AKMN, and SMGs
use AssaultRifle. Two original aim and equip foley takes alternate for variety.

All assets remain PCM16 stereo at 44.1 kHz. Only initial near-silence is removed,
with 4 ms preroll. Constant gain creates headroom; no attack fade, compression,
EQ, or removal of the source endings is applied. Dry clicks are short excerpts
of a real same-category reload mechanical transient, with an 8 ms end fade.
`metadata.json` contains source paths, hashes, duration, gain, peak, RMS, and
processing details per file.

The playback engine uses a dedicated `Weapons -> SFX -> Master` route. Gunshot
voices are never stolen. Earlier tail voices release over 75 ms on the next
shot; the final tail is preserved through reload, aim, and weapon changes.
The Weapons bus has -3 dB gain and a -3 dBFS hard limiter; existing SFX and Master
levels and effects are preserved, including the mix of special abilities.
Foley and reload levels remain materially below the direct gunshot.

Animation event API: `setup(player)`, `set_weapon(category, name)`, `fire()`,
`dry_fire()`, `equip()`, `aim(bool)`, `reload_stage(stage, pitch=1.0)`, and
`cancel_reload()`. Reload stages: `mag_out`, `mag_draw`, `mag_in`, `bolt`;
Handgun additionally `open`; Shotgun uses `open`, `shell_draw`, `shell_in`,
`close`, and optional `bolt`. The caller schedules cues at the actual clip
positions and cancels stale cues when animations are interrupted.
