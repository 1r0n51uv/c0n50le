# c0n50le: piano di riscrittura

Stato: bozza da approvare · 26/11/2026

c0n50le è una **app nuova e separata**: una home a schermo intero, in stile console, per avviare dal divano i
giochi e le app della propria libreria. Unisce il **layout e il contorno di consolemode** alla **libreria e alla
gestione delle card di ron1n-mode**. È scritta da zero; da entrambe le repo si prendono idee e codice.

## 1. Decisioni prese

| Tema | Decisione |
|---|---|
| Base | Riscrittura da zero. Il codice di consolemode e ron1n-mode si copia e si adatta file per file, dove serve. |
| Upstream | `lippdev/consolemode` e `1r0n51uv/consolemode` sono fonte di idee e codice. Nessun legame git. Upstream è fermo alla 1.7.1 (la base del fork): oggi non c'è nulla di nuovo da valutare. |
| Identità | Nome "c0n50le", `C0n50le.exe`, dati in `%LOCALAPPDATA%\c0n50le`, AppId Inno Setup nuovo, release e OTA da `1r0n51uv/c0n50le`, prima versione **0.1.0**. Nessuna migrazione da consolemode o ron1n-mode. |
| Licenza | AGPL-3.0 (come le due fonti), con `THIRD_PARTY_NOTICES.md` che cita provenienza di codice, font Inter (OFL) e dati LaunchBox. |
| Motore di sessione | **Escluso.** c0n50le non cambia schermi, audio, HDR/VRR, risoluzione né icone del desktop. Si apre sullo schermo scelto e basta. |
| Libreria | Propria, come ron1n-mode: exe aggiunti a mano, cartella giochi, Steam ed Epic installati, copertine e sfondi da LaunchBox. Niente Playnite. |
| In gioco | Solo la scorciatoia **torna alla home** (tasto da tastiera + combinazione del controller, configurabili) e la ripresa della finestra del gioco senza seconda istanza. Niente menu sovrapposto, niente ControlFS/JoyChromium. |
| Input | **Cambio automatico di modalità**: basta toccare il controller e l'interfaccia passa alla modalità controller (focus evidenziato, glifi dei pulsanti, cursore nascosto); muovere il mouse o premere un tasto la riporta a mouse/tastiera. Nessun puntatore mosso con lo stick. |
| Contorno | Tray e avvio con Windows, aggiornamenti OTA con canale beta, intro, suoni di navigazione, musica nella home, tour guidato. |
| Lingue | Italiano ed inglese, con test che confrontano chiavi e segnaposto dei due cataloghi. |
| Layout home | Orientato a consolemode: nessuna scheda in alto, una sola Home. Barra in alto con marchio a sinistra e pill a destra; eroe con titolo e descrizione; fila orizzontale di card; sfondo del gioco selezionato che sfuma. Gestione e disposizione delle card come in ron1n-mode. |
| Sistema | Raggiungibile da una **pill** in alto a destra (sempre presente) e da una **card** in fondo alla fila (nascondibile). Si apre in un pannello a schermo intero, categorie a sinistra. |
| Temi | Temi reinventati, con il vetro liquido come uno dei temi (vedi §5). Pulsanti ed etichette allineati su una griglia comune. |
| Verifica | Build e avvio su questo PC con il controller collegato, con quattro eseguibili di prova: *Pokémon Pokopia*, *Control Resonant*, *FIFA 27*, *GTA 6*. |

## 2. Cosa c'è a monte e cosa resta fuori

### Da consolemode (fork di upstream 1.7.1)

| Area | Esito in c0n50le |
|---|---|
| Home in stile PS5 (barra, eroe, fila, sfondo a dissolvenza, musica) | **Presa e riscritta**, senza le schede Home/Sessione/Sistema |
| Vetro liquido (`App.xaml`, `CardMaterial`) | **Presa** come tema "Vetro" |
| Suoni (`UiSounds`, `UiSoundSynth`, wav), intro (`BootIntro`), musica (`HomeMusic`, `AmbientMusic`) | **Presi** |
| Tour guidato (`ConsoleTour`) | **Preso**, con tappe nuove |
| Localizzazione (`LocalizationService`, test dei cataloghi) | **Presa**, solo it-IT ed en-US |
| Tray (`TrayService`), avvio con Windows (`StartupService`) | **Presi** |
| Aggiornamenti (`UpdateService`, `UpdateChannel`, `UpdateIntegrity`) | **Presi**, puntati a `1r0n51uv/c0n50le` |
| Navigazione col controller (`GamepadNavigator`), cursore (`CursorHider`), scala UI (`UiScale`) | **Presi** come base del cambio automatico di input |
| Diagnostica (`DiagnosticsReport`) | **Presa**, ridotta |
| Motore di sessione: `ConsoleEngine`, `MonitorService`, `DisplayBackend`, audio, HDR/VRR, risoluzione, `DesktopIconLayout`, `BlackCurtain` | **Fuori** |
| Menu della sessione, switcher finestre, Task View, tour del menu | **Fuori** |
| Scorciatoie "apri dalla tray", "menu", "torna al PC"; pressione breve del tasto Xbox; risveglio del controller | **Fuori** (resta solo "torna alla home") |
| Mouse col controller (`ControllerMouse`, `PointerParking`) | **Fuori** |
| ControlFS, JoyChromium, Playnite, app personalizzata, Game Bar | **Fuori** |
| Batteria dei controller | Fuori dalla 0.1.0, candidata per dopo |
| pt-BR | **Fuori** |

