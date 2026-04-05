using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace OpenClawPTT.TTS.Providers;

public static class JsonHelper
{
    public static bool TryParseJson(string? json, [NotNullWhen(true)] out JsonDocument? jsonDocument, [NotNullWhen(true)] out string? type)
    {
        jsonDocument = null;
        type = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        byte[]? rentedArray = null;
        int maxByteCount = Encoding.UTF8.GetMaxByteCount(json.Length);
        
        // Use stack memory for small strings (< 1KB), otherwise rent from pool
        Span<byte> buffer = maxByteCount <= 1024 
            ? stackalloc byte[1024] 
            : (rentedArray = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            int bytesWritten = Encoding.UTF8.GetBytes(json, buffer);
            var utf8Source = buffer.Slice(0, bytesWritten);

            var reader = new Utf8JsonReader(utf8Source);

            // 1. Attempt to parse the document
            if (!JsonDocument.TryParseValue(ref reader, out jsonDocument))
            {
                return false;
            }

            // 2. Fail-Proof Check: Ensure we parsed the WHOLE string. 
            // Prevents: {"type":"val"} some_extra_text
            // We allow trailing whitespace, but not trailing tokens.
            while (reader.Read()) 
            {
                if (reader.TokenType != JsonTokenType.Comment && reader.TokenType != JsonTokenType.None)
                {
                    jsonDocument.Dispose();
                    jsonDocument = null;
                    return false;
                }
            }

            // 3. Robust Property Extraction
            if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object &&
                jsonDocument.RootElement.TryGetProperty("type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.String) // Ensure it's actually a string!
            {
                type = typeElement.GetString();
                
                if (!string.IsNullOrWhiteSpace(type))
                {
                    return true;
                }
            }

            // If we got here, it's valid JSON but doesn't meet our "type" requirements
            jsonDocument.Dispose();
            jsonDocument = null;
            type = null;
            return false;
        }
        catch
        {
            // Catch-all for unexpected encoding or parsing issues
            jsonDocument?.Dispose();
            jsonDocument = null;
            type = null;
            return false;
        }
        finally
        {
            if (rentedArray != null) ArrayPool<byte>.Shared.Return(rentedArray);
        }
    }
}