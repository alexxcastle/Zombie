# Revisione Unity rispetto a Godot — 30 ottobre 2026

## Valutazione

UnityHDRPSlice è un porting parziale giocabile, con logica semplificata rispetto a Godot. Movimento, stamina, navigazione, danno e abilità hanno una base riutilizzabile. Il problema principale è che dati e modelli importati non garantiscono la presenza della relativa logica di gioco. Un'arma nel catalogo può esistere senza avere il comportamento originale.

Revisione statica dei controller runtime proprietari e della pipeline di import delle armi, con confronto diretto di `player.gd`, `zombie.gd`, `facility_map.gd`, `weapon_balance.gd`, `combat_rules.gd`, `encounter_rules.gd`, `player_sprint_stamina.gd`, `soph_plasma_ability.gd` e relativi sistemi ADS. Le librerie Unity/HDRP e tutti gli shader di terze parti non sono stati sottoposti a un audit completo. Le misure di prestazione richiedono un profiler; quanto indicato sotto come rischio prestazionale è distinto da un collo di bottiglia misurato.

## Esito delle correzioni

I nove problemi sotto sono stati corretti. La descrizione originale è conservata come diagnosi del comportamento precedente, non come elenco di difetti ancora aperti.

| Punto | Correzione applicata | Prova di regressione |
| --- | --- | --- |
| 1 — Ondate/accecamento | Pressione separata dai vivi nel mondo; gli accecati liberano la capacità dei pacchetti in attesa; tetto mondiale 32, ritiro dopo 20 s a oltre 24 m e fuori dalla vista, senza ricompensa. | Tre ondate reali consecutive completate con accecamento fino all'avvio della quarta; limite mondiale e crediti invariati. |
| 2 — Camera/canna | Percorso condiviso camera–volata–punto mirato, esclusione del proprietario, penetrazione tra attori che si arresta alle pareti. Marcatori di volata per tutti i prefab. | Copertura prima/dopo la canna, bersaglio a contatto, hitbox multiple, due bersagli allineati e muro intermedio. |
| 3 — Morte/abilità | Annullamento della sessione: coroutine, luci, overlay, proiettili, freeze, ignore e sollevamento. Ripristino dell'arma e annullamento della ricarica. | Morte prima e dopo il primo colpo plasma, durante il flare, freeze, ignore, lift, keep-away, reload e cambio di sessione. |
| 4 — Armi speciali | Modalità di fuoco esplicite, granata con traiettoria e area, stream del lanciafiamme, impulsi ad area, penetrazione e bloom. Drop al 3%, raccolta e durata di 30 s. | Proiettile esplosivo e danno a due attori, effetto fiamma e rilascio del grilletto, impulso, raccolta unica e scadenza del drop. |
| 5 — HUD | Un solo HUD mostra ondata, rimanenti, abilità/cooldown, messaggi, hitmarker, slot e timer speciale. Punteggio centralizzato anche per le abilità; lista zombie e stili memorizzati. | Uccisione da sparo/abilità, punteggio e crediti assegnati una sola volta, stato hitmarker e messaggi accessibili al renderer HUD. |
| 6 — Inventario/economia | Due slot ordinari, acquisto del terzo a 500 crediti con bonus movimento, sostituzione dello slot attivo, coltello fuori dagli slot e speciale temporanea separata. Soph conserva i tre oggetti del loadout richiesto, incluso HK53. | Limite prima/dopo upgrade, rifiuto doppio acquisto, sostituzione, coltello, arma temporanea e ripristino delle munizioni esatte. |
| 7 — PBR | Conservati i materiali glTFast compatibili HDRP, compresi shader graph con più subshader; conversione FBX con normal, metallic, AO, roughness/smoothness, specular ed emissione. Ripristinati i riferimenti sorgente nei prefab e nei tre operatori. | Verifica dei canali della mask HDRP, identità dei materiali compatibili e confronto dei binding originali glTF. |
| 8 — Schermo | Servizio display condiviso; il menu legge le preferenze senza sovrascriverle. Risoluzione, modalità finestra e qualità usano le stesse chiavi nel menu e nella partita. | Inizializzazione del menu con finestra e risoluzione diverse dai default; preferenze originali ripristinate dopo il test. |
| 9 — Plasma/copertura | Origine dalla volata plasma, controllo del segmento iniziale, collisione continua anche nei primi centimetri e verifica della sovrapposizione iniziale. Durata massima esplicita. | Parete tra camera e origine del colpo e muro sottile entro gli 80 cm iniziali, senza danno al bersaglio retrostante. |

