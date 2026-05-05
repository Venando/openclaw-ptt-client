using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.DirectLlm.Models;

public sealed class AnthropicResponse
{
    [JsonPropertyName("content")]
    public AnthropicContent[]? Content { get; set; }
}
