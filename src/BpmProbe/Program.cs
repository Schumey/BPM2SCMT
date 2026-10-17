// BpmProbe: measures the tempo of what this PC plays, as a test for a BPM display in SCMT.
//
//   BpmProbe              listens to the default output device (WASAPI loopback) and shows the tempo every second.
//                         Writes bpm-log.csv (one line per second) and bpm.txt (the shown value) next to the program.
//   BpmProbe <audio file> measures a file (mp3, wav, m4a ...) and prints the tempo second by second.
//
// The track comes from Windows' media controls (the same source HASS.Agent uses); a new track starts the measurement
// again.

using System.Globalization;
using BpmProbe;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Media.Control;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = System.Text.Encoding.UTF8;

return args.Length > 0 ? AnalyzeFile(args[0]) : await Live();

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

static async Task<int> Live()
{
    using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    using var capture = new WasapiLoopbackCapture(device);
    var format = capture.WaveFormat;
    var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                  (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
    if (!isFloat && format.BitsPerSample != 16)
    {
        Console.Error.WriteLine($"Format {format} wird nicht unterstützt.");
        return 1;
    }

    var channels = format.Channels;
    var detector = new TempoDetector(format.SampleRate);
    var gate = new object();
    var lastAudio = DateTime.MinValue;
    var interleaved = Array.Empty<float>();
    var mono = Array.Empty<float>();

    capture.DataAvailable += (_, a) =>
    {
        var count = isFloat ? a.BytesRecorded / 4 : a.BytesRecorded / 2;
        if (interleaved.Length < count) interleaved = new float[count];
        if (isFloat)
            Buffer.BlockCopy(a.Buffer, 0, interleaved, 0, count * 4);
        else
            for (var i = 0; i < count; i++) interleaved[i] = BitConverter.ToInt16(a.Buffer, i * 2) / 32768f;
        var frames = count / channels;
        if (mono.Length < frames) mono = new float[frames];
        Mix(interleaved.AsSpan(0, frames * channels), channels, mono);
        lock (gate)
        {
            detector.Add(mono.AsSpan(0, frames));
            lastAudio = DateTime.UtcNow;
        }
    };

    var dir = AppContext.BaseDirectory;
    var logPath = Path.Combine(dir, "bpm-log.csv");
    var valuePath = Path.Combine(dir, "bpm.txt");
    var newLog = !File.Exists(logPath);
    await using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
    if (newLog) await log.WriteLineAsync("time;artist;title;status;bpm;confidence;level_db;shown");

    GlobalSystemMediaTransportControlsSessionManager? media = null;
    try { media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
    catch (Exception ex) { Console.WriteLine($"Medien-Steuerung nicht erreichbar ({ex.Message}), Titel bleiben leer."); }

    var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, a) => { a.Cancel = true; stop.Cancel(); };
    capture.StartRecording();
    Console.WriteLine($"Höre auf „{device.FriendlyName}“ ({format.SampleRate} Hz). Beenden mit Strg+C.");
    Console.WriteLine($"Protokoll: {logPath}");

    var stable = new Stabilizer();
    var track = "";
    string? shownBefore = null;
    while (!stop.IsCancellationRequested)
    {
        try { await Task.Delay(1000, stop.Token); } catch (OperationCanceledException) { break; }

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

        TempoEstimate? e;
        lock (gate)
        {
            var key = artist + "\u0001" + title;
            if (key != track)
            {
                // A new track: measure it from its start, without the end of the previous one.
                track = key;
                detector.Reset();
                stable.Reset();
                if (title.Length > 0) Console.WriteLine($"\n♪ {artist} – {title}");
            }
            // No audio for two seconds (paused, nothing plays): nothing to show.
            e = DateTime.UtcNow - lastAudio > TimeSpan.FromSeconds(2) ? null : detector.Estimate();
            if (e is null && DateTime.UtcNow - lastAudio > TimeSpan.FromSeconds(2)) detector.Reset();
        }
        stable.Add(e);

        var shown = stable.Value is { } v ? Math.Round(v).ToString("0") : "";
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {Show(e)}  angezeigt: {(shown.Length > 0 ? shown : "-")}");
        await log.WriteLineAsync(string.Join(';',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Csv(artist), Csv(title), status,
            e is null ? "" : e.Bpm.ToString("0.00"), e is null ? "" : e.Confidence.ToString("0.00"),
            e is null ? "" : (20 * Math.Log10(e.Level)).ToString("0"), shown));
        if (shown != shownBefore)
        {
            try { await File.WriteAllTextAsync(valuePath, shown); } catch (IOException) { }
            shownBefore = shown;
        }
    }

    capture.StopRecording();
    return 0;
}

static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
