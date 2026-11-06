# Architettura — 31 ottobre 2026

Riorganizzazione del codice Unity di Elderboom verso una struttura da produzione. Riguarda solo il codice: modelli, animazioni e audio non sono stati toccati, salvo l'abilità 2 di Eli, che è nuova.

## Livelli: la scena è la fonte di verità

- I generatori (`ElderboomPortBuilder`, `HDRPSliceBuilder`, il calibratore del viewmodel) non partono più da soli all'apertura dell'Editor (tolti `[InitializeOnLoad]` e i file flag). Sono in **Elderboom HDRP → Import Tools** e chiedono conferma prima di riscrivere la scena.
- `ElderboomArea` definisce zone con nome e volume; i cancelli (`ElderboomGate`) le sbloccano.
- `ElderboomSpawnPoint` può appartenere a una zona: gli zombie nascono solo nelle zone sbloccate.
- `ElderboomTrigger` è un volume con avviso, suono, sblocco di zona, spawn scriptati ed evento UnityEvent.
- `ElderboomEvents` raccoglie gli eventi statici del gioco: sessione, ondata iniziata/finita, uccisione, danno, morte, abilità, power-up, zona, trigger.
- Strumenti: **Elderboom HDRP → Level** (zone dai cancelli, spawn point e trigger nel punto della Scene view).

## Giocatore

`HDRPSlicePlayer` resta l'unico punto d'accesso; l'API pubblica non è cambiata.

| File | Contenuto |
| --- | --- |
| `HDRPSlicePlayer.cs` | salute, danno, sessione, ordine di aggiornamento |
| `HDRPSlicePlayer.Movement.cs` | macchina a stati del movimento (`ElderboomMotorState`: Idle, Walking, Sprinting, Crouching, Sliding, Vaulting, Airborne), sguardo, movimento, accovacciata, scivolata, scavalco |
| `HDRPSlicePlayer.Weapon.cs` | macchina a stati dell'arma (`ElderboomWeaponState`: Ready, Lowered, Equipping, Reloading, Ability, Disabled), sparo, ricarica |
| `HDRPSlicePlayer.Viewmodel.cs` | oscillazione, estrazione, ostruzione |
| `ElderboomPauseMenu` | impostazioni (caricamento, salvataggio, applicazione) e menu di pausa; gestisce Esc |
| `ElderboomInput` | assegnazione dei tasti, condivisa |

Si spara solo nello stato `Ready`. Gli stati vengono risolti per priorità a ogni frame e notificati con gli eventi `MotorStateChanged` e `WeaponStateChanged`.

## Zombie

`ElderboomZombieState` (Idle, Chase, Attack, Investigate, Wander, HitReact, Stunned, Lifted, Pushed, KeepAway, OffMesh, Dead) viene risolto per priorità, e ogni stato ha la sua funzione di aggiornamento. Il comportamento è quello di prima, con una differenza: passando da Wander a Chase o Investigate lo zombie ricalcola subito il percorso. Il cambio di stato è notificato con l'evento `StateChanged`.

## Operatori e abilità come dati

- Asset in `Resources/Elderboom/Operators` (`ElderboomOperatorDefinition`): id, scheda del menu (titolo, sottotitolo, testo, ritratto), armi iniziali e numero di slot, statistiche dell'arma, elenco delle abilità, ricarica condivisa o per abilità, durata dell'animazione.
- Ogni abilità è un asset (`ElderboomAbilityDefinition`) che contiene **parametri ed effetto**: raggi, durate, danni, forze e suoni si modificano dall'Inspector.
  - Soph: Plasma Volley, Blinding Flare.
  - Eli: Ignore Signal, **Acoustic Pulse** (nuova), Freeze Signal.
  - Tactical: Shockwave, Lift & Smash, Keep Away.
- **Acoustic Pulse** (Eli, abilità 2) non c'era nemmeno nel gioco Godot. Proietta un'esca sonora fino a 30 m nel punto mirato; gli zombie entro 18 m da quel punto smettono per 8 s di inseguire Eli e vanno a indagare lì. Ha un test di regressione.
- `ElderboomOperators` carica gli asset; se mancano, usa valori predefiniti identici.
- Menu: **Elderboom HDRP → Data → Create or Update Operator Assets** crea gli asset mancanti e sostituisce i segnaposto, senza toccare le modifiche già fatte.
- Le id e le categorie condivise sono costanti in `ElderboomIds`.

## ElderboomGame

Una sola classe divisa in quattro file:

