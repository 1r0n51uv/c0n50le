# Avvisi di terze parti

## Codice di provenienza

c0n50le è scritto da zero, ma riprende e adatta codice da due progetti sotto GNU AGPL-3.0:

- [1r0n51uv/consolemode](https://github.com/1r0n51uv/consolemode), a sua volta derivato da
  [lippdev/consolemode](https://github.com/lippdev/consolemode): template a vetro dei pulsanti, suoni e loro sintesi,
  musica della home, scala dell'interfaccia, tour, icona nell'area di notifica, avvio con Windows.
- [1r0n51uv/ron1n-mode](https://github.com/1r0n51uv/ron1n-mode): scoperta dei giochi (cartella, Steam, Epic), lettura del
  catalogo LaunchBox, avvio e ripresa dei giochi, lettura del controller, scorciatoia da tastiera, aggiornamenti.

Il file [LICENSE](LICENSE) contiene la licenza AGPL-3.0, che vale per tutto c0n50le.

## Font

[Inter](https://rsms.me/inter/) 4.1 (`src/C0n50le/Assets/Fonts/InterVariable.ttf`), SIL Open Font License 1.1: la licenza è in
`src/C0n50le/Assets/Fonts/Inter-OFL.txt`.

## Suoni

| File | Origine | Licenza |
|---|---|---|
| `move.wav`, `confirm.wav`, `back.wav` | [Interface Sounds](https://kenney.nl/assets/interface-sounds) di Kenney, via Console Mode | CC0 1.0 |
| `boot.wav`, `open.wav` | generati da [`tools/make_sounds.py`](https://github.com/mattxslv/Selene/blob/main/tools/make_sounds.py) di [Selene](https://github.com/mattxslv/Selene), via Console Mode | MIT, Copyright (c) 2026 Matthew Silva |

La musica della home è sintetizzata nel codice.

## Dati

Testi e immagini delle card vengono dal [LaunchBox Games Database](https://gamesdb.launchbox-app.com/) al momento
dell'uso e restano nella cartella dei dati dell'utente; non sono distribuiti con c0n50le.

## Componenti

Windows App SDK e WinUI 3 (Microsoft, MIT), CommunityToolkit.Mvvm (.NET Foundation, MIT), H.NotifyIcon (MIT).
