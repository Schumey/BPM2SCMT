using NAudio.Dsp;

namespace BpmProbe;

/// <summary>One estimate: the tempo, how clearly it stands out (0 to 1), and how loud the audio was.</summary>
internal sealed record TempoEstimate(double Bpm, double Confidence, double Level);

/// <summary>
/// Finds the tempo of mono audio. Every hop of 256 samples gives one value of an onset curve (how much louder the
/// spectrum got, the bass counted three times, as the kick carries the beat in electronic music). The tempo is the
/// beat length at which the curve repeats best: its autocorrelation, summed over the first eight beats, so that a
/// small error in the beat length adds up and the estimate gets precise to about 0.1 BPM.
/// </summary>
internal sealed class TempoDetector
{
    private const int FftSize = 1024;
    private const int FftLog2 = 10;
    private const int Hop = 256;
    private const double WindowSeconds = 12.0;   // how much of the onset curve is kept
    private const double MinSeconds = 6.0;       // before that, no estimate
    private const double BassHz = 200.0;
    private const double SilenceRms = 0.001;     // about -60 dBFS

    public const double MinBpm = 85.0;
    public const double MaxBpm = 180.0;

    private readonly int _sampleRate;
    private readonly double _fps;
    private readonly float[] _hann = new float[FftSize];
    private readonly float[] _frame = new float[FftSize];
    private int _frameFill;
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _prevLog = new float[FftSize / 2];
    private readonly float[] _weight = new float[FftSize / 2];
    private readonly float[] _onset;
    private readonly float[] _rms;   // the RMS of each hop, for the level
    private int _count;              // values written to the ring since the last reset
    private bool _first = true;

    public TempoDetector(int sampleRate)
    {
        _sampleRate = sampleRate;
        _fps = (double)sampleRate / Hop;
        for (var i = 0; i < FftSize; i++)
            _hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        for (var i = 0; i < FftSize / 2; i++)
        {
            var hz = (double)i * sampleRate / FftSize;
            _weight[i] = i == 0 ? 0f : hz <= BassHz ? 3f : hz <= 8000 ? 1f : 0f;
        }
        var len = (int)(WindowSeconds * _fps);
        _onset = new float[len];
        _rms = new float[len];
    }

    public double FramesPerSecond => _fps;

    public void Reset()
    {
        _frameFill = 0;
        _count = 0;
        _first = true;
        Array.Clear(_prevLog);
    }

    public void Add(ReadOnlySpan<float> mono)
    {
        while (!mono.IsEmpty)
        {
            var n = Math.Min(FftSize - _frameFill, mono.Length);
            mono[..n].CopyTo(_frame.AsSpan(_frameFill));
            _frameFill += n;
            mono = mono[n..];
            if (_frameFill < FftSize) continue;
            ProcessFrame();
            Array.Copy(_frame, Hop, _frame, 0, FftSize - Hop);
            _frameFill = FftSize - Hop;
        }
    }

    private void ProcessFrame()
    {
        double sq = 0;
        for (var i = FftSize - Hop; i < FftSize; i++) sq += _frame[i] * _frame[i];
        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = _frame[i] * _hann[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog2, _fft);

        double flux = 0;
        for (var i = 1; i < FftSize / 2; i++)
        {
            var mag = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
            var lg = (float)Math.Log(1 + 1000 * mag);
            var d = lg - _prevLog[i];
            if (d > 0) flux += d * _weight[i];
            _prevLog[i] = lg;
        }
        if (_first)
        {
            flux = 0;   // the first frame has nothing to compare with
            _first = false;
        }

        var pos = _count % _onset.Length;
        _onset[pos] = (float)flux;
        _rms[pos] = (float)Math.Sqrt(sq / Hop);
        _count++;
    }

