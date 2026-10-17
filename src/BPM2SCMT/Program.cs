// BPM2SCMT: measures the tempo of what this PC plays and writes it to C:\ProgramData\BPM2SCMT\bpm.txt, where a
// custom sensor of HASS.Agent reads it for the BPM display of SCMT.
//
//   BPM2SCMT                  runs in the background, without a window (one instance at a time).
//   BPM2SCMT --console        the same with a console that shows the tempo every second, and writes bpm-log.csv.
//   BPM2SCMT --log            in the background, and writes bpm-log.csv.
//   BPM2SCMT --stop           ends the instance that runs.
//   BPM2SCMT --autostart on   starts with Windows (off: no longer).
//   BPM2SCMT <audio file>     measures a file (mp3, wav, m4a ...) and prints the tempo second by second.
//
// The track comes from Windows' media controls (the same source HASS.Agent uses): a new track starts the measurement
// again, and while nothing plays the file is empty.

using System.Globalization;
using System.Runtime.InteropServices;
using BPM2SCMT;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Media.Control;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BPM2SCMT");
Directory.CreateDirectory(dataDir);
const string StopName = @"Local\BPM2SCMT-stop";

try
{
    var first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
    switch (first)
    {
        case "":
            return await Live(dataDir, console: false, log: false);
        case "--log":
            return await Live(dataDir, console: false, log: true);
        case "--console":
            OpenConsole(allocate: true);
            return await Live(dataDir, console: true, log: true);
        case "--stop":
            OpenConsole(allocate: false);
            if (EventWaitHandle.TryOpenExisting(StopName, out var stop))
            {
                stop.Set();
                Console.WriteLine("BPM2SCMT wird beendet.");
            }
            else
            {
                Console.WriteLine("BPM2SCMT läuft nicht.");
            }
            return 0;
        case "--autostart":
            OpenConsole(allocate: false);
            return Autostart(args.Length > 1 && args[1].Equals("on", StringComparison.OrdinalIgnoreCase));
        default:
            OpenConsole(allocate: true);
            return AnalyzeFile(args[0]);
    }
}
catch (Exception ex)
{
    File.AppendAllText(Path.Combine(dataDir, "error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\r\n");
    return 1;
}

// A program without a window has no console: take the one of the command line it was started from, or open one.
static void OpenConsole(bool allocate)
{
    if (!AttachConsole(-1) && allocate) AllocConsole();
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine();
}

static int Autostart(bool on)
{
    using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
    if (on)
    {
        run.SetValue("BPM2SCMT", $"\"{Environment.ProcessPath}\"");
        Console.WriteLine($"BPM2SCMT startet jetzt mit Windows ({Environment.ProcessPath}).");
    }
    else
    {
        run.DeleteValue("BPM2SCMT", throwOnMissingValue: false);
        Console.WriteLine("BPM2SCMT startet nicht mehr mit Windows.");
    }
    return 0;
}

static string Show(TempoEstimate? e) =>
    e is null ? "  -   " : $"{e.Bpm,6:0.0} BPM  sicher {e.Confidence:0.00}  Pegel {20 * Math.Log10(e.Level),5:0} dB";

static void Mix(ReadOnlySpan<float> interleaved, int channels, Span<float> mono)
{
    var frames = interleaved.Length / channels;
    for (var f = 0; f < frames; f++)
    {
        float s = 0;
        for (var c = 0; c < channels; c++) s += interleaved[f * channels + c];
        mono[f] = s / channels;
    }
}

static int AnalyzeFile(string path)
{
    using var reader = new MediaFoundationReader(path);
    var samples = reader.ToSampleProvider();
    var channels = samples.WaveFormat.Channels;
    var rate = samples.WaveFormat.SampleRate;
    var detector = new TempoDetector(rate);
    var stable = new Stabilizer();
    var buffer = new float[rate * channels];
    var mono = new float[rate];
    var second = 0;
    int read;
    Console.WriteLine($"{Path.GetFileName(path)} ({rate} Hz, {channels} Kanäle)");
    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
    {
        Mix(buffer.AsSpan(0, read), channels, mono);
        detector.Add(mono.AsSpan(0, read / channels));
        second++;
        var e = detector.Estimate();
        stable.Add(e);
        Console.WriteLine($"{second,4} s  {Show(e)}  angezeigt: {(stable.Value is { } v ? $"{v:0.0}" : "-")}");
    }
    Console.WriteLine($"Ergebnis: {(stable.Value is { } r ? $"{r:0.0} BPM" : "keins")}");
    return 0;
}

static async Task<int> Live(string dataDir, bool console, bool log)
{
    using var single = new Mutex(true, @"Local\BPM2SCMT", out var firstInstance);
    if (!firstInstance)
    {
        if (console) Console.WriteLine("BPM2SCMT läuft schon. Erst mit --stop beenden.");
        return 0;
    }
    using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopName);
    if (console) Console.CancelKeyPress += (_, a) => { a.Cancel = true; stop.Set(); };

    var valuePath = Path.Combine(dataDir, "bpm.txt");
    StreamWriter? logFile = null;
    if (log)
    {
        var logPath = Path.Combine(dataDir, "bpm-log.csv");
        var newLog = !File.Exists(logPath);
        logFile = new StreamWriter(logPath, append: true) { AutoFlush = true };
        if (newLog) await logFile.WriteLineAsync("time;artist;title;status;bpm;confidence;level_db;shown");
        if (console) Console.WriteLine($"Protokoll: {logPath}");
    }

    GlobalSystemMediaTransportControlsSessionManager? media = null;
    try { media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
    catch (Exception ex) { if (console) Console.WriteLine($"Medien-Steuerung nicht erreichbar ({ex.Message})."); }

    using var capture = new LoopbackCapture();
    var stable = new Stabilizer();
    var track = "";
    string? written = null;
    if (console) Console.WriteLine($"Schreibt nach {valuePath}. Beenden mit Strg+C.");

    while (!stop.WaitOne(1000))
    {
        // Follow the default output device (headphones plugged in, another screen).
        if (capture.Ensure() is { } device && console) Console.WriteLine($"Höre auf „{device}“.");

        string artist = "", title = "", status = "";
        if (media is not null)
        {
            try
            {
                var session = media.GetCurrentSession();
                if (session is not null)
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    artist = props.Artist ?? "";
                    title = props.Title ?? "";
                    status = session.GetPlaybackInfo().PlaybackStatus.ToString();
                }
            }
            catch { /* the player went away for a moment */ }
        }

        // A new track: measure it from its start, without the end of the previous one.
        var key = artist + "\u0001" + title;
        if (key != track)
        {
            track = key;
            capture.Reset();
            stable.Reset();
            if (console && title.Length > 0) Console.WriteLine($"\n♪ {artist} – {title}");
        }

        // Paused, stopped or silent for two seconds: nothing to show.
        var playing = status.Length == 0 || status == "Playing";
        var e = playing ? capture.Estimate() : null;
        if (!playing) capture.Reset();
        stable.Add(e);

        var shown = stable.Value is { } v ? Math.Round(v).ToString("0") : "";
        if (console) Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Show(e)}  angezeigt: {(shown.Length > 0 ? shown : "-")}");
        if (logFile is not null)
            await logFile.WriteLineAsync(string.Join(';',
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Csv(artist), Csv(title), status,
                e is null ? "" : e.Bpm.ToString("0.00"), e is null ? "" : e.Confidence.ToString("0.00"),
                e is null ? "" : (20 * Math.Log10(e.Level)).ToString("0"), shown));
        if (shown != written && Write(valuePath, shown)) written = shown;
    }

    Write(valuePath, "");
    logFile?.Dispose();
    return 0;
}

// Writes the value in one step (a new file moved over the old one), so that a reader never sees half of it. False when
// the file is busy; the next second tries again.
static bool Write(string path, string value)
{
    try
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, value);
        File.Move(tmp, path, overwrite: true);
        return true;
    }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
}

static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

[DllImport("kernel32.dll")]
static extern bool AttachConsole(int processId);

[DllImport("kernel32.dll")]
static extern bool AllocConsole();
