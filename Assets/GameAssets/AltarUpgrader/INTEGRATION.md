# Church altar weapon upgrader

Installed in `Assets/Scenes/Elderboom_Unity_Port.unity` as `Church_Altar_Weapon_Upgrader`.
The scene object and `AltarUpgrader.prefab` use independent meshes, BC7 PBR textures and a synchronized six-second `UpgradeCycle` animation. Source GLB and its verified SHA256 are retained in `Source/`; the runtime prefab does not depend on that source GLB.

Approach the front tray from the church nave, look at it and press the configured Interact key (default E). Cost: 3,000 credits. Only a permanent firearm can be upgraded, once per owned weapon. The weapon is displayed on the animated tray; firing, reloading, changing weapons, other purchases and abilities are locked until it returns after six seconds. Walking and looking remain available.

Upgrade: damage x2; magazine and reserve x1.5 rounded up, fully refilled. The catalog is never edited. The owned inventory slot retains its upgrade through weapon switches and reloads. Replacing/selling that weapon removes its upgrade; a new game starts from the base catalog. Melee and temporary drops are excluded. Interrupting the transaction restores the weapon and refunds the payment in the same session.

A separate object-space crimson vein pass follows every selected weapon mesh, including skinned/animated parts. Original PBR textures, normals and operator arms are retained. `SOCKET_Weapon` is the display attachment point. The source model contains nine independent controls for covers, tray, clamps and induction heads.

Co-op: the host grants one reservation at a time; the apparatus owner and cycle time are sent in world snapshots, and timed-out reservations expire. Inventory/currency stay per-player, consistent with existing shops. Network protocol is version 4; all players need the same updated build. This reservation integration has compiled, but a multi-machine network session has not been exercised in this task.

Lighting: the night profile is fixed EV 7.4 (previous art pass 5.8, earlier baseline 7.2). A higher fixed EV makes the scene darker. Camera-mounted flashlight volumetric dimmer is zero in the saved scene and at runtime, retaining direct surface illumination. Local machine lights also have zero fog contribution.

Verification: `Logs/AltarIntegration/validation.txt` (imported clip, nine controls, shader, detached prefab), `play-validation.txt` (21 passing runtime assertions, real Rhino geometry), `altar-installed.jpg`, `altar-processing.jpg` and `crimson-weapon-detail.jpg`. Runtime tests use the actual station prefab and player/arsenal code in a minimal harness, because rendering the entire map exceeded this Mac's memory. The church placement captures use the saved church and scene lighting. Existing standalone builds were not rebuilt or published.
