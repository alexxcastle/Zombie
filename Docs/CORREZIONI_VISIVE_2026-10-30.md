# Correzioni visive — 30 ottobre 2026

AKMN e HK53 mantengono inquadratura, orientamento e asset originali di questa versione Unity. Nessuna modifica al progetto Blender.

- Gli FBX del negozio non ricevono più la rotazione di 180° della convenzione Godot. Posizione di impugnatura e punto di uscita sono ricalcolati sulla geometria dell’arma.
- I riferimenti dei mirini GLB usano la conversione dell’asse X di glTFast. Rimossi i riferimenti duplicati; SIG, Striker, Rhino e 1911 hanno una sola coppia autorevole. Ricalcolate le pose ADS e i punti di uscita.
- Disabilitata la geometria estranea `output_unwrapped.002` dello Striker.
- Il campionamento delle animazioni non legacy crea un Animator sulla radice esatta del modello, necessario nel Player compilato. Ricariche verificate anche fuori dall’editor.
- Le clip FBX dimostrative senza un idle dedicato mantengono il primo fotogramma, come nella versione Godot. Le clip idle dedicate continuano a scorrere; lo Striker mantiene il proprio fotogramma iniziale previsto.
- Il cursore luminosità modifica l’esposizione nella direzione corretta; 1.5 corrisponde a 1.5 volte la luminanza. La luce principale è meno satura di blu; resta il profilo notturno a EV 7.2 di base.
- Gli undici lampioni esistenti sono distribuiti lungo il villaggio e gli accessi, appoggiati al terreno/navigazione e normalizzati a 4.2 m di altezza. Luce a 6500 lumen, raggio 16 m.
- Sangue: spruzzo più denso e leggibile, durata/dimensione aumentate e piccolo nucleo di nebulizzazione. Emissione sul punto colpito, profondità e dissolvenza preservate.

Verifica finale:

- Revisione del gameplay: 88 controlli passati, zero errori (`Logs/review/regression.txt`).
- Player macOS/Metal: 36 controlli passati, zero errori (`Logs/visual_fix/player/verification.txt`). Include orientamento, posizione delle braccia, ricariche, stabilità dell’idle a 10 s e incremento misurato dei pixel rossi del sangue a 3 m.
- Ispezionate le immagini dei 18 viewmodel, ADS di SIG/Striker/Rhino/1911/HK53, scena, distribuzione dei lampioni e sangue. Le catture disabilitano temporaneamente motion blur/DOF/TAA solo nel percorso di validazione, per leggere le pose ferme.
- Build standalone riuscita, zero errori di compilazione. Ricevuta: `Logs/standalone_build_result.json`.

Il controllo del Player è attivato solo con `-elderboom-visual-validation`; il lancio normale apre il menu del gioco. Restano le limitazioni di animazione già documentate per gli asset senza ricarica dedicata.
