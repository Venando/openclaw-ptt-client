using NAudio.Wave;

namespace OpenClawPTT;

/// <summary>
/// Records microphone audio into WAV (16 kHz mono 16-bit by default).
/// Uses NAudio on Windows.  Falls back to `sox rec` on macOS/Linux.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bits;
    private readonly int _maxSeconds;

    // NAudio path
    private WaveInEvent? _waveIn;
    private MemoryStream? _memStream;
    private WaveFileWriter? _writer;

    // CLI fallback path
    private System.Diagnostics.Process? _recProc;
    private string? _tmpFile;

    private bool _recording;

    public bool IsRecording => _recording;

    public AudioRecorder(int sampleRate = 16_000, int channels = 1, int bits = 16, int maxSeconds = 120)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bits = bits;
        _maxSeconds = maxSeconds;
    }

    // ─── start ──────────────────────────────────────────────────────

    public void StartRecording()
    {
        if (_recording) return;
        _recording = true;

        if (OperatingSystem.IsWindows())
            StartNAudio();
        else
            StartCli();
    }

    private void StartNAudio()
    {
        var fmt = new WaveFormat(_sampleRate, _bits, _channels);
        _memStream = new MemoryStream();
        _writer = new WaveFileWriter(_memStream, fmt);

        _waveIn = new WaveInEvent
        {
            WaveFormat = fmt,
            BufferMilliseconds = 100
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            if (_writer == null) return;
            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            // enforce max duration
            if (_writer.TotalTime.TotalSeconds >= _maxSeconds)
                _waveIn?.StopRecording();
        };

        _waveIn.RecordingStopped += (_, _) => { /* handled in Stop */ };
        _waveIn.StartRecording();
    }

    private void StartCli()
    {
        _tmpFile = Path.Combine(Path.GetTempPath(), $"oc_ptt_{Guid.NewGuid():N}.wav");

        // sox rec: works on macOS (brew install sox) and Linux (apt install sox)
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sox",
            ArgumentList =
            {
                "-d",                               // default audio device
                "-r", _sampleRate.ToString(),
                "-c", _channels.ToString(),
                "-b", _bits.ToString(),
                "-e", "signed-integer",
                "-t", "wav",
                _tmpFile,
                "trim", "0", _maxSeconds.ToString()
            },
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _recProc = System.Diagnostics.Process.Start(psi);
        }
        catch (Exception)
        {
            // sox not found — try arecord (Linux/ALSA)
            psi.FileName = "arecord";
            psi.ArgumentList.Clear();
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add($"S{_bits}_LE");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(_sampleRate.ToString());
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(_channels.ToString());
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("wav");
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(_maxSeconds.ToString());
            psi.ArgumentList.Add(_tmpFile);
            _recProc = System.Diagnostics.Process.Start(psi)
                       ?? throw new InvalidOperationException(
                           "No audio recorder found. Install sox or NAudio (Windows).");
        }
    }

    // ─── stop ───────────────────────────────────────────────────────

    public byte[] StopRecording()
    {
        if (!_recording) return Array.Empty<byte>();
        _recording = false;

        return OperatingSystem.IsWindows()
            ? StopNAudio()
            : StopCli();
    }

    private byte[] StopNAudio()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        // WaveFileWriter.Dispose finalises the RIFF length headers,
        // then MemoryStream.ToArray() still returns the full buffer.
        _writer?.Dispose();
        _writer = null;

        var data = _memStream?.ToArray() ?? Array.Empty<byte>();
        _memStream?.Dispose();
        _memStream = null;

        return data;
    }

    private byte[] StopCli()
    {
        if (_recProc is { HasExited: false })
        {
            // send SIGINT / kill gracefully so sox finalises the WAV header
            try
            {
                _recProc.Kill(entireProcessTree: false);
                _recProc.WaitForExit(3_000);
            }
            catch { /* best effort */ }
        }
        _recProc?.Dispose();
        _recProc = null;

        var data = Array.Empty<byte>();
        if (_tmpFile != null && File.Exists(_tmpFile))
        {
            data = File.ReadAllBytes(_tmpFile);
            try { File.Delete(_tmpFile); } catch { }
        }
        _tmpFile = null;
        return data;
    }

    // ─── dispose ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_recording) StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();
        _memStream?.Dispose();
        _recProc?.Dispose();
    }
}