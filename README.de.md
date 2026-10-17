# BPM2SCMT

[English](README.md) · [Deutsch](README.de.md)

Misst das Tempo (BPM) der Musik, die ein Windows-PC abspielt, damit die Kachel von [SCMT](https://github.com/Schumey/SCMT)
es anzeigen kann („138 BPM“ unter dem Interpreten). Online-Quellen helfen dafür kaum: Spotify gibt das Tempo nicht
mehr heraus, und Deezer hat es fast nur für Titel bis 2018. Deshalb hört BPM2SCMT mit, was der PC abspielt, und misst
es.

So kommt der Wert auf den Bildschirm:

```
Musik am PC ──► BPM2SCMT.exe ──► C:\ProgramData\BPM2SCMT\bpm.txt
                                        │  alle 10 s gelesen
                                        ▼
                HASS.Agent (eigener Sensor) ──► Home Assistant: sensor.<pc>_bpm
                                                       │
                                                       ▼
                                  Tessera ──► SCMT-Kachel, Option „Tempo“
```

## Was du brauchst

| Teil | Wofür | Hinweise |
|---|---|---|
| Windows 10 (Version 2004 oder neuer) oder Windows 11, 64 Bit | führt BPM2SCMT aus | Der PC, der die Musik spielt. .NET muss nicht installiert sein. |
| [BPM2SCMT.exe](https://github.com/Schumey/BPM2SCMT/releases/latest) | misst das Tempo | Eine Datei, kein Installer. |
| [HASS.Agent .NET10](https://github.com/v1k70rk4/HASS.Agent.NET10), mit Home Assistant verbunden | macht aus der Datei einen Sensor | Getestet mit 10.9.2. Siehe „Andere HASS.Agent-Versionen“ unten. |
| [Home Assistant](https://www.home-assistant.io/) | hält den Sensor | |
| [Tessera](https://github.com/MaxGramser/homeassistant_espscreen) mit einem Bildschirm | zeigt die Kachel | Die Home-Assistant-App „Tessera Screen Manager“ und ein ESP32-Display. |
| [SCMT](https://github.com/Schumey/SCMT) 0.12.0 oder neuer | die Kachel mit der Tempo-Zeile | In Tessera zum Bildschirm hinzugefügt. |

Der Media Player, den die SCMT-Kachel zeigt, kann jeder `media_player` sein, zum Beispiel der, den HASS.Agent aus der
Windows-Mediensitzung macht. Das Tempo kommt nicht vom Player, sondern aus dem Ton des PCs.

## Einrichten

### 1. BPM2SCMT installieren

1. `BPM2SCMT.exe` aus dem [neuesten Release](https://github.com/Schumey/BPM2SCMT/releases/latest) herunterladen.
2. In einen Ordner legen, in dem es bleiben kann, zum Beispiel `C:\ProgramData\BPM2SCMT\` (derselbe Ordner, in den es
   schreibt).
3. In diesem Ordner eine Eingabeaufforderung öffnen (im Explorer `cmd` in die Adresszeile tippen und Enter drücken) und
   ausführen:

   ```
   BPM2SCMT.exe --autostart on
   BPM2SCMT.exe
   ```

   Die erste Zeile lässt es mit Windows starten (für deinen Benutzer), die zweite startet es jetzt. Es hat kein Fenster
   und kein Symbol im Infobereich; im Task-Manager steht es als „BPM2SCMT“.
4. Prüfen, ob es misst: Musik abspielen, `BPM2SCMT.exe --stop` und dann `BPM2SCMT.exe --console` ausführen. Jede
   Sekunde zeigt eine Zeile die Schätzung; nach etwa 10 Sekunden hat `shown:` einen Wert, den Wert in `bpm.txt`. Mit
   Strg+C beenden und mit `BPM2SCMT.exe` wieder im Hintergrund starten.

Das Programm ist nicht signiert. Zeigt Windows beim ersten Start „Der Computer wurde durch Windows geschützt“, auf
**Weitere Informationen** und **Trotzdem ausführen** klicken.

### 2. Sensor in HASS.Agent

In HASS.Agent unter **Custom sensors** einen neuen Sensor anlegen:

| Feld | Wert |
|---|---|
| Type | Command (program) |
| Name | `BPM` |
| Parameter | `cmd.exe /c type C:\ProgramData\BPM2SCMT\bpm.txt` |
| Unit | `BPM` |
| Profile | Fast (10 Sekunden) |

Mit **Test value** lässt sich prüfen, ob der Wert ankommt (Musik muss seit etwa 10 Sekunden laufen). Danach die Sensoren
speichern und aktivieren, damit HASS.Agent sie an Home Assistant schickt.

Die HASS.Agent-Doku rät bei Befehls-Sensoren vom Profil „Fast“ ab, weil ein Befehl bis zu 10 Sekunden brauchen darf;
`type` braucht nur wenige Millisekunden.

### 3. Den Sensor in Home Assistant finden

In Home Assistant unter **Einstellungen → Geräte & Dienste → Geräte** deinen PC wählen und den Sensor „BPM“ suchen. Seine
Entitäts-ID ist etwa `sensor.<pc>_bpm`, zum Beispiel `sensor.wohnzimmer_pc_bpm`. Solange Musik läuft, ist sein Zustand
das Tempo als ganze Zahl (`138`); läuft nichts, ist er „unbekannt“.

### 4. In SCMT anzeigen

1. In Tessera unter **Plugins** den Sensor als **Tempo-Sensor** von SCMT für den Bildschirm wählen und den Bildschirm
   bauen.
2. In den Optionen der Kachel „SCMT“ **Tempo** einschalten.

Die Zeile erscheint, wenn die Kachel hoch genug ist. Nach einem neuen Titel wartet die Kachel, bis der Sensor das neue
Tempo hat, damit sie nicht das Tempo des vorigen Titels zeigt. Mehr dazu im
[README von SCMT](https://github.com/Schumey/SCMT/blob/main/README.de.md#tempo).

## So misst es

- Es schneidet mit, was über das Standard-Ausgabegerät läuft (WASAPI-Loopback). Es hört also alles, was der PC
  abspielt, nicht nur Spotify. Wechselt das Standardgerät (Kopfhörer, anderer Bildschirm), folgt es innerhalb von 5
  Sekunden.
- Aus dem Klang wird eine Kurve der Anschläge berechnet. Der Bass zählt dreifach, weil bei elektronischer Musik die
  Bassdrum den Takt trägt. Das Tempo ist die Schlaglänge, bei der sich diese Kurve über acht Schläge am besten
  wiederholt. Gesucht wird zwischen 85 und 180 BPM.
- Gemessen werden die letzten 12 Sekunden. Die erste Schätzung kommt nach 6 Sekunden. Ein Wert wird geschrieben,
  sobald vier der letzten sechs Schätzungen höchstens 1 BPM auseinanderliegen, also nach etwa 10 Sekunden. Geschrieben
  wird er als ganze Zahl.
- Den Titel liest es aus der Windows-Mediensteuerung, derselben Quelle wie HASS.Agent. Bei einem neuen Titel beginnt
  die Messung von vorn. Ist der Player pausiert oder gestoppt, ist die Datei leer.

Test mit Deezer-Vorschauen (30 s): 12 von 12 Titeln liegen höchstens 0,2 BPM neben dem Wert von Deezer, zum Beispiel
Darren Porter „Inertia“ mit 140,0 (Deezer: 140,15), Roger Shah „Healesville Sanctuary“ mit 138,0 (137,81) und Daft Punk
„Get Lucky“ mit 116,1 (116,13).

### Grenzen

- Nur Tempi von 85 bis 180 BPM. Ein Titel mit 70 BPM wird als 140 angezeigt, einer mit 190 als 95.
- Musik ohne klaren Takt (Klassik, Ambient, Sprache) bekommt keinen Wert oder einen falschen.
- Es misst den Ton, nicht den Player: Andere Geräusche des PCs (ein Video, ein Spiel) zählen mit.
- Ein PC, ein Sensor. Ein PC, der nichts spielt, liefert kein Tempo, auch wenn ein anderer Player im Haus spielt.

## Befehle

| Befehl | Was |
|---|---|
| `BPM2SCMT.exe` | Läuft im Hintergrund (immer nur einmal). |
| `BPM2SCMT.exe --console` | Läuft in einem Fenster, das jede Sekunde Schätzung, Sicherheit und Pegel zeigt, und schreibt `bpm-log.csv`. |
| `BPM2SCMT.exe --log` | Läuft im Hintergrund und schreibt `bpm-log.csv`. |
| `BPM2SCMT.exe --stop` | Beendet das laufende BPM2SCMT. |
| `BPM2SCMT.exe --autostart on` | Startet mit Windows (`off`: nicht mehr). |
| `BPM2SCMT.exe song.mp3` | Misst eine Audiodatei Sekunde für Sekunde. |
| `BPM2SCMT.exe --version` | Zeigt die Version. |
| `BPM2SCMT.exe --help` | Listet die Befehle. |

Alle Dateien liegen in `C:\ProgramData\BPM2SCMT\`: `bpm.txt` (der Wert), `bpm-log.csv` (eine Zeile pro Sekunde mit
Titel, Schätzung, Sicherheit, Pegel und geschriebenem Wert) und `error.log`, falls es abstürzt.

Die Ausgaben des Programms sind englisch.

## Wenn es nicht klappt

| Was du siehst | Was du prüfst |
|---|---|
| `bpm.txt` bleibt leer | Läuft Musik, und laut genug? `BPM2SCMT.exe --console` starten und auf den Pegel achten: unter etwa -60 dB gilt als Stille. Ist der Player in der Windows-Mediensteuerung pausiert? |
| Das Fenster meldet „BPM2SCMT is already running“ | Erst das im Hintergrund mit `--stop` beenden. |
| Der Sensor ist in Home Assistant „unbekannt“ | Normal, solange nichts läuft. Sonst: Zeigt **Test value** in HASS.Agent eine Zahl? Ist HASS.Agent verbunden? |
| Das Tempo ist halb oder doppelt so hoch | Der Titel liegt außerhalb von 85 bis 180 BPM, oder sein Takt ist unklar. |
| Die Kachel zeigt keine Tempo-Zeile | Ist der Sensor in Tessera als „Tempo-Sensor“ gewählt und der Bildschirm neu gebaut? Ist „Tempo“ eingeschaltet und die Kachel hoch genug? |
| Es startet nicht mehr mit Windows | Exe verschoben? `--autostart on` im neuen Ordner noch einmal ausführen. |
| Es beendet sich von selbst | In `C:\ProgramData\BPM2SCMT\error.log` nachsehen. |

### Andere HASS.Agent-Versionen

Die Einrichtung oben ist mit HASS.Agent .NET10 getestet. Hat dein HASS.Agent (zum Beispiel das ursprüngliche
[HASS.Agent](https://github.com/hass-agent/HASS.Agent)) keinen Typ „Command (program)“, sollte ein Sensor vom Typ
**PowerShell** mit dem Befehl `Get-Content C:\ProgramData\BPM2SCMT\bpm.txt` dasselbe tun (nicht getestet). Alles andere,
das den Inhalt einer Datei zu einem Home-Assistant-Sensor macht, geht auch: SCMT braucht nur einen Sensor, dessen Zustand
das Tempo als Zahl ist.

## Deinstallieren

```
BPM2SCMT.exe --stop
BPM2SCMT.exe --autostart off
```

Danach den Ordner `C:\ProgramData\BPM2SCMT\` und den Sensor in HASS.Agent löschen.

## Selbst bauen

Braucht das [.NET-10-SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet publish src/BPM2SCMT/BPM2SCMT.csproj -c Release -o publish
```

Das ergibt `publish\BPM2SCMT.exe`, eine Datei, die kein installiertes .NET braucht. Ein Tag `v*`, der zu GitHub
gepusht wird, baut dieselbe Datei und hängt sie an ein Release (`.github/workflows/release.yml`).

## Lizenz

MIT, siehe [LICENSE](LICENSE). Nutzt [NAudio](https://github.com/naudio/NAudio) (MIT).
