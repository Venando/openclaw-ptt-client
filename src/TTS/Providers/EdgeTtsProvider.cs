namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Microsoft Edge TTS provider
/// Uses the edge-tts npm package or direct synthesis
/// </summary>
public sealed class EdgeTtsProvider : ITextToSpeech
{
    private readonly string? _subscriptionKey;
    private readonly string? _region;

    public string ProviderName => "Edge TTS";

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
        _subscriptionKey = subscriptionKey;
        _region = region;
    }

    public Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = voice ?? "en-US-AriaNeural";

        if (string.IsNullOrEmpty(_subscriptionKey))
        {
            throw new InvalidOperationException(
                "Edge TTS requires an Azure subscription key. " +
                "Set the 'TtsSubscriptionKey' configuration option.");
        }

        if (!AvailableVoices.Contains(selectedVoice, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid voice '{selectedVoice}'. Available: {string.Join(", ", AvailableVoices)}");
        }

        // Note: Full implementation would use edge-tts npm package or direct WebSocket
        // For now, this is a placeholder that requires external tooling
        throw new NotImplementedException(
            "Edge TTS requires the edge-tts npm package to be installed. " +
            "Install with: npm install -g edge-tts " +
            "Then use the edge-tts CLI or wrapper library.");
    }
}
