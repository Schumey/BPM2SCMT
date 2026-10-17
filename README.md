# BPM2SCMT

Misst das Tempo (BPM) der Musik, die ein Windows-PC gerade abspielt, damit die Kachel von
[SCMT](https://github.com/Schumey/SCMT) es anzeigen kann. Online-Quellen helfen dafür kaum: Spotify gibt das Tempo
nicht mehr heraus, und Deezer hat es fast nur für Titel bis 2018.

Stand: **Prototyp** (`src/BpmProbe`). Er zeigt das Tempo nur in der Konsole an und schreibt es in Dateien. Ob er
später in HASS.Agent.NET10 eingebaut wird oder als kleines Zusatzprogramm einen Sensor liefert, wird nach dem Test
entschieden.

## So misst er

- Er schneidet mit, was über das Standard-Ausgabegerät läuft (WASAPI-Loopback, NAudio). Er hört also alles, was der
  PC abspielt, nicht nur Spotify.
- Aus dem Klang wird eine Kurve der Anschläge berechnet. Der Bass zählt dreifach, weil bei elektronischer Musik die
  Bassdrum den Takt trägt. Das Tempo ist die Schlaglänge, bei der sich diese Kurve über acht Schläge am besten
  wiederholt. Gesucht wird zwischen 85 und 180 BPM.
- Gemessen werden die letzten 12 Sekunden. Die erste Schätzung kommt nach 6 Sekunden. Angezeigt wird ein Wert, sobald
  vier der letzten sechs Schätzungen höchstens 1 BPM auseinanderliegen, also nach etwa 10 Sekunden.
- Den Titel liest er aus der Windows-Mediensteuerung, derselben Quelle wie HASS.Agent. Bei einem neuen Titel beginnt
  die Messung von vorn.

## Testergebnis mit Deezer-Vorschauen (30 s)

12 von 12 Titeln liegen höchstens 0,2 BPM neben dem Wert von Deezer, zum Beispiel Darren Porter „Inertia“ mit 140,0
(Deezer: 140,15), Roger Shah „Healesville Sanctuary“ mit 138,0 (137,81) und Daft Punk „Get Lucky“ mit 116,1 (116,13).

## Benutzung

`BpmProbe.exe` braucht kein installiertes .NET.

- **Ohne Argument:** misst live. Jede Sekunde steht eine Zeile in der Konsole. Neben der EXE entstehen
  `bpm-log.csv` (eine Zeile pro Sekunde mit Titel, Schätzung, Sicherheit und Pegel) und `bpm.txt` (der angezeigte
  Wert, leer ohne Musik). Beenden mit Strg+C.
- **Mit einer Audiodatei:** `BpmProbe.exe song.mp3` misst die Datei Sekunde für Sekunde.

## Bauen

```
dotnet publish src/BpmProbe/BpmProbe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

Braucht das .NET-10-SDK.
