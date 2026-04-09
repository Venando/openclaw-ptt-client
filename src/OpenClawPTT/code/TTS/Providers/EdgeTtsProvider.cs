using System.Net.Http;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Microsoft Azure Cognitive Services TTS provider.
/// Uses the Azure Speech REST API directly.
/// </summary>
public sealed class EdgeTtsProvider : ITextToSpeech
{
    private readonly HttpClient _http;
    private readonly string _subscriptionKey;
    private readonly string _region;

    public string ProviderName => "Azure TTS";

    // https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech
    public IReadOnlyList<string> AvailableVoices { get; } = new[]
    {
        "en-US-AriaNeural", "en-US-GuyNeural", "en-US-JennyNeural", "en-US-SaraNeural",
        "en-US-EmmaNeural", "en-US-RogerNeural", "en-US-AshleyNeural", "en-US-CoreyNeural",
        "en-GB-SoniaNeural", "en-GB-RyanNeural",
        "de-DE-KatjaNeural", "de-DE-ConradNeural",
        "fr-FR-DeniseNeural", "fr-FR-HenriNeural",
        "es-ES-ElviraNeural", "es-MX-DaliaNeural"
    };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public EdgeTtsProvider(string? subscriptionKey = null, string region = "eastus")
    {
        if (string.IsNullOrEmpty(subscriptionKey))
        {
            throw new InvalidOperationException(
                "Azure TTS requires a subscription key. " +
                "Set the 'TtsSubscriptionKey' configuration option.");
        }

        _subscriptionKey = subscriptionKey;
        _region = region;
        _http = new HttpClient();
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = voice ?? "en-US-AriaNeural";

        if (!AvailableVoices.Contains(selectedVoice, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid voice '{selectedVoice}'. Available: {string.Join(", ", AvailableVoices)}");
        }

        var ssml = $@"<speak version='1.0' xmlns='https://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
            <voice name='{selectedVoice}'>{EscapeSsml(text)}</voice>
        </speak>";

        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1");
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-16bit-mono-wav");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string EscapeSsml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}