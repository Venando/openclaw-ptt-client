using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Adapter for OpenAI Whisper API transcription.
/// </summary>
public sealed class OpenAiTranscriberAdapter : ITranscriber
{
    private const string OpenAiApiUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string DefaultModel = "whisper-1";

    private readonly HttpClient _http;
    private readonly string _model;
    private bool _disposed;

    public OpenAiTranscriberAdapter(string apiKey, string model = DefaultModel)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "OpenAI API key must not be null or empty.");

        _model = model ?? DefaultModel;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        using var content = new MultipartFormDataContent();
        using var audioStream = new MemoryStream(wavBytes);

        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent("text"), "response_format");

        var response = await _http.PostAsync(OpenAiApiUrl, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new TranscriberException($"OpenAI API returned HTTP {(int)response.StatusCode}: {body}");
        }

        return ExtractText(body);
    }

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

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
