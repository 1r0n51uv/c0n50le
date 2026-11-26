# c0n50le

Le descrizioni seguono la lingua dell'interfaccia. La ricerca per titolo su Wikipedia copre anche giochi
PlayStation, Xbox, Nintendo e titoli provenienti da altri store o aggiunti manualmente, senza richiedere un ID Steam.
Il testo viene salvato per titolo e lingua e accompagnato dal collegamento alla fonte (Wikipedia, CC BY-SA).
In italiano, se manca una voce riconosciuta, la descrizione resta vuota: non viene sostituita da quella inglese di
LaunchBox. In inglese è disponibile anche Steam come fonte aggiuntiva. La copertura dipende dalle voci disponibili;
questa funzione non aggiunge nuovi launcher alla scansione automatica della libreria.

La home a schermo intero, nello stile di Apple TV, per avviare dal divano i giochi e le app del PC con il controller o con mouse e tastiera.
WinUI 3 su .NET 8, per Windows 10 e 11 (x64).

Unisce la home in stile console di [Console Mode](https://github.com/1r0n51uv/consolemode) e la libreria con le card
di [RON1N Mode](https://github.com/1r0n51uv/ron1n-mode). È un'app nuova: non cambia schermi, audio o HDR.

## Cosa fa

- **Libreria propria**: i giochi di Steam ed Epic, una cartella giochi (una sottocartella per gioco) e gli eseguibili
  aggiunti a mano. Gli ultimi giocati sono i primi.
- **Copertine, loghi e descrizioni** dal LaunchBox Games Database, abbinate da sole quando il titolo è certo, oppure con
  "Cerca un altro titolo" e anteprima di copertina e sfondo.
- **Avvio e ripresa**: una card avvia il gioco, la stessa card lo riprende senza aprirne una seconda copia.
  Ctrl+Alt+F11 o Visualizza+Y sul controller riportano alla home mentre giochi; alla chiusura del gioco la home torna da sola.
- **Input automatico**: basta toccare il controller per passare a focus, suggerimenti dei pulsanti e puntatore
  nascosto; muovere il mouse o premere un tasto torna a mouse e tastiera. Xbox, PlayStation e Switch Pro.
- **Sistema**: libreria, schermo e scala, comandi e prova del controller, temi e suoni, avvio con Windows,
  aggiornamenti, diagnostica, lingua (italiano e inglese).
- **Stile tvOS**: top shelf con l'artwork e il logo del gioco a fuoco, dock di vetro in basso, menu contestuali, avvisi, impostazioni a sidebar. Tema **Chiaro** o **Scuro**, **Liquid Glass** spento o a tre profondità.
- Intro, suoni, musica nella home, tour guidato, icona nell'area di notifica, aggiornamenti da GitHub con verifica SHA-256.

## Download

Dalla pagina delle release: `C0n50le-Setup-x64.exe` (installazione per utente, senza amministratore, si aggiorna da sola)
oppure `C0n50le-Portable-x64.zip` (da estrarre in una cartella: i dati restano in `c0n50le_Data` accanto all'exe).

## Sviluppo

Serve il .NET SDK 8 su Windows; per l'installer anche Inno Setup 6.

```powershell
dotnet test tests/C0n50le.Tests
dotnet build src/C0n50le/C0n50le.csproj -c Debug -r win-x64 -p:Platform=x64
./build/Publish-C0n50le.ps1 -Version 0.1.0
```

- `src/C0n50le.Core`: tutta la logica (libreria, catalogo, preferenze, input, temi, testi, aggiornamenti), senza WinUI.
- `src/C0n50le`: l'app WinUI 3.
- `tests/C0n50le.Tests`: test xUnit sul Core.
- `tools/TestGames`: giochi finti per provare l'app; `Make-TestLibrary.ps1` crea una cartella con quattro giochi.
- [docs/PLAN.md](docs/PLAN.md): il piano e le decisioni; [docs/TESTING.md](docs/TESTING.md): le prove a mano.

Una release parte da un tag `v*` (workflow **Release**).

[GNU AGPL-3.0](LICENSE) · [Avvisi di terze parti](THIRD_PARTY_NOTICES.md)
