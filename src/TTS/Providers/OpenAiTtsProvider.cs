using System.Net.Http.Json;
using System.Runtime.CompilerServices;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// OpenAI TTS provider (tts-1, tts-1-hd)
/// </summary>
public sealed class OpenAiTtsProvider : ITextToSpeech
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public string ProviderName => "OpenAI";

    // https://platform.openai.com/docs/guides/text-to-speech
    public IReadOnlyList<string> AvailableVoices { get; } = new[]
    {
        "alloy", "echo", "fable", "onyx", "nova", "shimmer"
    };

    public IReadOnlyList<string> AvailableModels { get; } = new[]
    {
        "tts-1", "tts-1-hd"
    };

    public OpenAiTtsProvider(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = voice ?? "alloy";
        var selectedModel = model ?? "tts-1";

        // Validate voice
        if (!AvailableVoices.Contains(selectedVoice, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid voice '{selectedVoice}'. Available: {string.Join(", ", AvailableVoices)}");
        }

        // Validate model
        if (!AvailableModels.Contains(selectedModel, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid model '{selectedModel}'. Available: {string.Join(", ", AvailableModels)}");
        }

        var request = new
        {
            model = selectedModel,
            voice = selectedVoice,
            input = text,
            response_format = "wav"
        };

        var response = await _http.PostAsJsonAsync(
            "https://api.openai.com/v1/audio/speech",
            request,
            ct);

        response.EnsureSuccessStatusCode();

        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        return audio;
    }
}
