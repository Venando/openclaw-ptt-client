using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class SessionMessageHandler
{
    private readonly AppConfig _cfg;
    private readonly IGatewayEventSource _events;

    public SessionMessageHandler(IGatewayEventSource events, AppConfig cfg)
    {
        _cfg = cfg;
        _events = events;
    }

    public void HandleSessionMessage(JsonElement payload)
    {
        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return;
        if (roleEl.GetString() != "assistant") return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;
        if (contentEl.ValueKind != JsonValueKind.Array) return;

        bool startFired = false;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var type = typeEl.GetString();

            if (type == "thinking" && block.TryGetProperty("thinking", out var thinkingEl))
            {
                var thinking = thinkingEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(thinking))
                    _events.RaiseAgentThinking(thinking);
            }
            else if (type == "toolCall" && block.TryGetProperty("name", out var nameEl) && block.TryGetProperty("arguments", out var argsEl))
            {
                var toolName = nameEl.GetString() ?? string.Empty;
                var args = argsEl.GetRawText();
                if (_cfg.DebugToolCalls) Console.WriteLine($"[DEBUG] ToolCall: {toolName}({args})");
                _events.RaiseAgentToolCall(toolName, args);
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    var (hasAudio, hasText, audioText, textContent) = ExtractMarkedContent(text);
                    if (hasAudio)
                        _events.RaiseAgentReplyAudio(audioText);
                    if (!startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    if (hasText)
                        _events.RaiseAgentReplyFull(textContent);
                    else if (hasAudio)
                        _events.RaiseAgentReplyFull(StripAudioTags(text));
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                var audioText = audioEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(audioText))
                {
                    if (!startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    _events.RaiseAgentReplyAudio(audioText);
                }
            }
            else
            {
                // Log unknown block type with its raw JSON
                Console.WriteLine($"[DEBUG] Unknown block type=\"{type}\": {block}");
            }
        }

        if (startFired)
            _events.RaiseAgentReplyDeltaEnd();
    }

    public void HandleAgentStream(JsonElement payload)
    {
        // Only relevant when RealTimeReplyOutput is on (DeepSeek / streaming models)
        if (!_cfg.RealTimeReplyOutput) return;
        if (!payload.TryGetProperty("data", out var data)) return;

        if (data.TryGetProperty("phase", out var phase))
        {
            var phaseType = phase.GetString() ?? string.Empty;
            if (phaseType == "start") _events.RaiseAgentReplyDeltaStart();
            if (phaseType == "end") _events.RaiseAgentReplyDeltaEnd();
        }

        if (data.TryGetProperty("delta", out var delta))
        {
            var chunk = delta.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(chunk))
                _events.RaiseAgentReplyDelta(chunk);
        }
    }

    public void HandleChatFinal(JsonElement payload)
    {
        if (!payload.TryGetProperty("state", out var state)) return;
        if (state.GetString() != "final") return;

        if (_cfg.RealTimeReplyOutput)
        {
            // Streaming mode: deltas already fired, just close the turn
            _events.RaiseAgentReplyDeltaEnd();
            return;
        }

        // Non-streaming fallback (models that only send final chat event)
        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;

        var text = ExtractFullText(contentEl);
        if (!string.IsNullOrEmpty(text))
        {
            _events.RaiseAgentReplyDeltaStart();
            _events.RaiseAgentReplyFull(text);
            _events.RaiseAgentReplyDeltaEnd();
        }
    }

    public string ExtractFullText(JsonElement contentElement)
    {
        if (contentElement.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "text" &&
                item.TryGetProperty("text", out var textElement))
            {
                textParts.Add(textElement.GetString() ?? string.Empty);
            }
        }
        return string.Join("", textParts);
    }

    /// <summary>
    /// Extracts content from [audio] and [text] markers in the message.
    /// Returns (hasAudioContent, hasTextContent, audioText, textContent).
    /// Handles partial tags: if [audio] has no closing tag, treats everything after it as audio content.
    /// </summary>
    public (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage)
    {
        var audioText = string.Empty;
        var textContent = string.Empty;

        // Extract [audio] content — require closing tag
        var audioMatch = System.Text.RegularExpressions.Regex.Match(fullMessage, @"\[audio\](.*?)\[/audio\]", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (audioMatch.Success)
        {
            audioText = audioMatch.Groups[1].Value.Trim();
        }
        else
        {
            // No closing tag — treat everything from [audio] onwards as audio content
            var openTagIndex = fullMessage.IndexOf("[audio]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                audioText = fullMessage.Substring(openTagIndex + 7).Trim();
            }
        }

        // Extract [text] content — require closing tag
        var textMatch = System.Text.RegularExpressions.Regex.Match(fullMessage, @"\[text\](.*?)\[/text\]", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (textMatch.Success)
        {
            textContent = textMatch.Groups[1].Value.Trim();
        }
        else
        {
            // No closing tag — treat everything from [text] onwards as text content
            var openTagIndex = fullMessage.IndexOf("[text]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                textContent = fullMessage.Substring(openTagIndex + 6).Trim();
            }
        }

        // If no markers found, treat the entire message as text content
        if (string.IsNullOrEmpty(audioText) && string.IsNullOrEmpty(textContent) && !string.IsNullOrEmpty(fullMessage))
        {
            textContent = fullMessage;
        }

        return (!string.IsNullOrEmpty(audioText), !string.IsNullOrEmpty(textContent), audioText, textContent);
    }

    /// <summary>
    /// Strips [audio]...[/audio] tags from text for display purposes, replacing them with the audio content.
    /// </summary>
    public static string StripAudioTags(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[audio\](.*?)\[/audio\]", "$1", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
    }

    // ─── Test support ────────────────────────────────────────────────────────

    /// <summary>Strips audio tags — exposes private static for testing.</summary>
    internal static string TestStripAudioTags(string text) => StripAudioTags(text);

    /// <summary>Extracts marked content — exposes private instance method for testing.</summary>
    internal (bool hasAudio, bool hasText, string audioText, string textContent) TestExtractMarkedContent(string fullMessage)
        => ExtractMarkedContent(fullMessage);
}