    /// <summary>The tempo of the last twelve seconds; null while there are fewer than six or the audio is silent.</summary>
    public TempoEstimate? Estimate()
    {
        var n = Math.Min(_count, _onset.Length);
        if (n < MinSeconds * _fps) return null;

        // The curve in order, oldest first.
        var o = new double[n];
        var start = _count - n;
        double level = 0;
        for (var i = 0; i < n; i++)
        {
            var p = (start + i) % _onset.Length;
            o[i] = _onset[p];
            level += _rms[p] * _rms[p];
        }
        level = Math.Sqrt(level / n);
        if (level < SilenceRms) return null;

        // Minus its local mean (a quarter second), only what rises above it.
        var w = Math.Max(1, (int)(_fps * 0.25));
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++) prefix[i + 1] = prefix[i] + o[i];
        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = Math.Max(0, i - w);
            var b = Math.Min(n, i + w + 1);
            x[i] = Math.Max(0, o[i] - (prefix[b] - prefix[a]) / (b - a));
        }

        // Autocorrelation up to where a lag still overlaps 40 % of the curve.
        var maxLag = (int)(n * 0.6);
        var ac = new double[maxLag + 2];
        for (var lag = 0; lag < ac.Length; lag++)
        {
            double s = 0;
            for (var i = lag; i < n; i++) s += x[i] * x[i - lag];
            ac[lag] = s / (n - lag);
        }
        if (ac[0] <= 0) return null;
        for (var lag = ac.Length - 1; lag >= 0; lag--) ac[lag] /= ac[0];

        // Every tempo from MinBpm to MaxBpm in steps of 0.05: the mean of the autocorrelation at one to eight beats.
        const double step = 0.05;
        var steps = (int)Math.Round((MaxBpm - MinBpm) / step) + 1;
        var score = new double[steps];
        for (var s = 0; s < steps; s++)
        {
            var bpm = MinBpm + s * step;
            var beat = 60.0 * _fps / bpm;
            var beats = Math.Min(8, (int)(maxLag / beat));
            double sum = 0;
            for (var k = 1; k <= beats; k++) sum += Interp(ac, k * beat);
            // A light preference for the usual range, against a half or double tempo at the edges.
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 130.0), 2));
            score[s] = beats > 0 ? sum / beats * prior : 0;
        }

        var best = 0;
        for (var s = 1; s < steps; s++) if (score[s] > score[best]) best = s;
        var bestBpm = MinBpm + best * step;
        if (best > 0 && best < steps - 1)
        {
            var (l, c, r) = (score[best - 1], score[best], score[best + 1]);
            var den = l - 2 * c + r;
            if (den < 0) bestBpm += 0.5 * (l - r) / den * step;
        }

        var sorted = (double[])score.Clone();
        Array.Sort(sorted);
        var median = sorted[steps / 2];
        var confidence = score[best] > 0 ? Math.Clamp((score[best] - median) / score[best], 0, 1) : 0;
        return new TempoEstimate(bestBpm, confidence, level);
    }

    private static double Interp(double[] a, double pos)
    {
        var i = (int)pos;
        if (i + 1 >= a.Length) return 0;
        var f = pos - i;
        return a[i] * (1 - f) + a[i + 1] * f;
    }
}

/// <summary>
/// Turns the estimates of every second into a value to show: the median of the last six, once at least four of them
/// lie within 1 BPM of it. Silence clears it.
/// </summary>
internal sealed class Stabilizer
{
    public const double MinConfidence = 0.2;
    private readonly List<double> _recent = [];

    public double? Value { get; private set; }

    public void Reset()
    {
        _recent.Clear();
        Value = null;
    }

    public void Add(TempoEstimate? e)
    {
        if (e is null)
        {
            Reset();
            return;
        }
        if (e.Confidence < MinConfidence) return;
        _recent.Add(e.Bpm);
        if (_recent.Count > 6) _recent.RemoveAt(0);
        if (_recent.Count < 4) return;
        var sorted = _recent.Order().ToArray();
        var median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        if (_recent.Count(b => Math.Abs(b - median) <= 1.0) >= 4) Value = median;
    }
}
