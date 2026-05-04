using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Raw gateway event payload containing the event name and parsed JsonElement payload.
/// </summary>
public record GatewayEvent(string Name, JsonElement Payload);
