# BPM2SCMT

[English](README.md) · [Deutsch](README.de.md)

Measures the tempo (BPM) of the music a Windows PC plays, so that the tile of [SCMT](https://github.com/Schumey/SCMT)
can show it ("138 BPM" under the artist). Online sources hardly help here: Spotify no longer gives out the tempo, and
Deezer has it almost only for tracks up to 2018. So BPM2SCMT listens to what the PC plays and measures it.

How the value gets to the screen:

```
music on the PC ──► BPM2SCMT.exe ──► C:\ProgramData\BPM2SCMT\bpm.txt
                                            │  read every 10 s
                                            ▼
                    HASS.Agent (custom sensor) ──► Home Assistant: sensor.<pc>_bpm
                                                          │
                                                          ▼
                                     Tessera ──► SCMT tile, option "Tempo"
```

## What you need

| Part | What for | Notes |
|---|---|---|
| Windows 10 (version 2004 or newer) or Windows 11, 64-bit | runs BPM2SCMT | The PC that plays the music. No .NET needs to be installed. |
| [BPM2SCMT.exe](https://github.com/Schumey/BPM2SCMT/releases/latest) | measures the tempo | One file, no installer. |
| [HASS.Agent .NET10](https://github.com/v1k70rk4/HASS.Agent.NET10), connected to Home Assistant | turns the file into a sensor | Tested with 10.9.2. See "Other HASS.Agent versions" below. |
| [Home Assistant](https://www.home-assistant.io/) | holds the sensor | |
| [Tessera](https://github.com/MaxGramser/homeassistant_espscreen) with a screen | shows the tile | The Home Assistant app "Tessera Screen Manager" and an ESP32 display. |
| [SCMT](https://github.com/Schumey/SCMT) 0.12.0 or newer | the tile with the tempo line | Added to the screen in Tessera. |

The media player that the SCMT tile shows can be any `media_player`, for example the one HASS.Agent makes from the
Windows media session. The tempo does not come from the player, but from the sound of the PC.

## Setup

### 1. Install BPM2SCMT

1. Download `BPM2SCMT.exe` from the [latest release](https://github.com/Schumey/BPM2SCMT/releases/latest).
2. Put it into a folder where it can stay, for example `C:\ProgramData\BPM2SCMT\` (the same folder it writes to).
3. Open a command prompt in that folder (in Explorer: type `cmd` into the address bar and press Enter) and run:

   ```
   BPM2SCMT.exe --autostart on
   BPM2SCMT.exe
   ```

   The first line makes it start with Windows (for your user), the second starts it now. It has no window and no tray
   icon; in the Task Manager it is listed as "BPM2SCMT".
4. Check that it measures: play music and run `BPM2SCMT.exe --stop`, then `BPM2SCMT.exe --console`. Every second a
   line shows the estimate; after about 10 seconds `shown:` has a value, the one written to `bpm.txt`. End it with
   Ctrl+C and start it again in the background with `BPM2SCMT.exe`.

The program is not signed. If Windows shows "Windows protected your PC" on the first start, choose **More info** and
**Run anyway**.

### 2. Sensor in HASS.Agent

In HASS.Agent, open **Custom sensors** and add a new sensor:

| Field | Value |
|---|---|
| Type | Command (program) |
| Name | `BPM` |
| Parameter | `cmd.exe /c type C:\ProgramData\BPM2SCMT\bpm.txt` |
| Unit | `BPM` |
| Profile | Fast (10 seconds) |

With **Test value** you can check that the value arrives (music must play for about 10 seconds). Then store and activate the
sensors, so that HASS.Agent sends them to Home Assistant.

The HASS.Agent documentation advises against the profile "Fast" for command sensors, as a command may take up to 10
seconds; `type` takes a few milliseconds.

### 3. Find the sensor in Home Assistant

In Home Assistant, open **Settings → Devices & services → Devices**, choose your PC and look for the sensor "BPM". Its
entity ID is something like `sensor.<pc>_bpm`, for example `sensor.living_room_pc_bpm`. While music plays its state is
the tempo as a whole number (`138`); while nothing plays it is "unknown".

### 4. Show it in SCMT

1. In Tessera, open **Plugins**, choose the sensor as SCMT's **Tempo sensor** for the screen, and build the screen.
2. In the options of the "SCMT" tile, switch on **Tempo**.

The line appears when the tile is high enough. After a new track the tile waits until the sensor has the new tempo, so
that it does not show the tempo of the track before. Details are in the [SCMT README](https://github.com/Schumey/SCMT#tempo).

## How it measures

- It records what goes out of the default output device (WASAPI loopback). So it hears everything the PC plays, not
  only Spotify. When the default device changes (headphones, another screen), it follows within 5 seconds.
- From the sound it computes a curve of the beats. The bass counts three times, because in electronic music the kick
  drum carries the beat. The tempo is the beat length at which this curve repeats best over eight beats. It looks
  between 85 and 180 BPM.
- It measures the last 12 seconds. The first estimate comes after 6 seconds. A value is written as soon as four of the
  last six estimates lie at most 1 BPM apart, so after about 10 seconds. It is written as a whole number.
- It reads the track from Windows' media controls (the same source HASS.Agent uses). A new track starts the
  measurement again. While the player is paused or stopped, the file is empty.

Test with Deezer previews (30 s): 12 of 12 tracks lie at most 0.2 BPM from Deezer's value, for example Darren Porter
"Inertia" with 140.0 (Deezer: 140.15), Roger Shah "Healesville Sanctuary" with 138.0 (137.81) and Daft Punk "Get Lucky"
with 116.1 (116.13).

### Limits

- Only tempos from 85 to 180 BPM. A track at 70 BPM is shown as 140, one at 190 as 95.
- Music without a clear beat (classical, ambient, speech) gets no value, or a wrong one.
- It measures the sound, not the player: other sounds of the PC (a video, a game) count too.
- One PC, one sensor. A PC that plays nothing gives no tempo, even if another player in the house plays.

## Commands

| Command | What |
|---|---|
| `BPM2SCMT.exe` | Runs in the background (only one at a time). |
| `BPM2SCMT.exe --console` | Runs in a window that shows estimate, confidence and level every second, and writes `bpm-log.csv`. |
| `BPM2SCMT.exe --log` | Runs in the background and writes `bpm-log.csv`. |
| `BPM2SCMT.exe --stop` | Ends the BPM2SCMT that runs. |
| `BPM2SCMT.exe --autostart on` | Starts with Windows (`off`: no longer). |
| `BPM2SCMT.exe song.mp3` | Measures an audio file second by second. |
| `BPM2SCMT.exe --version` | Prints the version. |
| `BPM2SCMT.exe --help` | Lists the commands. |

All files are in `C:\ProgramData\BPM2SCMT\`: `bpm.txt` (the value), `bpm-log.csv` (one line per second with track,
estimate, confidence, level and the value written) and `error.log`, if it crashes.

## When it does not work

| What you see | What to check |
|---|---|
| `bpm.txt` stays empty | Does music play, and loud enough? Run `BPM2SCMT.exe --console` and look at the level: below about -60 dB counts as silence. Is the player paused in Windows' media controls? |
| The console says "BPM2SCMT is already running" | End the background one with `--stop` first. |
| The sensor is "unknown" in Home Assistant | Normal while nothing plays. Otherwise: does **Test value** in HASS.Agent show a number? Is HASS.Agent connected? |
| The tempo is half or double | The track lies outside 85 to 180 BPM, or its beat is unclear. |
| The tile shows no tempo line | Is the sensor chosen as "Tempo sensor" in Tessera, and the screen built again? Is "Tempo" switched on, and is the tile high enough? |
| It no longer starts with Windows | Moved the exe? Run `--autostart on` again from the new folder. |
| It ends on its own | Look into `C:\ProgramData\BPM2SCMT\error.log`. |

### Other HASS.Agent versions

The setup above is tested with HASS.Agent .NET10. If your HASS.Agent (for example the original
[HASS.Agent](https://github.com/hass-agent/HASS.Agent)) has no type "Command (program)", a sensor of type **PowerShell**
with the command `Get-Content C:\ProgramData\BPM2SCMT\bpm.txt` should do the same (not tested). Anything else that can turn the content
of a file into a Home Assistant sensor works too: SCMT only needs a sensor whose state is the tempo as a number.

## Uninstall

```
BPM2SCMT.exe --stop
BPM2SCMT.exe --autostart off
```

Then delete the folder `C:\ProgramData\BPM2SCMT\` and the sensor in HASS.Agent.

## Build it yourself

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet publish src/BPM2SCMT/BPM2SCMT.csproj -c Release -o publish
```

This gives `publish\BPM2SCMT.exe`, one file that needs no installed .NET. A tag `v*` pushed to GitHub builds the same
file and attaches it to a release (`.github/workflows/release.yml`).

## License

MIT, see [LICENSE](LICENSE). Uses [NAudio](https://github.com/naudio/NAudio) (MIT).