Codice principale: `ElderboomCombat.cs`, `ElderboomWeaponFire.cs`, `ElderboomGame.cs`, `ElderboomArsenal.cs`, `ElderboomSpecialPickup.cs`, `ElderboomDisplaySettings.cs`, `ElderboomMaterialRepair.cs` e controller/HUD collegati.

La regressione aggiornata esegue **88 controlli con zero errori**, registrati in `Logs/review/regression.txt`; i controlli HK53 e sangue rimangono in `Logs/integration/verification.txt`. Gli asset rigenerati e le scene sono quelli del progetto esistente: nessuna copia del progetto Blender o delle sue sorgenti.

## Diagnosi originale dei nove problemi, ora corretti


### 1. P1 — I sopravvissuti accecati possono bloccare le ondate successive

**Unity:** [ElderboomGame.cs:189](/Users/alessandrocastellani/zombie-test/UnityHDRPSlice/Assets/Scripts/ElderboomGame.cs:189) attende finché `zombies.Count >= maxActive`. Alla fine di un'ondata completamente accecata, i sopravvissuti vengono esclusi dall'obiettivo tramite `CountsForWave=false` (righe 198–206), ma restano vivi nella stessa lista e continuano a occupare il limite degli spawn. Il limite è solo 10 alla prima ondata, 12 alla seconda. Se si accumulano abbastanza sopravvissuti, lo spawn aspetta senza scadenza.

**Godot:** `facility_map.gd:_select_encounter_spawn` distingue pressione dell'ondata e numero totale nel mondo; `_retire_distant_blind_survivor` rimuove i sopravvissuti ormai lontani e fuori dalla vista, dopo un tempo minimo. `encounter_rules.gd` definisce anche il limite mondiale separato di 32.

**Correzione consigliata:** contare separatamente i vivi che fanno parte dell'ondata, mantenere un limite mondiale e ritirare gli accecati distanti senza assegnare una seconda ricompensa. Testare più ondate consecutive vinte con il flare, oltre alle ondate vinte sparando. Non basta testare che la prima ondata avanzi.

### 2. P1 — Spari dalla camera: manca il controllo dell'ostruzione della canna

**Unity:** [HDRPSlicePlayer.cs:332](/Users/alessandrocastellani/zombie-test/UnityHDRPSlice/Assets/Scripts/HDRPSlicePlayer.cs:332) esegue il raycast direttamente dalla camera. `rifleMuzzle` viene individuato ma non partecipa al calcolo dell'impatto. Una canna dietro uno spigolo può quindi colpire un bersaglio che vede la camera, anche se il percorso dalla canna è ostruito.

**Godot:** `player.gd:_cast_weapon_rays` controlla il segmento camera–canna, trova il punto mirato dalla camera e lancia poi il colpo dalla canna verso quel punto.

**Correzione consigliata:** portare questo doppio controllo in una funzione di tiro condivisa, con test su copertura vicina, angoli stretti e bersagli a contatto. Conservare la precisione del mirino, senza usare solo un raycast parallelo dalla canna.

### 3. P1 — Gli effetti ritardati delle abilità continuano dopo la morte

