using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public interface IConfigurationService
{
    Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default);
    Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct);
    AppConfig? Load();
    void Save(AppConfig cfg);
    List<string> Validate(AppConfig cfg);
}
