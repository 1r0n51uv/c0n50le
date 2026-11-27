# c0n50le: piano

Stato: 0.1.0 implementata · 26/11/2026

c0n50le è un'app nuova e separata: una home a schermo intero, nello stile di Apple TV (tvOS), per avviare dal divano i
giochi e le app del PC con il controller o con mouse e tastiera. Viene da due progetti, consolemode e ron1n-mode: la
libreria e le card di ron1n-mode, il contorno di consolemode (suoni, musica, intro, tour, tray, aggiornamenti). È scritta
da zero.

## 1. Decisioni

| Tema | Decisione |
|---|---|
| Base | Riscrittura da zero. Il codice delle due fonti si copia e si adatta file per file. |
| Upstream | `lippdev/consolemode` e `1r0n51uv/consolemode` sono fonte di idee e codice, senza legame git. Upstream è fermo alla 1.7.1. |
| Identità | "c0n50le", `C0n50le.exe`, dati in `%LOCALAPPDATA%\c0n50le` (portatile: `c0n50le_Data`), AppId Inno Setup nuovo, release e OTA da `1r0n51uv/c0n50le`, prima versione 0.1.0. Nessuna migrazione. |
| Licenza | AGPL-3.0, come le fonti. |
| Motore di sessione | Escluso: c0n50le non cambia schermi, audio, HDR o risoluzione. |
| Libreria | Exe aggiunti a mano, cartella giochi, Steam ed Epic; copertine, logo trasparente, sfondi e descrizioni da LaunchBox. Niente Playnite. |
| In gioco | Solo il ritorno alla home (Ctrl+Alt+F-tasto, combinazione del controller) e la ripresa senza seconda istanza. |
| Input | Cambio automatico tra controller e mouse/tastiera. Con il mouse, puntare un elemento gli dà il focus, come il touch del telecomando Apple: un solo modo di evidenziare. |
| Stile | **tvOS**, misurato sul kit Apple tvOS UI (Figma, 1920×1080) e sulle Human Interface Guidelines. Seconda riscrittura dell'interfaccia, dopo una prima versione in stile console. |
| Temi | Solo **Chiaro** e **Scuro**. **Liquid Glass**: Spento (materiale pieno) o Leggero, Medio (il materiale del kit), Profondo. |
| Lingue | Italiano e inglese, con test su chiavi e segnaposto. |

## 2. Lo stile tvOS in c0n50le

Misure prese dal file Figma (1920×1080):

| Elemento | Kit tvOS | In c0n50le |
|---|---|---|
| Materiale | `#1E1E1E` al 50% (chiaro: `#FEFDFF` al 50%) + sfocatura, bordo bianco 10–16% | `AcrylicBrush` con la stessa tinta; la profondità del vetro ne cambia trasparenza, riflesso in alto e bordo luminoso |
| Focus | piattaforma bianca al 90%, testo nero, ingrandimento | pulsanti, righe e voci di menu cambiano solo sfondo; crescono (+5%) testo e icone, ciascuno dal proprio bordo esterno, così niente esce dalla riga; le card crescono del 22% (250×150 → 306×184) con ombra e luce speculare |
| Card | 250×150, angoli 30 | artwork del gioco con il suo logo trasparente sopra, come un'icona di Apple TV; altrimenti banner, copertina o nome |
| Dock | piattaforma 1820×230, angoli 60 | in basso, con tutti i giochi (i più recenti prima), poi Aggiungi e Sistema; scorre di lato |
| Top shelf / pagina prodotto | artwork, logo, metadati 29, descrizione 29 | artwork a schermo intero del gioco a fuoco, logo (o titolo 76), dettagli e descrizione; su Aggiungi e Sistema mostra cosa fanno |
| Menu contestuale | 440 di larghezza, angoli 30, voci 70, divisori | pannello arrotondato senza bordi visibili; le voci lo riempiono da bordo a bordo: la prima prende gli angoli superiori, l'ultima quelli inferiori, quelle in mezzo sono rettangoli; tra i gruppi una fascia tenue |
| Avviso | 504, angoli 48, pulsanti 66 impilati, sfondo scurito | conferme, aggiunta e modifica di un gioco, ricerca della copertina con anteprima, aggiornamenti, tour |
| Impostazioni | sidebar con voci 72 (angoli 16), icona in un cerchio da 44 | sidebar a sinistra, righe nome/valore a destra, valori come testo ("Attivo"), descrizione della riga a fuoco in basso; niente switch |
| Barra in alto | — | un'unica app bar di vetro: modalità di input, ora, Sistema e Desktop |
| Widget | — | a destra sotto la barra: meteo (Open-Meteo, città scelta), notizie (feed RSS/Atom), galleria (cartella di foto); in Sistema → Widget |
| Testo | SF Pro Medium 23–31, Bold 31–76, secondario al 50–60% | Inter (SF Pro non è distribuibile su Windows) agli stessi corpi e pesi |

Principi Apple seguiti: un solo elemento a fuoco e sempre evidente; il contenuto (l'artwork) prima dei controlli; materiali
che lasciano intravedere ciò che sta dietro; gerarchia data da corpo e peso del testo, non da cornici; niente indicazioni
superflue (i suggerimenti dei tasti compaiono solo usando il controller); testi brevi, il resto nella descrizione in basso.

## 3. Architettura

```
src/C0n50le.Core/   logica senza WinUI: libreria e scoperta, catalogo LaunchBox (copertina, sfondo, logo, banner),
                    preferenze, input, temi (Chiaro/Scuro, profondità del vetro), widget (meteo, notizie, galleria), testi, versioni, tour, suoni
src/C0n50le/        WinUI 3: MainWindow (home, Sistema, menu, avvisi, tour, intro), Themes/Theme.xaml (componenti tvOS),
                    Ui/ThemeManager (ricolora i pennelli), Services (controller, sessione di gioco, tray, aggiornamenti…)
tests/              114 test xUnit sul Core
tools/TestGames/    giochi finti per le prove
build/              publish, Inno Setup; .github/workflows: CI e release
```

## 4. Fuori dalla 0.1.0

Batteria dei controller · altre lingue · ricerca nella libreria · GOG, Xbox, EA · motore di sessione.