**Unity:** in [ElderboomGame.cs:57](/Users/alessandrocastellani/zombie-test/UnityHDRPSlice/Assets/Scripts/ElderboomGame.cs:57) la morte cambia lo stato e il cursore, ma non annulla le coroutine. `PlasmaVolley` (271–281), `PlasmaFlare` (284–309) e `AcousticFreeze` (321–326) non verificano nuovamente che il giocatore sia vivo dopo le attese. Si possono quindi emettere colpi, applicare accecamento o mantenere il congelamento dopo il game over.

**Godot:** `player.gd:_apply_damage` annulla le abilità; `soph_plasma_ability.gd:fire_shot` ricontrolla la vita del giocatore.

**Correzione consigliata:** un contesto di sessione/abilità con generazione o token di annullamento; ripulire luci, overlay, suoni persistenti e pose nella stessa cancellazione. Applicarlo anche a un futuro riavvio della partita. Test: morire durante ogni fase, non solo prima dell'attivazione.

### 4. P2 — I profili delle armi speciali non corrispondono al comportamento

**Unity:** `ElderboomWeaponDefinition.mode` è deserializzato, ma `HDRPSlicePlayer.Fire` non lo legge. Il lanciagranate nel catalogo ha `mode=explosive`, eppure usa lo stesso raycast dei fucili: non genera una granata né un'esplosione ad area. Il lanciafiamme usa raggi istantanei senza il relativo effetto continuo. Il campo `pierce` non viene usato.

**Godot:** `player.gd:_fire_weapon_round` separa esplosivi, impulsi, hitscan ed effetti speciali; `_cast_weapon_rays` implementa la penetrazione. `facility_map.gd:spawn_zombie_drop` e `special_weapon_pickup.gd` generano e assegnano i drop temporanei; in Unity manca questa catena, anche se i prefab delle armi sono presenti.

**Correzione consigliata:** implementare strategie di fuoco esplicite e un test per ciascuna; non dichiarare completa la parità basandosi sul numero dei prefab. Prima progettare la gestione dei drop e della scadenza, poi integrarli nell'inventario.

### 5. P2 — HUD: informazioni calcolate ma mai mostrate durante la partita

**Unity:** `ElderboomGame.DrawPortHud` contiene numero dell'ondata, nemici rimasti, nome abilità e messaggi, ma non viene chiamato da `OnGUI`. `ElderboomHud.OnGUI` mostra icone e munizioni senza questi dati testuali. Inoltre il vecchio hitmarker di `HDRPSlicePlayer.OnGUI:678` viene saltato quando esiste `ElderboomGame`, e il nuovo HUD non lo riproduce. Il giocatore riceve quindi meno feedback proprio nella scena principale.

**Godot:** `player.gd:update_hud`, il sistema dei messaggi e gli annunci delle ondate hanno percorsi visibili dedicati.

**Correzione consigliata:** esporre lo stato tramite proprietà/eventi e affidare tutto a un solo HUD. Ripristinare ondata, rimanenti, feedback impatto e messaggi di acquisto/abilità, evitando di riattivare due HUD sovrapposti. Il punteggio visualizzato è aggiornato dagli spari del player, mentre i crediti sono aggiornati dalle uccisioni nel director: verificare e decidere la regola per le uccisioni da abilità.

### 6. P2 — L'inventario e l'economia non seguono ancora le regole originali

**Unity:** `ElderboomArsenal.Add` può aggiungere tutte le armi distinte acquistate; non esiste una politica di sostituzione, il limite di slot o l'acquisto del terzo slot. Il blocco dei duplicati aggiunto in questa revisione impedisce di accumulare infinite istanze della stessa arma, ma non implementa l'economia completa.

**Godot:** `player.gd:equip_weapon` inserisce fino a `max_weapon_slots`, poi sostituisce lo slot attivo; `unlock_third_weapon_slot` sblocca il terzo. Esistono inoltre coltello permanente e arma speciale temporanea fuori dagli slot normali.

