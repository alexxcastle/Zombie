# Backup e versioni

Il codice, le scene, i prefab, i materiali e tutti i `.meta` sono in git (repo `alexxcastle/Zombie`, ramo `main`).
I file d'arte pesanti (modelli `.glb/.gltf/.fbx`, texture, audio, video) **non** sono in git: vedi `.gitignore`.
Vanno copiati a parte (disco esterno o cloud) insieme alla cartella `Assets/`.

## Ogni giorno di lavoro
```
cd ~/zombie-test/UnityHDRPSlice
git add -A
git commit -m "cosa ho cambiato"
git push
```

## Tornare indietro
- vedere la storia: `git log --oneline`
- buttare le modifiche non salvate di un file: `git checkout -- percorso/del/file`
- tornare a un commit per prova: `git switch --detach <id>` (poi `git switch main`)

## Copia completa (arte inclusa)
Copiare `Assets/`, `Packages/`, `ProjectSettings/` (non `Library/`, `Builds/`, `Logs/`, `Temp/`: si rigenerano).

## Build
`echo windows > Logs/claude_build_now.txt` (oppure `mac` / `all`) con l'editor aperto.
La build si rifiuta di partire con meno di 8 GB liberi e alla fine controlla i file (campo `integrity`
in `Logs/claude_build_result*.json`): `CORRUPT` = disco pieno durante la build, da rifare.