### Già tolto da consolemode rispetto a upstream (resta fuori)

TV (Google/Android TV, Wake-on-LAN, HDMI), RTSS e FPS, profili per gioco, API locale e link `consolemode://`,
spegnimento DDC/CI, Steam Big Picture e modalità Xbox, lingue es/de/fr/ru.

### Da ron1n-mode

| Area | Esito in c0n50le |
|---|---|
| Scoperta giochi (`GameDiscovery`: cartella, Steam, Epic) | **Presa** |
| Catalogo LaunchBox (`GameArtwork`, `LaunchBoxArtwork`), anteprima prima dell'abbinamento | **Presi** |
| Avvio, ripresa e controllo del processo (`GameSession`) | **Preso** |
| Gestione delle card: aggiungi exe, modifica, cerca un altro titolo, rimuovi; ultimo gioco preselezionato; anteprima al passaggio | **Presa** |
| Colore dal artwork (`ArtworkPalette`) | **Preso** per il tema "Colore del gioco" |
| Scorciatoia globale (`WindowsKeys`) e lettura del controller (`Controller`) | **Prese** per "torna alla home" |
| Preferenze (`Preferences`) | **Riscritte** (formato nuovo, nessuna lettura dei vecchi file) |
| Impostazioni a rail sopra la home | **Sostituite** dal pannello Sistema |
| Materiali Scuro/Chiaro/Trasparente/Colore | **Sostituiti** dai temi nuovi |

## 3. Architettura

```
c0n50le/
  C0n50le.sln
  global.json                 .NET SDK 8.0.4xx
  src/C0n50le.Core/           net8.0, solo logica: niente WinUI, niente P/Invoke
    Library/                  Game, Artwork, LibraryStore, GameDiscovery (cartella, Steam, Epic)
    Catalog/                  LaunchBox: ricerca, abbinamento, scelta delle immagini
    Settings/                 Preferences, AppPaths, salvataggio atomico JSON
    Input/                    InputMode (Controller | MouseKeyboard), regole di cambio, glifi
    Theming/                  ThemeId, token dei temi, contrasto, colore dal artwork
    Localization/             cataloghi it-IT / en-US
    Updates/                  confronto versioni, canale beta, verifica dell'hash
  src/C0n50le/                net8.0-windows10.0.19041.0, WinUI 3 non pacchettizzato, x64
    App, MainWindow           una sola finestra a schermo intero sullo schermo scelto
    Views/                    HomeView, SystemView, dialoghi delle card, tour, intro
    Controls/                 GameCard, Pill, SettingRow, scale e animazioni
    Services/                 GameLauncher, ReturnShortcut, InputWatcher, Tray, Startup, Sounds, Music
    Themes/                   Tokens.xaml + un ResourceDictionary per tema
    Strings/                  Strings.it-IT.json, Strings.en-US.json
  tests/C0n50le.Tests/        xUnit su net8.0, referenzia solo C0n50le.Core
  tools/TestGames/            exe di prova (finestra che resta aperta) per le quattro voci di test
  build/                      Publish-C0n50le.ps1, C0n50le.iss (Inno Setup)
  .github/workflows/          ci.yml (build + test), release.yml (tag v*)
```

Scelte:

- **MVVM leggero con CommunityToolkit.Mvvm** (come consolemode), ma un ViewModel per vista invece di un unico
  `MainViewModel` diviso in 25 file.
- **Core separato** invece dei file collegati di consolemode: i test referenziano il progetto e coprono tutta
  la logica senza Windows.
- Dipendenze: `Microsoft.WindowsAppSDK 1.7`, `CommunityToolkit.Mvvm`, `H.NotifyIcon.WinUI` (tray). Niente
  SettingsControls: la riga di impostazione è un controllo nostro, per controllare gli allineamenti.

## 4. Layout

