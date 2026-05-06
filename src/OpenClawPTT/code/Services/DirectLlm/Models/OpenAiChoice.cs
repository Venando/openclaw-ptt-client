using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.DirectLlm.Models;

public sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }
}
