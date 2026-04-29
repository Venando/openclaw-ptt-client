using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Singleton wrapper around StreamShell's ConsoleAppHost.
/// Exposes AddMessage, command registration, and the UserInputSubmitted event.
/// </summary>
public sealed class StreamShellHost : IStreamShellHost, IDisposable
{
    private readonly ConsoleAppHost _host;

    public StreamShellHost()
    {
        _host = new ConsoleAppHost();
    }

    public void AddMessage(string markup) => _host.AddMessage(markup);

    public void AddCommand(Command command) => _host.AddCommand(command);

    public event Action<string>? UserInputSubmitted
    {
        add => _host.UserInputSubmitted += value;
        remove => _host.UserInputSubmitted -= value;
    }

    public void Run() => _host.Run();

    public void Stop() => _host.Stop();

    public void Dispose() => _host.Dispose();
}