### Home

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ▍c0n50le                         [🎮 Controller] [21:40] [⚙ Sistema]      │  barra: 72 px
│                                                                          │
│ ▬                                                                        │
│ CONTROL RESONANT                                                         │  eroe: titolo 64,
│ Azione · 2026 · Remedy                                                   │  dettagli, descrizione
│ Descrizione su tre righe al massimo…                                     │  (3 righe), azioni
│ [ ▶ Gioca ]  [ ⋯ ]                 In esecuzione · Ⓐ riprendi            │
│                                                                          │
│ ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐                                       │  fila di card:
│ │    │ │    │ │    │ │    │ │ ⚙  │                                       │  la selezionata si
│ └────┘ └────┘ └────┘ └────┘ └────┘                                       │  solleva; l'ultima è
│                                                                          │  la card Sistema
│ Ⓐ Gioca   Ⓨ Opzioni card   ☰ Sistema                   (suggerimenti)    │  barra dei comandi
└──────────────────────────────────────────────────────────────────────────┘
   sfondo: artwork del gioco selezionato, a dissolvenza, con velo del tema
```

- La pill di input mostra il dispositivo attivo; la barra dei comandi in basso cambia glifi (Ⓐ/✕, Invio, clic)
  o si nasconde in modalità mouse.
- Card: copertina verticale di LaunchBox; senza copertina, titolo su sfondo del tema. ⋯ / Ⓨ apre il menu della
  card: Gioca, Modifica, Cerca un altro titolo, Nascondi, Rimuovi (solo per quelle manuali).
- Fila vuota: la card "Aggiungi un gioco" e il link a Sistema → Libreria.

### Sistema

Pannello a schermo intero sopra la home, con la stessa barra in alto e Indietro (Ⓑ / Esc).

| Categoria | Contenuto |
|---|---|
| Libreria | Cartella giochi, Steam on/off, Epic on/off, Aggiungi exe, Riscansiona, giochi nascosti |
| Schermo | Schermo su cui si apre la home, scala dell'interfaccia |
| Comandi | Tasto "torna alla home", combinazione del controller, prova del controller |
| Aspetto e suoni | Tema, card Sistema visibile, animazioni ridotte, suoni, musica, intro |
| Avvio | Avvia con Windows, apri a schermo intero, resta nella tray alla chiusura |
| Aggiornamenti | Controlla automaticamente, canale beta, Controlla adesso |
| Info e diagnostica | Versione, licenze, cartella dati, copia del report |
| Lingua | Italiano / English |

Ogni riga ha la stessa struttura: etichetta e descrizione a sinistra, valore o controllo a destra, altezza
minima unica.

## 5. Temi

Un tema è un set di token (sfondo, velo sopra l'artwork, superficie, bordo, testo, testo secondario, accento,
focus, raggi, ombre). Tutti i controlli leggono solo i token, mai colori diretti.

| Tema | Idea |
|---|---|
| **Notte** (predefinito) | Superfici scure opache, accento turchese, massimo contrasto sopra qualsiasi sfondo |
| **Vetro** | Il vetro liquido di consolemode: acrilico, bordo luminoso, tinta del gioco selezionato |
| **Colore del gioco** | Superfici scure tinte con il colore dominante dell'artwork, accento derivato |
| **Alba** | Tema chiaro, superfici crema, testo scuro, per stanze illuminate |
| **Arcade** | Nero pieno, accento magenta, bordi netti a raggio piccolo, per chi vuole un look retrò |

Regole di allineamento: griglia di 8 px; altezze fisse per pulsanti (48), pill (40), righe (72); icone 20 px in
riquadri da 24 centrati con il testo sulla stessa linea di base; padding orizzontale identico per pulsanti e
pill; test visivo su ogni tema a 1080p e 4K.

## 6. Fasi di lavoro

1. **Impalcatura**: solution, progetti, `global.json`, `.gitignore`, LICENSE, README, CI.
2. **Core e test**: preferenze, libreria e scoperta, catalogo LaunchBox, input, temi, localizzazione, versioni.
3. **Finestra e avvio giochi**: schermo intero sullo schermo scelto, avvio/ripresa/controllo del processo,
   "torna alla home" da tastiera e controller, tray, avvio con Windows.
4. **Home**: barra e pill, eroe, fila di card, sfondo a dissolvenza, menu e dialoghi delle card, card Sistema,
   cambio automatico di input.
5. **Sistema**: le otto categorie.
6. **Temi**: i cinque temi e un giro di allineamento su tutte le viste.
7. **Contorno**: intro, suoni, musica, tour.
8. **Pacchetto**: script di publish, installer Inno Setup, release workflow, OTA.
9. **Verifica**: unit test; build e avvio su questo PC con i quattro exe di prova e il controller collegato;
   screenshot di home, Sistema e ogni tema; prova di avvio, ritorno alla home e ripresa.

Commit in Conventional Commits, uno per fase. Niente push né release senza conferma.

## 7. Fuori dalla 0.1.0 (candidati per dopo)

Batteria dei controller nella barra · lingue oltre it/en · ricerca nella libreria · altri store (GOG, Xbox,
EA) · motore di sessione (schermi, audio, HDR), se un giorno servirà.