| File | Contenuto |
| --- | --- |
| `ElderboomGame.cs` | sessione, punteggio e crediti, uccisioni, power-up, eventi |
| `ElderboomGame.Waves.cs` | ciclo delle ondate, spawn, intensità, atmosfera |
| `ElderboomGame.Abilities.cs` | selezione e ricariche delle abilità, servizi usati dagli asset delle abilità |
| `ElderboomGame.Menu.cs` | menu degli operatori nella scena e schermata di game over |

## Animazioni delle armi (Playables)

`ElderboomClipMixer` crea un PlayableGraph manuale per ogni viewmodel, con tre ingressi (idle, ricarica, abilità) e dissolvenze di 0,12 s. Il tempo di ogni clip è calcolato come prima, con i remap calibrati, quindi tempi e inquadrature non cambiano. Le clip legacy usano ancora `SampleAnimation`.

## Prestazioni

- Raycast dello sparo con buffer riusati (`RaycastNonAlloc`) e liste e set condivisi: nessuna allocazione per colpo nel percorso hitscan.
- I materiali degli effetti importati (fireball e particelle HDRP) sono condivisi per aspetto; prima ne veniva creato uno nuovo per ogni effetto e mai distrutto.
- Gli stili IMGUI del menu e della pausa sono in cache; l'overlay delle prestazioni conta gli zombie due volte al secondo.
- Gli interventi vengono dalla lettura del codice, non da misure col Profiler.

## Build

Gli script di test e anteprima (validazioni, rig di anteprima, cattura mappa) sono compilati solo nell'Editor e nelle build di sviluppo; la build finale non li include.

## Interfaccia (UI Toolkit)

Tutta l'interfaccia è passata da IMGUI a UI Toolkit: HUD, menu principale, pausa e game over.

- **Sistema di design** — `Resources/Elderboom/UI/ElderboomMenu.uss` definisce colori come variabili, tipografia, voci di menu con barra d'accento animata, pulsanti, schede, righe d'impostazione, slider con riempimento, interruttori, selettori ◀ ▶, tasti indicati a schermo, card, indicatori a tacche e animazioni d'ingresso. `ElderboomUi` costruisce i componenti e gestisce suoni (generati via codice, udibili anche in pausa), hover, invio da tastiera e animazioni.
- **Menu principale** — Marchio ELDERBOOM con emblema. Voci numerate con descrizione al passaggio del mouse. Percorso PLAY → OPERATORE → DIFFICOLTÀ → DEPLOY.
  - Scelta operatore: lista, scheda con equipaggiamento e abilità (nomi, tempi di ricarica e descrizioni presi dagli asset) e ritratto grande.
  - Difficoltà: card con indicatori di velocità, danno e numero di zombie.
  - Impostazioni: schede GAMEPLAY / DISPLAY / AUDIO / CONTROLS, pannello di dettagli e riassegnazione dei tasti.
  - Caricamento: video, operatore e difficoltà scelti, consiglio e indicatore di attesa.
  - Esc torna indietro.
- **Pausa** — Pannello laterale sopra il gioco oscurato, con ondata, uccisioni e crediti della partita in corso. Schede GAME / CONTROLS / VIDEO / AUDIO / ADMIN e righe d'impostazione con dettagli. Volume, luminosità, campo visivo e dimensione HUD si applicano subito; modalità schermo, risoluzione e qualità con APPLY.
- **Game over** — Titolo grande, statistiche (ondata, uccisioni, punteggio, precisione, tempo), PLAY AGAIN e MAIN MENU.
- **HUD** — Stesso aspetto di prima, ora in UI Toolkit: il testo viene riscritto solo quando cambia un valore.
- La scelta di operatore e difficoltà avviene solo nel menu principale. Avviando direttamente la scena di gioco (Editor) si parte con l'ultima scelta.

## Strumenti di sviluppo

- **Elderboom HDRP → Dev**: avvia una sessione, apri/chiudi la pausa o una sua scheda, forza il game over, carica il menu principale o una sua pagina. Servono a rivedere le schermate.
- **Elderboom HDRP → Editor → Play In Background** (attivo di default): nell'Editor la Play mode continua anche con Unity dietro altre finestre. Non riguarda la build. La regressione lo attiva per conto suo.

## Verifica

Regressione: 88 controlli su 89. L'unico fallito è il test sul materiale dell'HK53, che falliva già prima di queste modifiche.

## Scelte e limiti

- **IK delle mani: non fatto, per scelta.** Braccia e arma sono un unico modello animato e le mani sono già sull'arma in ogni clip. L'IK servirebbe solo con armi separate montate su braccia generiche.
- I remap di tempo delle animazioni di Soph ed Eli in `ElderboomViewmodel` sono ancora per operatore: sono dati di calibrazione.
- Copie di sicurezza degli script: `Logs/backup_split` e `Logs/backup_1031_1206`. La cartella Unity non è sotto git.


