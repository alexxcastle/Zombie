# Revisione completa del progetto — 6 novembre 2026

Progetto: `zombie-test/UnityHDRPSlice` (Unity 6000.6.7f1, HDRP 17.6). Dati raccolti oggi da disco, codice, impostazioni e dal report dell'ultima build Mac (8,1 GB).
Le revisioni precedenti (`Docs/REVISIONE_CODICE_2026-10-30_SERA.md`, `Docs/RIFACIMENTO_FASI_2026-10-31.md`) restano valide per i punti non ripresi qui.

---

## In sintesi: le 6 cose che contano di più

| # | Problema | Effetto oggi | Intervento |
|---|---|---|---|
| 1 | **Il progetto Unity non è sotto controllo versione** | Nessun modo di tornare indietro; più agenti (Codex e Claude) modificano gli stessi file in parallelo | Git + LFS solo per `UnityHDRPSlice`, commit a ogni sessione, un agente alla volta |
| 2 | **Texture non compresse dentro i file .glb** | 7,0 GB degli 8,1 GB della build sono texture (88%); RAM e VRAM esaurite su un Mac da 8 GB | Estrarre e comprimere le texture, una sola copia per le braccia di ogni operatore |
| 3 | **Disco pieno** (5,5 GB liberi) | Build "riuscite" ma corrotte (crash all'avvio di lunedì) | Pulizia (sotto) + controllo di spazio e integrità nella build |
| 4 | **3,9 GB di pacchetto demo e altri asset inutilizzati** dentro `Assets` | Import lenti, Library da 24 GB, rischio che finiscano nella build | Tenere solo i pochi asset usati, togliere il resto |
| 5 | **Due interfacce (UI Toolkit + IMGUI) e due reti (LAN grezza + Relay)** | Prompt incoerenti e sovrapposti; in LAN gli eventi possono perdersi | Un solo sistema di prompt; Unity Transport anche in LAN |
| 6 | **Molto lavoro recente mai provato in gioco** | Possibili regressioni nella prossima build | Sessione di test guidata prima di nuove funzioni |

---

## 1. Critico

### 1.1 Nessun controllo versione per il progetto Unity
- La cartella `zombie-test` ha un repository git, ma è quello del vecchio progetto **Godot**: ultimo commit 27 ottobre, **0 file di `UnityHDRPSlice` tracciati**, 461 file modificati non committati.
- Tutto il lavoro Unity (scene, script, mappa a blocchi, impugnature) esiste in **una sola copia** su un disco quasi pieno.
- Nei log di oggi ci sono build e modifiche fatte da un altro agente (`MacBuild-Uncompressed.log`, `Build-Mac-InputFix…`, `VillageExpansion…`, `AltarUpgrader`), in parallelo al mio lavoro. Senza git non si vede chi ha cambiato cosa, e due modifiche contemporanee alla scena si sovrascrivono.

**Da fare**
1. Repository git dedicato a `UnityHDRPSlice`, con `.gitignore` Unity (`Library/`, `Logs/`, `Builds/`, `UserSettings/`, `Temp/`, `obj/`) e Git LFS per `.glb .fbx .png .jpg .tif .exr .wav .asset` grandi. In alternativa Unity Version Control, già integrato nell'Hub.
2. Scene e prefab in modalità testo (già così) e "Visible Meta Files".
3. Regola di lavoro: **un agente alla volta sul progetto**, commit all'inizio e alla fine di ogni sessione.
4. Copia di backup su un disco esterno o nel cloud, almeno di `Assets/`, `ProjectSettings/`, `Packages/` e della cartella `assets/` (13 GB di sorgenti Blender).

### 1.2 Texture non compresse nei .glb (il problema di prestazioni n. 1)
I modelli importati con glTFast portano le immagini incorporate nel `.glb` come texture **4096×4096 non compresse** (circa 89 MB l'una con le mipmap). Dal report della build:

| File | In build |
|---|---|
| `Soph/HK53/HK53_FPS.glb` | 885 MB |
| `Tactical/Abilities/KeepAway.glb` | 707 MB |
| `Tactical/Abilities/LiftSmash.glb` | 707 MB |
| `Resources/Elderboom/Menu/OperatorSet.glb` | 616 MB |
| `Resources/Elderboom/Mounts/MechHorse_Rider.glb` | 439 MB |
| `Soph/Soph_AKMN_Plasma.glb` | 352 MB |
| `Sidearms/1911_idle.glb` | 253 MB |
| `Eli/Eli_Acoustic_Ignore.glb` | 246 MB |
| `ElderboomVillage/generated/elderboom_gate_leaf.glb` | 238 MB |
| `Tactical/Striker-12-shockwaveattack.glb` | 194 MB |
| `ArtDirectionGenerated/RuntimeMaterials` (`PBR_*.asset`, 21 MB l'uno) | ~900 MB nel progetto |

- Le **stesse braccia di Tactical** (con le stesse texture) stanno in 4 file diversi: Striker, LiftSmash, KeepAway, `Tactical_Grips`. Quindi 4 copie in memoria.
- Effetti: build da 8 GB (download e aggiornamenti lenti per gli amici), fino a 3,7 GB di memoria allocata in partita (misurata nell'editor) su un Mac con 8 GB in tutto, scatti, chiusure di Unity e build corrotte per disco pieno.

**Da fare**
- Estrarre le immagini dei `.glb` in file `.png` separati, importati con la compressione di Unity (BC7, o DXT5 per le maschere) e un limite di dimensione: 2048 per armi e braccia in prima persona, 1024 per oggetti di scena, 512 per oggetti piccoli o lontani. Rimappare i materiali.
- Un solo set di texture e materiali per le braccia di ogni operatore, condiviso da grips, Striker, LiftSmash e KeepAway.
- Rifare i `PBR_*.asset` di ArtDirectionGenerated con texture compresse esterne.
- Cielo `Deep Dusk Equirect.png` (96 MB): importarlo come cubemap compressa a 2048.
- **Stima**: build da 8 GB a circa 1,5–2 GB e 2–3 GB di RAM/VRAM in meno in partita. È l'intervento con il miglior rapporto costo/beneficio di tutto il progetto.

### 1.3 Disco e build silenziosamente corrotte
- Liberi oggi: 5,5 GB. Occupano: `Library` 24 GB, `Builds` 8,1 GB, `Logs` del progetto 1,4 GB, `assets/` 13 GB, `TerrainDemoScene_HDRP` 3,9 GB.
- Lunedì Unity ha dichiarato la build "Succeeded", ma `sharedassets1.assets` era corrotto: il gioco crashava all'avvio.

**Da fare**: lo script di build deve rifiutarsi di partire con meno di 20 GB liberi e, a fine build, verificare che i file dei dati si aprano. Un'unica procedura build + verifica + push su itch per Windows e Mac.

---

## 2. Da togliere

| Cosa | Dimensione | Note |
|---|---|---|
| `TerrainDemoScene_HDRP` (scene, Terrain demo 2,8 GB, VFXGraphs, LensFlares, copia di TextMeshPro…) | 3,9 GB | Si usano solo: alberi `Pine_A–D` e `Conifer`, le rocce, `Red_Bush`, `WaterPlane` e i layer del terreno. Spostarli in `GameAssets/Nature`, cancellare il resto |
| Pacchetto **URP** e `UniversalRenderPipelineGlobalSettings.asset` | — | Il progetto è HDRP: nessuno script lo usa. Allunga compilazione e shader |
| Pacchetti **Cinemachine, Timeline, Visual Scripting, VS Code** (deprecato), **2D Sprite** | — | Nessun riferimento nel codice |
| Scene non in build: `Museum_First_Map_HDRP`, `Elderboom_HDRP_PlayableSlice`, `AbilityPreview`, `NightVfxPreview`, `ShockwavePreview`, `ViewAudit`, `Scenes/Backups`, `_Recovery/0.unity` | ~1 GB con gli asset collegati | Con `GameAssets/Museum` (170 MB) ed `ElderboomHouseSample.glb` (154 MB, usato solo dalla PlayableSlice) |
| Script di test compilati nella build: `ElderboomViewAuditRig`, `VisualValidation`, `HitFeedbackValidation`, `AbilityPreviewRig`, `MapCapture`, `ValleyShot`, `NightVfxRig`, `ShockwaveRig` | ~1.500 righe | Nessuno è nelle scene di gioco: spostarli in `Dev/` con un assembly solo per l'editor |
| Impugnatura con IK a runtime (`ElderboomOperatorGrip` + parte di `ElderboomArmIK`) | ~360 righe | Disattivata (`UseOperatorGrip = false`) da quando esistono le impugnature fatte in Blender |
| Strumenti editor: **71 script, 13.466 righe** (quanto il gioco); **21 `[InitializeOnLoad]`, 20 dei quali controllano file "flag" ogni 2 s** (compresi i miei `ElderboomClaude*`) | — | Tenerne 4–5 (build, push, smoke test, audit armi, mappa) riuniti in un solo strumento con un solo controllo; archiviare gli altri |
| Radice `zombie-test`: avanzi Godot (`.gd`, `addons/`, una trentina di `.glb`/`.png` sciolti, `Unity/` e `Unreal Engine/` di esempio) | ~100 MB | Spostarli in `Archivio_Godot/` |
| `Logs/` del progetto (foto degli audit, vecchi log di build), `Exports/`, `claude_xfer/` | ~1,5 GB | Tenere solo gli ultimi report |
| `AltarUpgrader/Source` (173 MB di sorgenti dentro `Assets`) | 173 MB | I sorgenti vanno in `assets/`, fuori dal progetto |

---

## 3. Organizzazione

- **Nomi rimasti dai prototipi**: `HDRPSlicePlayer` (il giocatore), `ZombieTest_Menu`, `Elderboom_Unity_Port`, `HDRPSliceStandaloneBuild`. Da rinominare in `Player`, `MainMenu`, `Elderboom`, `BuildTools`, con attenzione a riferimenti e meta.
- **Cartelle degli asset sovrapposte**: `GameAssets/Ported`, `GameAssets/Elderboom`, `Generated`, `ElderboomGenerated`, `ArtDirectionGenerated`, `ThirdParty`, `Solodream`, `Vefects`, `Mirza`, `TwoUncleVFX`, `AllSkyFree`. Proposta: `Art/Characters`, `Art/Weapons`, `Art/World`, `Art/VFX`, `Audio`, `ThirdParty/<venditore>`.
- **`Resources` (324 MB)**: tutto quello che sta in `Resources` finisce nella build anche se non usato. Tenere lì solo ciò che si carica per nome; il resto con riferimenti diretti o Addressables.
- **Script**: 84 file in una sola cartella. Suddividerli in `Player/`, `Weapons/`, `Zombies/`, `Abilities/`, `Net/`, `UI/`, `World/` con assembly definition: compilazione più veloce e dipendenze chiare.
- **File troppo grandi**: `ElderboomLan.cs` (1.220 righe: protocollo, LAN, Relay, snapshot, avatar), `ElderboomMenu.cs` (815), `ElderboomMount.cs` (492). Dividere la rete in trasporto, messaggi e sincronizzazione del mondo.
- **Due sistemi di interfaccia**: HUD e menu sono in UI Toolkit, ma i prompt di interazione (stazioni, cancelli, pickup, cavallo, altare, nomi dei giocatori LAN) usano `OnGUI` in 10 script. Lo stile è diverso, si sovrappongono quando sei vicino a due oggetti e costano a ogni frame. → Un solo `InteractionPrompt` nell'HUD, che mostra l'oggetto più vicino.
- **Due percorsi di rete**: in LAN i pacchetti vanno su UDP senza affidabilità, quindi colpi, raccolte, power-up e aperture dei cancelli possono andare persi. Online invece passano dalla pipeline affidabile di Unity Transport. → Usare Unity Transport anche in LAN: stesso codice, stessa affidabilità.
- **Tre modi di mostrare le abilità**: clip sull'intero viewmodel, scambio del modello (Tactical), braccio sinistro sopra l'arma (nuovo). Una volta verificato il terzo, portarci tutto e togliere gli altri due.
- **Documentazione**: 6 documenti in `Docs/`; `PORTING_STATUS.md` è superato (cita 4 stazioni, cancelli "di cortile"…). Tenere un solo documento vivo "stato del progetto" più l'architettura.

---

## 4. Da ottimizzare (oltre alle texture)

| Area | Oggi | Intervento |
|---|---|---|
| Skinning | `gpuSkinning: 0` | Attivare lo skinning su GPU: con 40–60 zombie animati libera la CPU (nell'editor il thread principale era al 92%) |
| Backend di script | Mono | IL2CPP per le build di rilascio: logica più veloce. Mono va bene in sviluppo |
| Draw call | ~4.700 per frame nella vista del villaggio | Attivare GPU Resident Drawer / static batching per i ~2.000 oggetti statici del villaggio |
| Mesh in build | 866 MB | Compressione mesh, togliere Read/Write dove non serve, LOD per edifici generati e zombie (skinned) |
| Ombre | Molte luci e oggetti fanno ombra | Distanza ombre per qualità, lampioni senza ombre oltre i 20–25 m |
| HUD | `PlayerPrefs.GetFloat` a ogni frame | Leggerlo solo quando cambia l'impostazione (minimo, ma gratuito) |
| Misure | Il log FPS c'è, ma i dati veri sono solo dell'editor | Una partita con la build fino all'ondata 10, poi decidere su dati reali |

---

## 5. Bug noti e lavoro non verificato

- **Abilità sopra l'arma** (braccio sinistro e guanto di Tactical): compila, mai visto in gioco.
- **Mappa a blocchi**: provata solo in automatico (zombie solo dai blocchi aperti, cancelli che sbloccano). Non giocata; il distributore "ricompensa" nel bosco non è verificato.
- **Itch**: il canale Windows è fermo al 1° novembre. Gli amici hanno una versione incompatibile con la tua per il multiplayer.
- **Multiplayer**: gli avatar degli altri giocatori tengono sempre l'AKMN; gli effetti delle abilità non si vedono agli altri; la causa di "connection to host lost" va confermata con `lan_log.txt` dopo una partita.
- **Armi**: mira del Rhino con Tactical fuori schermo; pose di 1911, Service e Revolver con alcuni operatori.
- **Input**: solo il vecchio Input Manager (niente gamepad). Il blocco dei tasti è tamponato leggendo lo stato dal sistema operativo.

---

## 6. Da implementare (in ordine)

1. Controllo versione e backup (§1.1).
2. Procedura di build unica: controllo spazio → build → verifica integrità → push su itch Windows (e Mac quando serve).
3. Texture compresse e braccia condivise (§1.2).
4. Prompt di interazione unico nell'HUD.
5. Unity Transport anche in LAN; avatar remoti con l'arma giusta e abilità visibili agli altri.
6. Gamepad (Input System), localizzazione italiano/inglese, salvataggio di record e statistiche.

---

## Piano consigliato

| Settimana | Lavoro | Risultato |
|---|---|---|
| 1 | Git e backup · pulizia (TerrainDemo, pacchetti, scene, Logs, radice Godot) · procedura di build con verifica | Progetto al sicuro, 15–20 GB liberi, build affidabili |
| 2 | Texture compresse e braccia condivise · GPU skinning · IL2CPP · GPU Resident Drawer | Build ~2 GB, molta più memoria libera, FPS misurati in build |
| 3 | Test guidato di mappa, abilità e armi · prompt unico · rete LAN affidabile | Prima versione stabile da dare agli amici |
| poi | Riorganizzazione di cartelle e script, nomi definitivi, gamepad, localizzazione | Progetto pronto a crescere |
