using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

/// <summary>Abstraction for console input handling.</summary>
public interface IInputHandler
{
    Task<InputResult> HandleInputAsync(CancellationToken ct);
}