## Armi impugnate dalle braccia degli operatori (grip authored in Blender)

Ogni operatore (Soph, Eli, Tactical) tiene tutte le armi dei pack con le proprie braccia.

- Sorgente: `assets/grips/Operator_Grips_All.blend` (scene `Scene` = Soph, `Op_Eli`, `Op_Tactical`, `Pack_<id>`).
- Pipeline: `Logs/grip/*.py`
  - `extract.py` / `extract_gltf.py`: giunti canonici (spalla, gomito, polso, falangi) dai pack FBX/GLB.
  - `retarget2.py`: posa del pack sulle braccia dell'operatore (IK a 2 ossa con le lunghezze dell'operatore, dita copiate per direzione), arma allineata alla vista (asse canna), mano sinistra che segue l'arma, respiro dell'idle dell'operatore per i pack statici.
  - `*_setup.py` (ancore in spazio camera per operatore), `cfg_all.py` (parametri per arma), `run_ids.py`, `export_op.py`.
- Export: `Assets/Resources/Elderboom/Grips/<Operatore>_Grips.glb` con braccia, clip `<Operatore>_<id>_Idle` e punti `GripPt_<id>_W/I/M/P(.L)`.
- Runtime: `ElderboomAuthoredGrip.Build` (chiamato da `ElderboomArsenal.Create`): clona il viewmodel dell'operatore, sostituisce il modello con quello grips, prende la geometria dell'arma dal prefab del pack e la sposta rigidamente perché il sistema mano del pack coincida con i GripPt; poi la aggancia alla mano. Senza clip/GripPt torna al pack (es. `wrench`, HK53 di Soph che è già sul suo rig).
- Armi coperte: ar9, carbine, shotgun, sawnoff (doppia, canna sinistra sulla mano sinistra), grenade, minigun, flame, crowbar, knife, m1911, service, revolver, rhino, più le primarie degli altri operatori (akmn, sig550, striker, hk53; sorgente = viewmodel dell'operatore proprietario, `Logs/grip/oppack.py` + `run_op.py`; a runtime il pack è il suo `<Op>_Viewmodel_Pivot`, solo i nodi `stowedWeaponNodes`).
- Mira: il giocatore risolve l'ADS dai marker `ADS_Rear/Front` dell'arma spostata; i pack senza marker ricevono una linea di mira sopra la canna (`EnsureSights`), relief per categoria. Niente ADS per il melee.
- Reload: `ElderboomViewmodel.proceduralReload` (abbassa e ruota il modello attorno alla mano destra mentre l'idle continua) per le armi senza clip di reload. Fuoco: rinculo procedurale come prima.
- Da fare: wrench (pack senza braccia), reload animati per arma con il caricatore che si muove.

## Modello completo degli operatori (terza persona)
- Sorgente: `Characters/eli_anims.blend` (in Documents/Assets zombie game), cioè il rig di Meshy (ossa Mixamo) con 15 azioni importate dagli FBX Meshy.
- Export: `Assets/Resources/Elderboom/Characters/Eli/Eli_Rigged.fbx` contiene mesh e scheletro; in `Anims/Eli_<Clip>.fbx` c'è solo lo scheletro con una clip per file.
- `Editor/ElderboomCharacterImport.cs` imposta tutto in automatico: Humanoid, loop, root bloccata. Le clip che si spostano mettono l'XZ nella root motion, che viene scartata.
- `ElderboomCharacterBody` carica il modello e il materiale HDRP; con `Play("Ride")` fa il crossfade a 2 ingressi tramite Playables.
- Cavallo (`ElderboomMount`): mentre sei in sella, V (toggle_view) attiva la terza persona. Il corpo usa la clip `Ride`, la testa va dove stava la camera FP e la camera orbita con controllo delle collisioni.
- Menu (`ElderboomMenu`): l'anteprima 3D sostituisce il ritratto per gli operatori che hanno un modello completo, con `Bar_Hang_Idle` e la sbarra. Con DEPLOY parte `Fall_from_Bar` e poi la schermata di caricamento.
- Clip disponibili: Ride, Bar_Hang_Idle, Fall_from_Bar, Idle_5, Idle_8, Walking, Running, Rifle_WalkForward, Walk_Backward_with_Gun(_1), Walk_Backward_While_Shooting_inplace, Walk_Left_with_Gun, Crouch_Walk_Left_with_Gun_inplace, Throw_Grenade, Downed_ReachHelp.
