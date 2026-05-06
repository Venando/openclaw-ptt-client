namespace OpenClawPTT.Services;

/// <summary>
/// Summarizes text for TTS using Direct LLM.
/// </summary>
public interface ITtsSummarizer : IDisposable
{
    /// <summary>
    /// Summarizes text for TTS output.
    /// </summary>
    /// <param name="text">Text to summarize</param>
    /// <param name="config">App config with summarization settings</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Summarized text ready for TTS</returns>
    Task<string> SummarizeForTtsAsync(string text, AppConfig config, CancellationToken ct = default);
}

/// <summary>
/// Uses Direct LLM to summarize content for TTS.
/// </summary>
public sealed class TtsSummarizer : ITtsSummarizer, IDisposable
{
    private readonly IDirectLlmService? _directLlm;
    private bool _disposed;

    public TtsSummarizer(IDirectLlmService? directLlm)
    {
        _directLlm = directLlm;
    }

    public async Task<string> SummarizeForTtsAsync(string text, AppConfig config, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TtsSummarizer));
        if (_directLlm == null || !_directLlm.IsConfigured)
            throw new InvalidOperationException("Direct LLM not configured");
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (config.TtsMaxChars <= 0)
            throw new ArgumentException("TtsMaxChars must be greater than 0", nameof(config));

        var prompt = BuildSummarizationPrompt(text, config);
        var summary = await _directLlm.SendAsync(prompt, ct);

        // Guard against empty/no-response from LLM
        if (string.IsNullOrWhiteSpace(summary) || summary == "(No response)")
            throw new InvalidOperationException("LLM returned no usable content for summarization");

        return summary.Trim();
    }

    private static string BuildSummarizationPrompt(string text, AppConfig config)
    {
        var codeBlockInstruction = config.TtsCodeBlockMode.ToLowerInvariant() switch
        {
            "summarize" => "For code blocks: describe what the code does in one sentence",
            "skip" => "For code blocks: replace with '[Code block]' or mention the filename if present",
            "smart" => "For code blocks: small snippets (<5 lines) describe key logic; large blocks summarize in one sentence",
            _ => "For code blocks: describe what the code does"
        };

        return $@"Summarize the following text for text-to-speech output. Strip all markdown formatting, remove URLs, keep the tone conversational, and output only the summarized text.

{codeBlockInstruction}

Text to summarize:
{text}";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            (_directLlm as IDisposable)?.Dispose();
        }
    }
}
