# Prove a mano

I test automatici (`dotnet test tests/C0n50le.Tests`) coprono la logica. Queste prove servono per quello che dipende da
Windows, dal controller e dallo schermo. Per avere una libreria di prova: `tools/TestGames/Make-TestLibrary.ps1`, poi
Sistema → Libreria → Cartella giochi → `tools/TestGames/library`.

## Esito della 0.1.0 (26/11/2026, Windows 11, schermo 4096×2160 al 300%, controller Xbox via Bluetooth)

| Prova | Esito |
|---|---|
| Avvio a schermo intero, intro, tour alla prima apertura | ok |
| Scansione della cartella con i 4 giochi di prova | ok |
| Copertine automatiche (Control Resonant, Pokémon Pokopia) e "Cerca un altro titolo" (EA Sports FC 27, Grand Theft Auto VI) | ok |
| Navigazione con frecce, Invio, Esc, tasto Applicazioni, F1 | ok |
| Avvio di un gioco, ritorno con Ctrl+Alt+F11, "Riprendi" senza seconda istanza, ritorno automatico alla chiusura | ok |
| Il gioco appena chiuso passa in testa alla fila | ok |
| Popup delle opzioni sulla card, Sistema e tutte le categorie, selettori, dialoghi | ok |
| I cinque temi su home e Sistema | ok |
| Controller rilevato in Sistema → Comandi ("Xbox") | ok |
| Pacchetto: setup e portatile | vedi la release |

## Da provare con il controller in mano

- [ ] Toccare il pad: la pill passa a "Xbox", compaiono i suggerimenti in basso, sparisce il puntatore, le card hanno il bordo.
- [ ] Muovere il mouse: tornano il puntatore e il "⋯" sulla card selezionata, spariscono i suggerimenti.
- [ ] Croce e stick: spostamento, ripetizione tenendo premuto. A avvia, Y apre le opzioni, B chiude/torna al desktop,
      ☰ apre Sistema, LB/RB cambiano categoria.
- [ ] Durante un gioco, Visualizza+Y riporta alla home; A sulla card riprende il gioco.
- [ ] Sistema → Comandi → Prova dei pulsanti mostra i pulsanti premuti. Ripetere con un DualSense e uno Switch Pro.

## Altre prove

- [ ] Steam ed Epic installati: i giochi compaiono con il nome del negozio e partono dal launcher.
- [ ] Due schermi: Sistema → Schermo → Schermo della home sposta la finestra.
- [ ] Scala 80% e 150% a 1080p e 4K: niente testo tagliato.
- [ ] Avvio con Windows acceso/spento; "All'accesso apri la home" spento parte nell'area di notifica.
- [ ] Chiudere la finestra con "Chiudendo resta nell'area di notifica" acceso e spento.
- [ ] Lingua inglese e ritorno a "Come Windows".
- [ ] Aggiornamento: installare 0.1.0, pubblicare 0.1.1, "Controlla adesso".
