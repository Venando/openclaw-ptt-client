using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.DirectLlm.Models;

public sealed class OpenAiResponse
{
    [JsonPropertyName("choices")]
    public OpenAiChoice[]? Choices { get; set; }
}
