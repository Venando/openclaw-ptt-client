namespace OpenClawPTT.Services;

/// <summary>
/// Abstraction for config persistence, enabling testability.
/// </summary>
public interface IConfigStorage
{
    AppConfig? Load();
    void Save(AppConfig config);
}

/// <summary>
/// Default implementation that persists config to disk via ConfigManager.
/// </summary>
public sealed class FileConfigStorage : IConfigStorage
{
    private readonly ConfigManager _manager;

    public FileConfigStorage()
    {
        _manager = new ConfigManager();
    }

    public AppConfig? Load()
    {
        return _manager.Load();
    }

    public void Save(AppConfig config)
    {
        _manager.Save(config);
    }
}
