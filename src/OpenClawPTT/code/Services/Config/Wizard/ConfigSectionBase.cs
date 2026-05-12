using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Base class for modular config sections with generic <see cref="ConfigSetupItem"/> support.
/// Subclasses populate <see cref="_configItems"/> in their constructors or dynamically before
/// calling <see cref="RunConfigItemsAsync"/>.
/// </summary>
public abstract class ConfigSectionBase : IConfigSectionWizard
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>
    /// Config items to run in the generic loop. Subclasses populate this eagerly or dynamically.
    /// </summary>
    protected readonly List<ConfigSetupItem> _configItems = new();

    /// <summary>
    /// Runs all registered <see cref="_configItems"/> in sequence.
    /// Returns true if any item reported a change.
    /// </summary>
    protected async Task<bool> RunConfigItemsAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;
        foreach (var item in _configItems)
        {
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
        }
        return changed;
    }

    public abstract Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct);
}
