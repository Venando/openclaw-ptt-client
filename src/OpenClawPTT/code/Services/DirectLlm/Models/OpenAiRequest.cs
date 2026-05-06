using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.DirectLlm.Models;

public sealed class OpenAiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
    
    [JsonPropertyName("messages")]
    public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();
    
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
