# BPM2SCMT

Misst das Tempo (BPM) der Musik, die ein Windows-PC gerade abspielt, damit die Kachel von
[SCMT](https://github.com/Schumey/SCMT) es anzeigen kann. Online-Quellen helfen dafür kaum: Spotify gibt das Tempo
nicht mehr heraus, und Deezer hat es fast nur für Titel bis 2018.

Der Weg des Werts:

1. `BPM2SCMT.exe` läuft im Hintergrund und schreibt das Tempo nach `C:\ProgramData\BPM2SCMT\bpm.txt`. Die Datei ist
   leer, solange nichts läuft oder der Wert noch nicht sicher ist.
2. Ein eigener Sensor in HASS.Agent liest die Datei alle 10 Sekunden und gibt den Wert an Home Assistant.
3. SCMT zeigt den Sensor auf der Kachel an (in Arbeit).

## So misst es

- Es schneidet mit, was über das Standard-Ausgabegerät läuft (WASAPI-Loopback, NAudio). Es hört also alles, was der
  PC abspielt, nicht nur Spotify. Wechselt das Standardgerät (etwa auf Kopfhörer), folgt es innerhalb von 5 Sekunden.
- Aus dem Klang wird eine Kurve der Anschläge berechnet. Der Bass zählt dreifach, weil bei elektronischer Musik die
  Bassdrum den Takt trägt. Das Tempo ist die Schlaglänge, bei der sich diese Kurve über acht Schläge am besten
  wiederholt. Gesucht wird zwischen 85 und 180 BPM.
- Gemessen werden die letzten 12 Sekunden. Die erste Schätzung kommt nach 6 Sekunden. Ein Wert wird geschrieben,
  sobald vier der letzten sechs Schätzungen höchstens 1 BPM auseinanderliegen, also nach etwa 10 Sekunden. Geschrieben
  wird er als ganze Zahl.
- Den Titel liest es aus der Windows-Mediensteuerung, derselben Quelle wie HASS.Agent. Bei einem neuen Titel beginnt
  die Messung von vorn. Ist der Player pausiert oder gestoppt, ist die Datei leer.

## Testergebnis mit Deezer-Vorschauen (30 s)

12 von 12 Titeln liegen höchstens 0,2 BPM neben dem Wert von Deezer, zum Beispiel Darren Porter „Inertia“ mit 140,0
(Deezer: 140,15), Roger Shah „Healesville Sanctuary“ mit 138,0 (137,81) und Daft Punk „Get Lucky“ mit 116,1 (116,13).

## Einrichten

`BPM2SCMT.exe` braucht kein installiertes .NET. Lege es in einen festen Ordner, zum Beispiel
`C:\Users\<du>\Programme\BPM2SCMT\`, und führe dort in einer Eingabeaufforderung aus:

```
BPM2SCMT.exe --autostart on
BPM2SCMT.exe
```

Die erste Zeile lässt es mit Windows starten, die zweite startet es jetzt. Es hat kein Fenster und kein Symbol im
Infobereich; im Task-Manager steht es als „BPM2SCMT“.

### Sensor in HASS.Agent.NET10

Unter **Custom sensors** einen neuen Sensor anlegen:

| Feld | Wert |
|---|---|
| Name | `BPM` |
| Type | Command (program) |
| Parameter | `cmd.exe /c type C:\ProgramData\BPM2SCMT\bpm.txt` |
| Unit | `BPM` |
| Profile | Fast (10 Sekunden) |

Mit **Test value** lässt sich prüfen, ob der Wert ankommt (Musik muss seit etwa 10 Sekunden laufen). Ohne Musik ist der
Sensor in Home Assistant „unbekannt“. Die HASS.Agent-Doku rät bei Befehls-Sensoren vom Profil „Fast“ ab, weil ein
Befehl bis zu 10 Sekunden brauchen darf; `type` braucht nur wenige Millisekunden.

## Befehle

| Befehl | Was |
|---|---|
| `BPM2SCMT.exe` | Läuft im Hintergrund (immer nur einmal). |
| `BPM2SCMT.exe --console` | Läuft mit einem Fenster, das jede Sekunde Schätzung, Sicherheit und Pegel zeigt, und schreibt `bpm-log.csv`. |
| `BPM2SCMT.exe --log` | Läuft im Hintergrund und schreibt `bpm-log.csv`. |
| `BPM2SCMT.exe --stop` | Beendet das laufende BPM2SCMT. |
| `BPM2SCMT.exe --autostart on` | Startet mit Windows (`off`: nicht mehr). |
| `BPM2SCMT.exe song.mp3` | Misst eine Audiodatei Sekunde für Sekunde. |

Alle Dateien liegen in `C:\ProgramData\BPM2SCMT\`: `bpm.txt`, `bpm-log.csv` (eine Zeile pro Sekunde mit Titel,
Schätzung, Sicherheit, Pegel und geschriebenem Wert) und `error.log`, falls es abstürzt.

## Bauen

```
dotnet publish src/BPM2SCMT/BPM2SCMT.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Braucht das .NET-10-SDK.
