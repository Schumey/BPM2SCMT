# Changelog

## 1.0.0 - 2026-10-18

First public release.

- Measures the tempo of what the PC plays (WASAPI loopback on the default output device, which it follows) and writes
  it to `C:\ProgramData\BPM2SCMT\bpm.txt` as a whole number, empty while nothing plays. For a HASS.Agent sensor and
  the option "Tempo" of SCMT 0.12.0.
- Runs in the background without a window, one instance at a time. Commands `--console`, `--log`, `--stop`,
  `--autostart on|off`, `--version`, `--help`, and an audio file to measure.
- The program's messages are in English.
- One exe for Windows x64 that needs no installed .NET.
