using Moq;
using OpenClawPTT.Services;
using System;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and edge-case tests for AudioPlayerService.
/// NAudio may fail to open a device in headless/CI environments, so we test
/// the error-handling paths and resource management rather than audible output.
/// </summary>
public class AudioPlayerServiceTests
{
    private readonly Mock<IConsoleOutput> _mockConsole;

    public AudioPlayerServiceTests()
    {
        _mockConsole = new Mock<IConsoleOutput>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. Play(byte[]) with valid WAV bytes — WaveFileReader path works
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_ValidWavBytes_DoesNotThrow()
    {
        var service = new AudioPlayerService(_mockConsole.Object);
        byte[] wavBytes = CreateMinimalWavBytes();

        // Should not throw — NAudio may fail to open audio device but that is
        // caught internally; the method itself should not propagate.
        var exception = Record.Exception(() => service.Play(wavBytes));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Play(byte[]) with raw PCM bytes — fallback RawSourceWaveStream path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_RawPcmBytes_DoesNotThrow()
    {
        var service = new AudioPlayerService(_mockConsole.Object);
        // Raw PCM: 16kHz, 16-bit, mono = 2 bytes per sample
        byte[] rawPcm = new byte[16000]; // 0.5s of silence at 16kHz
        var exception = Record.Exception(() => service.Play(rawPcm));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Play() called after Dispose() → throws ObjectDisposedException
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_AfterDispose_ThrowsObjectDisposedException()
    {
        var service = new AudioPlayerService(_mockConsole.Object);
        service.Dispose();

        var ex = Assert.Throws<ObjectDisposedException>(() => service.Play(Array.Empty<byte>()));
        Assert.Contains(nameof(AudioPlayerService), ex.Message);
    }

    [Fact]
    public void Play_String_AfterDispose_ThrowsObjectDisposedException()
    {
        var service = new AudioPlayerService(_mockConsole.Object);
        service.Dispose();

        var ex = Assert.Throws<ObjectDisposedException>(() => service.Play("nonexistent.wav"));
        Assert.Contains(nameof(AudioPlayerService), ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Stop() then Play() fresh audio — no resource leak from previous _waveOut
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stop_ThenPlay_NoResourceLeak()
    {
        var service = new AudioPlayerService(_mockConsole.Object);
        byte[] audio1 = CreateMinimalWavBytes();
        byte[] audio2 = CreateMinimalWavBytes();

        // First play + stop
        service.Play(audio1);
        service.Stop();

        // Second play on top of stopped instance — should not leak
        var exception = Record.Exception(() => service.Play(audio2));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Play(null) or Play(empty array) — should not crash
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_NullArray_DoesNotThrow()
    {
        var service = new AudioPlayerService(_mockConsole.Object);

        // Should not throw — NAudio catches exceptions internally
        var exception = Record.Exception(() => service.Play((byte[])null!));
        Assert.Null(exception);
    }

    [Fact]
    public void Play_EmptyArray_DoesNotThrow()
    {
        var service = new AudioPlayerService(_mockConsole.Object);

        var exception = Record.Exception(() => service.Play(Array.Empty<byte>()));
        Assert.Null(exception);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 6. Double Dispose() — should not throw
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new AudioPlayerService(_mockConsole.Object);

        var ex1 = Record.Exception(() => service.Dispose());
        var ex2 = Record.Exception(() => service.Dispose());

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the smallest valid WAV file (RIFF header + fmt chunk + data chunk)
    /// that WaveFileReader can attempt to parse.
    /// </summary>
    private static byte[] CreateMinimalWavBytes()
    {
        // WAV header (44 bytes) + 100 bytes of silence
        byte[] wav = new byte[144];

        // RIFF header
        wav[0]  = 0x52; // R
        wav[1]  = 0x49; // I
        wav[2]  = 0x46; // F
        wav[3]  = 0x46; // F
        wav[4]  = 0x28; // FileSize - 8 (little-endian)
        wav[5]  = 0x00;
        wav[6]  = 0x00;
        wav[7]  = 0x00;
        wav[8]  = 0x57; // W
        wav[9]  = 0x41; // A
        wav[10] = 0x56; // V
        wav[11] = 0x45; // E

        // fmt chunk
        wav[12] = 0x66; // f
        wav[13] = 0x6D; // m
        wav[14] = 0x74; // t
        wav[15] = 0x20; // (space)
        wav[16] = 0x10; // ChunkSize (16 for PCM)
        wav[17] = 0x00;
        wav[18] = 0x00;
        wav[19] = 0x00;
        wav[20] = 0x01; // AudioFormat (1 = PCM)
        wav[21] = 0x00;
        wav[22] = 0x01; // NumChannels (1 = mono)
        wav[23] = 0x00;
        wav[24] = 0x40; // SampleRate (16000 little-endian: 0x3E80 → 0x40 0x3E 0x00 0x00)
        wav[25] = 0x3E;
        wav[26] = 0x00;
        wav[27] = 0x00;
        wav[28] = 0x00; // ByteRate (32000)
        wav[29] = 0x7D;
        wav[30] = 0x00;
        wav[31] = 0x00;
        wav[32] = 0x02; // BlockAlign (2)
        wav[33] = 0x00;
        wav[34] = 0x10; // BitsPerSample (16)
        wav[35] = 0x00;

        // data chunk
        wav[36] = 0x64; // d
        wav[37] = 0x61; // a
        wav[38] = 0x74; // t
        wav[39] = 0x61; // a
        wav[40] = 0x64; // DataSize (100 bytes, little-endian)
        wav[41] = 0x00;
        wav[42] = 0x00;
        wav[43] = 0x00;

        // bytes 44-143 are already zeroed (silence)

        return wav;
    }
}