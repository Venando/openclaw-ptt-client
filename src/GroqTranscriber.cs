using System.Net.Http.Headers;
using System.Text.Json;

/// <summary>
/// A self-contained module for transcribing audio using the Groq Whisper API.
/// Initialize with your Groq API key, then call TranscribeAsync() with raw WAV bytes.
/// </summary>
public sealed class GroqTranscriber : IDisposable
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const string GroqApiUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string DefaultModel = "whisper-large-v3-turbo";

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------
    private readonly HttpClient _http;
    private readonly string _model;
    private bool _disposed;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new <see cref="GroqTranscriber"/> instance.
    /// </summary>
    /// <param name="apiKey">
    ///   Your Groq API key (e.g. "gsk_…").
    ///   Throws <see cref="ArgumentNullException"/> when null or whitespace.
    /// </param>
    /// <param name="model">
    ///   Whisper model to use. Defaults to <c>whisper-large-v3-turbo</c>.
    /// </param>
    public GroqTranscriber(string apiKey, string model = DefaultModel)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "Groq API key must not be null or empty.");

        _model = model ?? DefaultModel;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transcribes the supplied WAV audio bytes using the Groq Whisper API.
    /// </summary>
    /// <param name="wavBytes">Raw bytes of a valid WAV file.</param>
    /// <param name="fileName">
    ///   Virtual file name sent to the API. Must end in a supported extension
    ///   (.wav, .mp3, .flac, …). Defaults to <c>"audio.wav"</c>.
    /// </param>
    /// <returns>
    ///   The transcription text, or <see cref="string.Empty"/> when Groq
    ///   returned no speech.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///   Thrown when <paramref name="wavBytes"/> is null or empty.
    /// </exception>
    /// <exception cref="GroqTranscriberException">
    ///   Thrown on any HTTP or JSON error from the Groq API.
    /// </exception>
    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav")
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        ThrowIfDisposed();

        using var content = new MultipartFormDataContent();
        using var audioStream = new MemoryStream(wavBytes);

        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent("text"), "response_format");  // plain text response

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(GroqApiUrl, content).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GroqTranscriberException("Network error while contacting Groq API.", ex);
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new GroqTranscriberException(
                $"Groq API returned HTTP {(int)response.StatusCode}: {body}");
        }

        // When response_format=text the body IS the plain transcription string.
        // When the API wraps it in JSON ({"text":"…"}), we handle that too.
        return ExtractText(body);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Handles both a bare plain-text body and a JSON <c>{"text":"…"}</c> body.
    /// </summary>
    private static string ExtractText(string body)
    {
        body = body.Trim();

        if (body.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("text", out var textProp))
                    return textProp.GetString()?.Trim() ?? string.Empty;
            }
            catch (JsonException)
            {
                // Fall through — return raw body
            }
        }

        return body;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GroqTranscriber));
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}

// ---------------------------------------------------------------------------
// Exception type
// ---------------------------------------------------------------------------

/// <summary>
/// Thrown when the Groq transcription API returns an error.
/// </summary>
public sealed class GroqTranscriberException : Exception
{
    public GroqTranscriberException(string message) : base(message) { }
    public GroqTranscriberException(string message, Exception innerException) : base(message, innerException) { }
}