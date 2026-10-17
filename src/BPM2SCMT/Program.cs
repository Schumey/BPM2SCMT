// BPM2SCMT: measures the tempo of what this PC plays and writes it to C:\ProgramData\BPM2SCMT\bpm.txt, where a
// custom sensor of HASS.Agent reads it for the BPM display of SCMT.
//
//   BPM2SCMT                  runs in the background, without a window (one instance at a time).
//   BPM2SCMT --console        the same with a console that shows the tempo every second, and writes bpm-log.csv.
//   BPM2SCMT --log            in the background, and writes bpm-log.csv.
//   BPM2SCMT --stop           ends the instance that runs.
//   BPM2SCMT --autostart on   starts with Windows (off: no longer).
//   BPM2SCMT <audio file>     measures a file (mp3, wav, m4a ...) and prints the tempo second by second.
//   BPM2SCMT --help           lists these commands; --version prints the version.
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
                Console.WriteLine("BPM2SCMT is stopping.");
            }
            else
            {
                Console.WriteLine("BPM2SCMT is not running.");
            }
            return 0;
        case "--autostart":
            OpenConsole(allocate: false);
            var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (mode is not ("on" or "off")) return Help(2);
            return Autostart(mode == "on");
        case "--version":
            OpenConsole(allocate: false);
            Console.WriteLine($"BPM2SCMT {Version()}");
            return 0;
        case "--help" or "-h" or "-?" or "/?":
            OpenConsole(allocate: false);
            return Help(0);
        default:
            OpenConsole(allocate: true);
            if (first.StartsWith('-')) return Help(2);
            if (!File.Exists(args[0]))
            {
                Console.WriteLine($"No such file: {args[0]}");
                return Help(2);
            }
            return AnalyzeFile(args[0]);
    }
}
catch (Exception ex)
{
    var errorLog = Path.Combine(dataDir, "error.log");
    File.AppendAllText(errorLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Version()} {ex}\r\n");
    try { Console.Error.WriteLine($"Error: {ex.Message} (details in {errorLog})"); }
    catch { /* no console */ }
    return 1;
}

static string Version() => typeof(TempoDetector).Assembly.GetName().Version?.ToString(3) ?? "?";

static int Help(int code)
{
    Console.WriteLine($"""
        BPM2SCMT {Version()}: measures the tempo of what this PC plays and writes it to
        C:\ProgramData\BPM2SCMT\bpm.txt (empty while nothing plays).

          BPM2SCMT                  runs in the background, without a window
          BPM2SCMT --console        the same in a window that shows the tempo every second
          BPM2SCMT --log            in the background, and writes bpm-log.csv
          BPM2SCMT --stop           ends the BPM2SCMT that runs
          BPM2SCMT --autostart on   starts with Windows (off: no longer)
          BPM2SCMT <audio file>     measures a file (mp3, wav, m4a ...)
          BPM2SCMT --version        prints the version

        https://github.com/Schumey/BPM2SCMT
        """);
    return code;
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
        Console.WriteLine($"BPM2SCMT now starts with Windows ({Environment.ProcessPath}).");
    }
    else
    {
        run.DeleteValue("BPM2SCMT", throwOnMissingValue: false);
        Console.WriteLine("BPM2SCMT no longer starts with Windows.");
    }
    return 0;
}

static string Show(TempoEstimate? e) =>
    e is null ? "  -   " : $"{e.Bpm,6:0.0} BPM  confidence {e.Confidence:0.00}  level {20 * Math.Log10(e.Level),5:0} dB";

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
    Console.WriteLine($"{Path.GetFileName(path)} ({rate} Hz, {channels} channels)");
    while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
    {
        Mix(buffer.AsSpan(0, read), channels, mono);
        detector.Add(mono.AsSpan(0, read / channels));
        second++;
        var e = detector.Estimate();
        stable.Add(e);
        Console.WriteLine($"{second,4} s  {Show(e)}  shown: {(stable.Value is { } v ? $"{v:0.0}" : "-")}");
    }
    Console.WriteLine($"Result: {(stable.Value is { } r ? $"{r:0.0} BPM" : "none")}");
    return 0;
}

static async Task<int> Live(string dataDir, bool console, bool log)
{
    using var single = new Mutex(true, @"Local\BPM2SCMT", out var firstInstance);
    if (!firstInstance)
    {
        if (console) Console.WriteLine("BPM2SCMT is already running. End it with --stop first.");
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
        if (console) Console.WriteLine($"Log: {logPath}");
    }

    GlobalSystemMediaTransportControlsSessionManager? media = null;
    try { media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
    catch (Exception ex) { if (console) Console.WriteLine($"Media controls not available ({ex.Message})."); }

    using var capture = new LoopbackCapture();
    var stable = new Stabilizer();
    var track = "";
    string? written = null;
    if (console) Console.WriteLine($"BPM2SCMT {Version()} writes to {valuePath}. Ctrl+C ends it.");

    while (!stop.WaitOne(1000))
    {
        // Follow the default output device (headphones plugged in, another screen).
        if (capture.Ensure() is { } device && console) Console.WriteLine($"Listening to \"{device}\".");

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
        if (console) Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Show(e)}  shown: {(shown.Length > 0 ? shown : "-")}");
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
