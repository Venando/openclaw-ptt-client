using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClawPTT;

/// <summary>
/// Pure handler for all inbound session message events.
/// Returns structured data — does not fire events.
/// </summary>
public sealed class SessionMessageHandler
{
    private readonly bool _realTimeReplyOutput;
    private readonly bool _debugToolCalls;
    private readonly Func<string, object?, CancellationToken, TimeSpan, Task<JsonElement>>? _sendRequestAsync;

    public SessionMessageHandler(
        AppConfig cfg,
        Func<string, object?, CancellationToken, TimeSpan, Task<JsonElement>>? sendRequestAsync = null)
    {
        _realTimeReplyOutput = cfg.RealTimeReplyOutput;
        _debugToolCalls = cfg.DebugToolCalls;
        _sendRequestAsync = sendRequestAsync;
    }

    public sealed record class SessionMessageResult(
        bool HasAudio,
        bool HasText,
        string AudioText,
        string TextContent,
        string? Thinking,
        string? ToolCallName,
        string? ToolCallArgs
    );

    // ─── public handlers ───────────────────────────────────────────

    public SessionMessageResult HandleSessionMessage(JsonElement payload)
    {
        if (!payload.TryGetProperty("message", out var messageEl)) return default;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return default;
        if (roleEl.GetString() != "assistant") return default;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return default;
        if (contentEl.ValueKind != JsonValueKind.Array) return default;

        bool emitDelta = !_realTimeReplyOutput;
        bool startFired = false;
        bool hasAudio = false, hasText = false;
        string audioText = "", textContent = "";
        string? thinking = null, toolCallName = null, toolCallArgs = null;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var type = typeEl.GetString();

            if (type == "thinking" && block.TryGetProperty("thinking", out var thinkingEl))
            {
                thinking = thinkingEl.GetString() ?? string.Empty;
            }
            else if (type == "toolCall" && block.TryGetProperty("name", out var nameEl) && block.TryGetProperty("arguments", out var argsEl))
            {
                toolCallName = nameEl.GetString() ?? string.Empty;
                toolCallArgs = argsEl.GetRawText();
                if (_debugToolCalls) Console.WriteLine($"[DEBUG] ToolCall: {toolCallName}({toolCallArgs})");
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    var (hA, hT, aT, tC) = ExtractMarkedContent(text);
                    if (hA) { hasAudio = true; audioText = aT; }
                    if (hT) { hasText = true; textContent = tC; }
                    else if (hA) { hasText = true; textContent = StripAudioTags(text); }
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                audioText = audioEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(audioText)) hasAudio = true;
            }
        }

        return new SessionMessageResult(hasAudio, hasText, audioText, textContent, thinking, toolCallName, toolCallArgs);
    }

    public SessionMessageResult HandleAgentStream(JsonElement payload)
    {
        if (_realTimeReplyOutput && payload.TryGetProperty("data", out var data))
        {
            bool hasAudio = data.TryGetProperty("delta", out var delta) && !string.IsNullOrEmpty(delta.GetString());
            return new SessionMessageResult(hasAudio, false, delta.GetString() ?? "", "", null, null, null);
        }
        return default;
    }

    public SessionMessageResult HandleChatFinal(JsonElement payload)
    {
        if (!payload.TryGetProperty("state", out var state)) return default;
        if (state.GetString() != "final") return default;
        if (_realTimeReplyOutput) return default;

        if (!payload.TryGetProperty("message", out var messageEl)) return default;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return default;

        var text = ExtractFullText(contentEl);
        return new SessionMessageResult(false, !string.IsNullOrEmpty(text), "", text, null, null, null);
    }

    public Task HandleApprovalRequest(JsonElement payload)
    {
        if (_sendRequestAsync == null) return Task.CompletedTask;

        if (payload.TryGetProperty("id", out var idEl))
        {
            return Task.Run(async () =>
            {
                try
                {
                    await _sendRequestAsync("exec.approval.resolve", new Dictionary<string, object?>
                    {
                        ["id"] = idEl.GetString(),
                        ["approved"] = true
                    }, CancellationToken.None, TimeSpan.FromSeconds(10));
                }
                catch { /* swallow */ }
            });
        }
        return Task.CompletedTask;
    }

    // ─── static helpers ───────────────────────────────────────────

    public static string StripAudioTags(string text)
        => Regex.Replace(text, @"\[audio\](.*?)\[/audio\]", "$1", RegexOptions.Singleline).Trim();

    public static (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage)
    {
        var audioText = string.Empty;
        var textContent = string.Empty;

        var audioMatch = Regex.Match(fullMessage, @"\[audio\](.*?)\[/audio\]", RegexOptions.Singleline);
        if (audioMatch.Success)
            audioText = audioMatch.Groups[1].Value.Trim();
        else
        {
            var openTagIndex = fullMessage.IndexOf("[audio]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
                audioText = fullMessage.Substring(openTagIndex + 7).Trim();
        }

        var textMatch = Regex.Match(fullMessage, @"\[text\](.*?)\[/text\]", RegexOptions.Singleline);
        if (textMatch.Success)
            textContent = textMatch.Groups[1].Value.Trim();
        else
        {
            var openTagIndex = fullMessage.IndexOf("[text]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
                textContent = fullMessage.Substring(openTagIndex + 6).Trim();
        }

        if (string.IsNullOrEmpty(audioText) && string.IsNullOrEmpty(textContent) && !string.IsNullOrEmpty(fullMessage))
            textContent = fullMessage;

        return (!string.IsNullOrEmpty(audioText), !string.IsNullOrEmpty(textContent), audioText, textContent);
    }

    private static string ExtractFullText(JsonElement contentElement)
    {
        if (contentElement.ValueKind != JsonValueKind.Array) return string.Empty;
        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "text" &&
                item.TryGetProperty("text", out var textElement))
                textParts.Add(textElement.GetString() ?? string.Empty);
        }
        return string.Join("", textParts);
    }
}