**Decisione per l'HK53:** Soph riceve esplicitamente l'HK53 come terza arma aggiuntiva, come richiesto, conservando AKMN e Rhino. Questa è una scelta per il nuovo loadout, non la dichiarazione di parità dell'intera economia.

### 7. P2 — La conversione generica dei materiali perde informazioni PBR

**Unity:** `Assets/Editor/ElderboomArsenalBuilder.cs:63–77` converte ogni shader non riconosciuto dal prefisso `HDRP` copiando la sola texture di colore, un eventuale colore `_Color` e una smoothness fissa `.25`. Normal, metallic, occlusione e roughness originali non vengono trasferiti. Anche uno shader graph compatibile può essere convertito inutilmente.

**Conseguenza:** acciaio e polimero diventano troppo simili o piatti, indipendentemente dalla qualità delle texture sorgenti. Un'ulteriore rigenerazione con lo stesso builder non risolve il problema.

**Correzione consigliata:** conservare i materiali HDRP prodotti da glTFast; per gli altri importatori creare conversioni esplicite dei canali PBR. Il builder dedicato dell'HK53 conserva già le mappe originali e viene usato anche quando si ricostruisce l'arsenale completo. Gli altri prefab preesistenti richiedono una revisione separata.

### 8. P2 — L'avvio del menu annulla la preferenza schermo

**Unity:** `Assets/Scripts/ElderboomMenu.cs:53–58` scrive sempre `fullscreen=1` e imposta la risoluzione del display in ogni avvio standalone. Una preferenza salvata dall'utente per modalità finestra o risoluzione diversa viene persa al successivo passaggio dal menu. Il controller del giocatore implementa invece impostazioni persistenti.

**Correzione consigliata:** un unico servizio impostazioni, con i valori predefiniti applicati solo quando la chiave non esiste. Provare chiusura e riapertura della build con impostazioni diverse da quelle di default.

### 9. P2 — Il plasma può attraversare la copertura immediatamente vicina

**Unity:** `ElderboomPlasmaBolt.cs:53` ignora gli impatti con l'ambiente fino a `traveled > .8f`. Un muro sottile incontrato nei primi 80 cm può essere oltrepassato. Il proiettile nasce inoltre 45 cm davanti alla camera, senza controllo di quel segmento.

**Godot:** il colpo plasma usa il muzzle dell'arma, una direzione verso il punto mirato e una durata esplicita nel proiettile dedicato.

**Correzione consigliata:** escludere soltanto il proprietario e i collider del viewmodel, non ignorare genericamente la copertura iniziale. Gestire anche l'ostruzione tra camera e origine del proiettile.

## Difetti corretti durante questo intervento

- Sangue: il punto colpito e la normale raggiungono ora lo zombie; lo spruzzo nasce sulla superficie, anziché ai piedi. Aggiunto effetto particellare HDRP con alpha di vita, orientato verso l'esterno. Il precedente prefab era uno splat da pavimento e veniva adattato con blending additivo.
- Bilanciamento: il tiro legge `spread` e `minimumDamage` dal profilo, riduce lo spread in ADS e campiona un disco anziché un quadrato. Prima fucili e pistole con profili diversi potevano avere lo stesso spread e lo stesso danno minimo. Restano da portare bloom, penetrazione e strategie speciali.
- Ricarica/cambio arma/acquisti: cambio e acquisti bloccati durante ricarica e animazione abilità. La ricarica non trasferisce più munizioni se il proprietario muore o cambia il modello attivo.
- Riferimenti del viewmodel: al cambio si riassociano animazione e muzzle flash alla nuova arma; l'animazione non torna sul vecchio AKMN nascosto.
- ADS: viene disattivato durante ricarica e abilità.
- Inventario: inizializzazione pulisce le sole istanze aggiuntive possedute dall'arsenale; duplicati rifiutati. Un acquisto senza prefab valido non sottrae più crediti.
- Soph con arma aggiuntiva: l'abilità usa temporaneamente il modello originale con la pistola al plasma, poi ripristina l'arma selezionata e le sue munizioni.
- Zombie morto: collider figli disabilitati e melee annullato, oltre ai collider sulla radice.
- Metadati Unity: risolto un conflitto Git rimasto letteralmente dentro `Assets/TerrainDemoScene_HDRP/Scripts.meta`, mantenendo il GUID che il parser Unity aveva già letto. La ricerca nelle sorgenti, negli shader e nei metadati del progetto non ha trovato altri marcatori di conflitto.
- Validazione risorse: lo spawn e lo smoke test facevano riferimento a `ZombieMaleAAB`, prefab non presente nel progetto. Allineati ai due prefab effettivamente disponibili, senza inventare una terza variante.
- HK53: import dedicato con idle di 4 secondi, reload di 3,5 secondi, parti mobili animate e materiali PBR conservati. Il timer della nuova ricarica segue la clip, senza accorciarla a 2,5 secondi.

