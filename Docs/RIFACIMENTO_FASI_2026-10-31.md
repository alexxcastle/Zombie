# Rifacimento Elderboom — fasi 1–6, 31 ottobre 2026

Interventi successivi a `REVISIONE_CODICE_2026-10-30_SERA.md`. Tutto il codice compila in Unity 6000.6.7f1. Le versioni precedenti dei file modificati sono state conservate durante il lavoro.

## Fase 1 — Feel dello sparo e del danno (`ElderboomFeel.cs`, `Resources/Elderboom/Shaders/FeelParticles.shader`)
- Rinculo della visuale per arma (parte del calcio resta nella mira), scuotimento a trauma per spari, esplosioni, Shockwave, Lift & Smash e plasma.
- Impatti sul mondo con polvere, schegge, scintille, fori persistenti (pool di 96) e suono per superficie (pietra, legno, metallo, terra).
- Fiammata alla volata e traccianti; tonfo "carne" sugli zombie colpiti.
- Danno subito: flinch, vignetta rossa, archi direzionali attorno al mirino, battito sotto 35 HP.

## Fase 2 — Movimento (`HDRPSlicePlayer.Movement.cs`)
- Ctrl: accovacciarsi (con controllo del soffitto); Ctrl durante lo sprint: scivolata; salto dalla scivolata.
- Spazio contro ostacoli da 0,45–1,35 m: scavalcamento o salita.
- F: torcia (spenta lascia una luce minima sull'arma).
- Passi per superficie e atterraggi. Audio: Kenney Impact Sounds, CC0 (`Resources/Elderboom/Audio/kenney`, licenza inclusa).

## Fase 3 — Armi in mano (`HDRPSlicePlayer.Viewmodel.cs`)
- Dimensione dell'arma indipendente dall'impostazione FOV (radice `ViewmodelFov` con compensazione in Z; nessun cambiamento a FOV 72; ADS invariato).
- Ondeggiamento con il mouse, inclinazione laterale, estrazione al cambio arma, ritardo sprint→sparo, arma ritirata contro i muri.

## Fase 4 — Nemici e ritmo
- Varianti: runner (da ondata 3, veloci e fragili) e brute (da ondata 5, resistenti, più grandi, meno respinti dalle spinte).
- Udito: spari ed esplosioni attirano zombie accecati o vaganti.
- Accerchiamento: a media distanza gli zombie si distribuiscono attorno al giocatore.
- Director: dopo un picco di danno subito gli spawn rallentano fino a 6 s.
- Watchdog: zombie fuori NavMesh o bloccati vengono riposizionati, poi ritirati senza ricompensa; uno spawn fallito non blocca l'ondata; percorso calcolato dal punto NavMesh più vicino al giocatore.
- Spinte con `agent.Move`: non attraversano più muri sottili o cancelli chiusi.

## Fase 5 — Atmosfera audio
- Grilli tra un combattimento e l'altro, musica che sale con la pressione; entrambe seguono il volume musica. L'audio si ferma in pausa.

## Fase 6 — Sistemi di gioco e HUD (`ElderboomPowerUp.cs`)
- Power-up 4%: Max Ammo, Insta-Kill (20 s), Double Points (20 s).
- HUD: testo con ombra, banner d'ondata, timer power-up, uccisioni, "+100" fluttuanti, minimappa con cornice, direzione e varianti.
- Fine partita: ondata, ondate superate, uccisioni, punteggio, precisione, tempo; PLAY AGAIN.

## Bug della revisione corretti
Pause: impostazioni video non applicate scartate alla chiusura; tasti duplicati scambiati; token di sessione del Plasma Volley; colpo melee che ignorava l'abilità di Eli; START del menu interno passa da `BeginSession`; cooldown di Lift coerente (9 s); maschera layer centralizzata; sangue in pool (24); coroutine morta rimossa; attesa del test lanciafiamme 1,4→1,6 s.

## Strumenti
`Elderboom HDRP → Run Review Regression`: regressione dall'Editor senza chiuderlo. Richiede Unity visibile: macOS (App Nap) sospende la scena in Play quando la finestra è nascosta.

## Ancora aperti
- Grafica e asset: siepi, lampioni d'epoca, alberi sovraesposti, braccia coerenti, nuovi modelli di zombie; luce e nebbia volumetrica vanno regolate guardando la scena.
- Materiali HK53 diversi dal glTF originale (test di regressione), dopo la rigenerazione delle 19:16 del 30 ottobre.
- Rig di validazione ancora compilati nella build; automazioni [InitializeOnLoad] a file flag.
- Multiplayer, gamepad, localizzazione.
