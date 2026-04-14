using System;
using System.Text.Json;
using System.Threading;

namespace OpenClawPTT;

public interface ISender
{
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
}