## Cosa conviene conservare e come migliorare la struttura

- La stamina segue gli stessi valori di Godot: 100, consumo 20/s, recupero 18/s, attesa .85 s e blocco dopo esaurimento. Non serve riscriverla per uniformare lo stile.
- `RegisterKill` protegge già dai doppioni con `Registered`: la chiamata sia dallo zombie sia dal controller non assegna due volte i crediti. Centralizzarla resta più chiaro, ma non è il bug del sangue.
- Il recoil usa sotto-passi per la stabilità; la navigazione usa il NavMesh e verifica la raggiungibilità degli spawn. Mantenere questi sistemi e aggiungere test sui casi limite.
- Separare progressivamente `HDRPSlicePlayer` in input/movimento, arma/ricarica e impostazioni; separare `ElderboomGame` in director delle ondate e abilità. Non farlo tutto insieme durante una correzione visiva.
- Sostituire i confronti per nome (`"SOPH"`, `"HK53"`, categorie stringa) con identificatori e dati di configurazione per animazioni, suoni e modalità di fuoco. Gli attuali duplicati snake_case/camelCase nel JSON rendono la manutenzione più fragile.
- `ElderboomHud.OnGUI` cerca tutti gli zombie e crea stili a ogni evento GUI; `ElderboomZombie.LateUpdate` cerca l'osso Hips se il rig non è humanoid. Memorizzare gli oggetti e misurare con Profiler prima di attribuire loro un calo di FPS.
- La copertura automatica iniziale era troppo limitata: lo smoke verifica pochi spawn, una morte e un heal, non la parità. Aggiungere regressioni mirate per copertura/canna, slot/munizioni, cancellazione abilità, avanzamento ondate con ciechi e contratti HUD. Evitare test che si limitano a rileggere gli stessi valori impostati dal codice.

## Verifica di questo intervento

I risultati eseguiti sono registrati in `Logs/integration/verification.txt`, con immagini in `Logs/integration/`. Il controllo comprende anche uno sparo attraverso `HDRPSlicePlayer.Fire`: impatto fisico, danno del profilo HK53 ed emissione sulla superficie colpita. Le articolazioni della mano sono confrontate con nove pose campione Blender; la tolleranza di 1 mm si riferisce alla posizione dell’articolazione, non a tutti i vertici della pelle. Unity usa lo skinning lineare del proprio renderer. La ricevuta dell'esportazione è `Logs/hk53_export.json`: riporta la sorgente Blender invariata, riduzione della sola copia Unity e intervalli delle clip. Lo smoke salvato della scena è `Logs/elderboom_saved_validation.json`.

Queste correzioni chiudono i nove punti della revisione. Multiplayer, altre mappe Godot, rifinitura artistica completa degli effetti e prestazioni su altri computer restano ambiti separati; non sono certificati da questa regressione. I drop temporanei e i comportamenti di fuoco elencati nella tabella sono ora implementati.
