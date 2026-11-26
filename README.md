# c0n50le

La home a schermo intero per avviare dal divano i giochi e le app del PC, con il controller o con mouse e tastiera.
WinUI 3 su .NET 8, per Windows 10 e 11 (x64).

Unisce la home in stile console di [Console Mode](https://github.com/1r0n51uv/consolemode) e la libreria con le card
di [RON1N Mode](https://github.com/1r0n51uv/ron1n-mode). È un'app nuova: non cambia schermi, audio o HDR.

## Cosa fa

- **Libreria propria**: i giochi di Steam ed Epic, una cartella giochi (una sottocartella per gioco) e gli eseguibili
  aggiunti a mano. Gli ultimi giocati sono i primi.
- **Copertine e descrizioni** dal LaunchBox Games Database, abbinate da sole quando il titolo è certo, oppure con
  "Cerca un altro titolo" e anteprima di copertina e sfondo.
- **Avvio e ripresa**: una card avvia il gioco, la stessa card lo riprende senza aprirne una seconda copia.
  Ctrl+Alt+F11 o Visualizza+Y sul controller riportano alla home mentre giochi; alla chiusura del gioco la home torna da sola.
- **Input automatico**: basta toccare il controller per passare a focus, suggerimenti dei pulsanti e puntatore
  nascosto; muovere il mouse o premere un tasto torna a mouse e tastiera. Xbox, PlayStation e Switch Pro.
- **Sistema**: libreria, schermo e scala, comandi e prova del controller, temi e suoni, avvio con Windows,
  aggiornamenti, diagnostica, lingua (italiano e inglese).
- **Cinque temi**: Notte, Vetro, Colore del gioco, Alba, Arcade.
- Intro, suoni, musica nella home, tour guidato, icona nell'area di notifica, aggiornamenti da GitHub con verifica SHA-256.

## Download

Dalla pagina delle release: `C0n50le-Setup-x64.exe` (installazione per utente, senza amministratore, si aggiorna da sola)
oppure `C0n50le-Portable-x64.exe` (un solo exe che salva i dati in `c0n50le_Data` accanto a sé).

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
