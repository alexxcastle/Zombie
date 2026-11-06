# Reazioni ai colpi, sangue e hitmarker — 30 ottobre 2026

I colpi non letali avviano lo stato `ShotHit` già presente nei controller di entrambi gli zombie. La riproduzione usa velocità 1.3, come `zombie.gd` in Godot. Durante la reazione la navigazione si ferma; un pugno in preparazione viene annullato, compreso il danno ritardato. Alla fine della clip lo zombie riprende la locomozione. Colpi ripetuti durante la stessa reazione non la riportano continuamente al primo fotogramma. Morte, congelamento, sollevamento e shockwave conservano la precedenza.

Il sangue non letale passa da 18 a 48 particelle di spruzzo e da 5 a 12 di nebulizzazione, con maggiore dimensione, velocità e durata. La limitazione fra emissioni passa da 180 a 65 ms, evitando di scartare i colpi delle raffiche più comuni. L’effetto alla morte usa 72 particelle di spruzzo e 18 di nebulizzazione. Restano i controlli di profondità e l’emissione sul punto d’impatto.

Gli hitmarker sostituiscono la croce bianca con i quattro frammenti rossi di `crosshair_hud.gd`: durata 0.55 s, separazione minima di 0.12 s tra conferme, gravità 680 pixel/s², deriva laterale, rotazione e dissolvenza. Le trasformazioni rispettano la scala dell’HUD e gli schermi HiDPI. Il cambio di sessione o la morte svuotano anche le conferme in attesa.

Verifica nel Player macOS/Metal: `Logs/hit_feedback/final/verification.txt`, 42 controlli passati senza errori. Include colpi HK53 reali contro entrambi i prefab, movimento delle ossa, priorità delle abilità, interruzione del pugno, ritorno alla camminata, quantità del sangue e catture native degli hitmarker con verifica della posizione dei pixel rossi. Le immagini sono nella stessa cartella. Il percorso di test è attivo solo con `-elderboom-hit-validation`.

Le viste ortografiche esportate da Unity sono in `Exports/`, risoluzione 4096 × 4096:

- `Elderboom_mappa_completa_ortografica_4096.png`: tutto il terreno 700 × 700 m, con un piccolo margine d’inquadratura.
- `Elderboom_villaggio_ortografica_4096.png`: dettaglio di 190 × 190 m contenente il villaggio, i cortili e gli accessi.

La direzione verso l’alto delle immagini corrisponde a +Z in Unity. L’illuminazione diurna e l’assenza di nebbia servono solo alla cattura; non sono salvate nella scena di gioco. `Exports/map_export.txt` riporta i limiti e i parametri. Le versioni JPEG con suffisso `_anteprima` permettono un’apertura rapida.
