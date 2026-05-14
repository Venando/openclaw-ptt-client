using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Parses chat history messages into <see cref="SessionStateEvent"/> records.
/// Chat messages carry model, provider, timestamp, and usage (tokens + cost)
/// but NOT session-level metadata like status/phase.
/// </summary>
public static class AgentStatusExtractor
{
    /// <summary>
    /// Builds a <see cref="SessionStateEvent"/> from a chat history
    /// message (which follows a different schema from gateway events).
    /// </summary>
    public static SessionStateEvent? FromHistoryMessage(
        JsonElement msg, string sessionKey)
    {
        if (msg.ValueKind != JsonValueKind.Object)
            return null;

        if (string.IsNullOrEmpty(sessionKey))
            return null;

        // ── Top-level fields ───────────────────────────────────────────
        string? model = null;
        string? modelProvider = null;
        long? timestamp = null;

        if (msg.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
            model = mEl.GetString();

        if (msg.TryGetProperty("provider", out var pEl) && pEl.ValueKind == JsonValueKind.String)
            modelProvider = pEl.GetString();

        if (msg.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number)
        {
            if (tsEl.TryGetInt64(out var tVal)) timestamp = tVal;
        }

        // ── Nested usage ────────────────────────────────────────────────
        long? inputTokens = null;
        long? outputTokens = null;
        long? totalTokens = null;
        decimal? costUsd = null;

        if (msg.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            if (usageEl.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Number)
            {
                if (inp.TryGetInt64(out var v)) inputTokens = v;
            }

            if (usageEl.TryGetProperty("output", out var outp) && outp.ValueKind == JsonValueKind.Number)
            {
                if (outp.TryGetInt64(out var v)) outputTokens = v;
            }

            if (usageEl.TryGetProperty("totalTokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
            {
                if (tt.TryGetInt64(out var v)) totalTokens = v;
            }

            if (usageEl.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Object)
            {
                if (costEl.TryGetProperty("total", out var ct) && ct.ValueKind == JsonValueKind.Number)
                {
                    if (ct.TryGetDecimal(out var d)) costUsd = d;
                }
            }
        }

        return new SessionStateEvent
        {
            SessionKey = sessionKey,
            Model = model,
            ModelProvider = modelProvider,
            UpdatedAt = timestamp,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = costUsd,
        };
    }
}
