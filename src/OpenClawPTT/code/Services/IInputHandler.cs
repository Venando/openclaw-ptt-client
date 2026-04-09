namespace OpenClawPTT.Services;

/// <summary>Abstraction for console input handling.</summary>
public interface IInputHandler
{
    Task<int> HandleInputAsync(CancellationToken ct);
}
