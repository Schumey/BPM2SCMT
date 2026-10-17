using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BPM2SCMT;

/// <summary>
/// Listens to the default output device (WASAPI loopback) and feeds a <see cref="TempoDetector"/>. Follows a change of
/// the default device and starts again when the device goes away.
/// </summary>
internal sealed class LoopbackCapture : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _gate = new();
    private WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private string _deviceId = "";
    private TempoDetector? _detector;
    private bool _isFloat;
    private int _channels;
    private bool _stopped;
    private DateTime _lastAudio = DateTime.MinValue;
    private DateTime _lastCheck = DateTime.MinValue;
    private float[] _interleaved = [];
    private float[] _mono = [];

    /// <summary>
    /// Starts listening, or again on another device when the default changed (checked every five seconds) or the
    /// capture stopped. Returns the name of the device when it (re)started, else null.
    /// </summary>
    public string? Ensure()
    {
        var now = DateTime.UtcNow;
        if (_capture is not null && !_stopped && now - _lastCheck < TimeSpan.FromSeconds(5)) return null;
        _lastCheck = now;

        MMDevice device;
        try { device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { Stop(); return null; }   // no output device right now
        if (_capture is not null && !_stopped && device.ID == _deviceId)
        {
            device.Dispose();
            return null;
        }

        Stop();
        var capture = new WasapiLoopbackCapture(device);
        var format = capture.WaveFormat;
        _isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                   (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
        if (!_isFloat && format.BitsPerSample != 16)
        {
            capture.Dispose();
            device.Dispose();
            return null;
        }
        _channels = format.Channels;
        lock (_gate) _detector = new TempoDetector(format.SampleRate);
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, _) => _stopped = true;
        _stopped = false;
        _capture = capture;
        _device = device;
        _deviceId = device.ID;
        capture.StartRecording();
        return device.FriendlyName;
    }

    /// <summary>Forgets what was heard so far.</summary>
    public void Reset()
    {
        lock (_gate) _detector?.Reset();
    }

    /// <summary>The tempo of what was heard; null when nothing came for two seconds.</summary>
    public TempoEstimate? Estimate()
    {
        lock (_gate)
        {
            if (_detector is null) return null;
            if (DateTime.UtcNow - _lastAudio > Quiet)
            {
                _detector.Reset();
                return null;
            }
            return _detector.Estimate();
        }
    }

    private void OnData(object? sender, WaveInEventArgs a)
    {
        var count = _isFloat ? a.BytesRecorded / 4 : a.BytesRecorded / 2;
        if (_interleaved.Length < count) _interleaved = new float[count];
        if (_isFloat)
            Buffer.BlockCopy(a.Buffer, 0, _interleaved, 0, count * 4);
        else
            for (var i = 0; i < count; i++) _interleaved[i] = BitConverter.ToInt16(a.Buffer, i * 2) / 32768f;

        var frames = count / _channels;
        if (_mono.Length < frames) _mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float s = 0;
            for (var c = 0; c < _channels; c++) s += _interleaved[f * _channels + c];
            _mono[f] = s / _channels;
        }

        lock (_gate)
        {
            _detector?.Add(_mono.AsSpan(0, frames));
            _lastAudio = DateTime.UtcNow;
        }
    }

    private void Stop()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnData;
            try { _capture.StopRecording(); } catch { /* already gone */ }
            _capture.Dispose();
            _capture = null;
        }
        _device?.Dispose();
        _device = null;
        _deviceId = "";
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }
}
