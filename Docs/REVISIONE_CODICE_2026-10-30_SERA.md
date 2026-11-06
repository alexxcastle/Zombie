# Revisione del codice Unity — 30 ottobre 2026 (sera)

Seconda revisione statica di `UnityHDRPSlice`, successiva a `REVISIONE_UNITY_GODOT_2026-10-30.md`. I nove punti di quella revisione risultano risolti e non sono ripetuti qui.

**Ambito:** 38 script runtime in `Assets/Scripts` letti integralmente (i rig di validazione solo per le parti a rischio); i 33 script in `Assets/Editor` controllati per pattern rischiosi, non riga per riga. Nessuna esecuzione in Unity: le correzioni sotto vanno compilate e verificate con la regressione esistente. Le osservazioni sulle prestazioni sono stime da lettura, non misure del Profiler.

## Corretti in questo intervento

| # | Problema | Correzione | File |
| --- | --- | --- | --- |
| 1 | La stazione munizioni impostava la riserva a 4 caricatori: per Sawnoff (30→8), 1911 (56→32), Rhino (36→24) comprare munizioni le riduceva. Addebitava anche a riserva piena o col coltello, e riempiva solo l'arma attiva. | Riempimento di tutti gli slot ordinari fino alla riserva del catalogo, mai al ribasso; coltello e arma speciale esclusi; "AMMO ALREADY FULL" senza addebito. | `ElderboomArsenal.RefillAmmo`, `HDRPSlicePlayer.RefillAmmo/SetReserveAmmo`, `ElderboomStation` |
| 2 | Acquisto con inventario pieno mentre si impugna coltello o arma speciale: l'indice -1 diventava 0 e veniva sostituita l'arma primaria dell'operatore. | Si sostituisce l'ultimo slot ordinario selezionato (`selectedRegular`), coerente con il controllo di regressione esistente. | `ElderboomArsenal.Add` |
| 3 | Alla morte "YOU ARE DOWN…" si sovrapponeva a "ELDERBOOM FALLEN". | Il messaggio del player compare solo senza `ElderboomGame`. | `HDRPSlicePlayer.OnGUI` |
| 4 | Dopo la morte Esc apriva la pausa; chiudendola il cursore veniva bloccato sopra la schermata di game over. | Pausa non apribile da morto; il cursore non viene ribloccato da morto. | `HDRPSlicePlayer.Update/SetSettingsOpen` |
| 5 | Il volume musica era salvato ma mai applicato (musica del menu fissa a 0.35); nel menu non venivano applicati volume generale ed effetti. | Il menu applica le tre preferenze audio all'avvio e in tempo reale; aggiunti gli slider musica ed effetti alle impostazioni del menu. | `ElderboomMenu` |

Nota: in partita non c'è musica, quindi lo slider musica della pausa ha effetto sulla musica del menu.

## Ancora aperti — priorità media

6. **Push attraverso muri e cancelli** — `ElderboomZombie.cs` (ramo push in `Update`) sposta lo zombie con `Warp` sul punto NavMesh più vicino entro 2 m. Può superare muri sottili o un cancello chiuso (ostacolo con carving). Usare `agent.Move` o `NavMesh.Raycast`.
7. **Ondate che non finiscono** — uno zombie fuori NavMesh resta inerte ma conta per l'ondata; `SpawnZombie` può fallire senza correggere `waveRemaining`; `SelectSpawn` calcola il percorso verso la posizione grezza del giocatore, che può fallire su oggetti rialzati o in salto. Aggiungere un watchdog (zombie fermo da N secondi → riposizionare o ritirare) e campionare la posizione del giocatore con `SamplePosition`, conservando l'ultima valida.
8. **Pausa: impostazioni video non confermate salvate** — la chiusura del menu salva risoluzione/qualità scelte ma non applicate e applica subito lo schermo intero.
9. **Token di sessione inefficace** — `PlasmaVolley` confronta `session` con se stessa; catturare il token all'avvio della coroutine.
10. **Minori** — la carica melee (0,42 s) non ricontrolla l'abilità ignore di Eli; il pulsante START del menu interno salta `BeginSession`; l'audio non va in pausa (`AudioListener.pause`); il rebinding consente tasti duplicati.

## Prestazioni (da misurare)

- Sangue: ogni colpo crea un GameObject con due particle system configurati da zero. Pool consigliato (minigun, lanciafiamme, shotgun).
- `Fireball`/`HdrpParticle` creano un `Material` per istanza mai distrutto.
- Ogni sparo alloca `RaycastAll`, ordinamenti, List/HashSet/Dictionary: usare versioni NonAlloc.
- Overlay prestazioni: `FindObjectsByType` a ogni evento OnGUI. Menu e pausa creano `GUIStyle` a ogni chiamata.

## Struttura e igiene

- I rig di validazione (`ElderboomVisualValidation`, `ElderboomHitFeedbackValidation`, `ElderboomViewAuditRig`, `ElderboomMapCapture`…) sono in `Scripts/` e finiscono nella build: spostarli in un assembly separato o sotto `#if DEVELOPMENT_BUILD`.
- Automazioni `[InitializeOnLoad]` a file flag (`ElderboomBuildKick`, `ElderboomPortAutoBuild`, `ElderboomSaveAndQuit`) fermano il Play mode, cancellano `Builds/`, rigenerano la scena e chiudono l'Editor. Un flag rimasto avvia tutto all'apertura. Preferire `-executeMethod`. Le scene generate dal codice perdono le modifiche manuali alla rigenerazione.
- Layer 19 scritto a mano in circa otto punti invece di `ElderboomCombat.WorldMask`. Cooldown Lift 9 s nel codice contro 12 s in `AbilityCooldown`, che ignora il proprio parametro. Codice morto: `WeaponRecoil`, `HDRPSliceZombie`.
- Restano valide le indicazioni precedenti: separare `HDRPSlicePlayer` (input/movimento, arma, impostazioni/pausa) ed `ElderboomGame` (director ondate, abilità, menu) e sostituire gli identificatori stringa con dati tipizzati.
