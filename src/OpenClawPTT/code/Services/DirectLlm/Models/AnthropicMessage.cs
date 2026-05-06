using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.DirectLlm.Models;

public sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
