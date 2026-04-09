using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public interface IConfigurationService
{
    Task<AppConfig> LoadOrSetupAsync(bool forceReconfigure = false);
    Task<AppConfig> ReconfigureAsync(AppConfig existing);
    AppConfig? Load();
    void Save(AppConfig cfg);
    List<string> Validate(AppConfig cfg);
}