using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public interface IConfigurationService
{
    /// <summary>
    /// Raised whenever the configuration is successfully saved.
    /// Carries <see cref="ConfigChangedEventArgs"/> with the new config and
    /// the set of property names that actually changed.
    /// Subscribers should check <c>e.IsChanged(...)</c> before acting.
    /// </summary>
    event Action<ConfigChangedEventArgs>? ConfigSaved;

    Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default);
    Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct);
    AppConfig? Load();
    void Save(AppConfig cfg);
    List<string> Validate(AppConfig cfg);
}
