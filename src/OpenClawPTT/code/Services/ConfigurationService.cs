using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly ConfigManager _configManager;
    private readonly IConfigStorage _storage;

    public ConfigurationService()
        : this(new FileConfigStorage())
    {
    }

    public ConfigurationService(IConfigStorage storage)
    {
        _storage = storage;
        _configManager = new ConfigManager();
    }

    /// <summary>
    /// Load configuration or run first-time setup if missing.
    /// Validates config and auto-fixes issues.
    /// </summary>
    public async Task<AppConfig> LoadOrSetupAsync(bool forceReconfigure = false)
    {
        var cfg = _storage.Load();

        if (cfg is null)
        {
            ConsoleUi.PrintWarning("No configuration found — starting first-time setup.\n");
            cfg = await _configManager.RunSetup();
            _storage.Save(cfg);
            ConsoleUi.PrintSuccess("Configuration saved.\n");
            return cfg;
        }

        var issues = _configManager.Validate(cfg);
        if (issues.Count > 0)
        {
            ConsoleUi.PrintWarning("Configuration issues detected:");
            foreach (var i in issues)
                Console.WriteLine($"    • {i}");
            Console.WriteLine();

            Console.WriteLine("  Starting setup wizard to fix missing/invalid fields...\n");
            cfg = await _configManager.RunSetup(cfg);
            _storage.Save(cfg);
            ConsoleUi.PrintSuccess("Configuration updated.\n");
            return cfg;
        }

        ConsoleUi.Log("configuration_service", $"Config loaded");

        if (forceReconfigure || ShouldReconfigure())
        {
            ConsoleUi.PrintWarning("Starting setup wizard...\n");
            cfg = await _configManager.RunSetup(cfg);
            _storage.Save(cfg);
            ConsoleUi.PrintSuccess("Configuration updated.\n");
        }

        return cfg;
    }

    /// <summary>
    /// Run reconfiguration wizard for existing config.
    /// </summary>
    public async Task<AppConfig> ReconfigureAsync(AppConfig existing)
    {
        var newCfg = await _configManager.RunSetup(existing);
        _storage.Save(newCfg);
        return newCfg;
    }

    /// <summary>
    /// Load config from disk without validation or setup.
    /// </summary>
    public AppConfig? Load()
    {
        return _storage.Load();
    }

    /// <summary>
    /// Save config to disk.
    /// </summary>
    public void Save(AppConfig cfg)
    {
        _storage.Save(cfg);
    }

    /// <summary>
    /// Validate config and return issues.
    /// </summary>
    public List<string> Validate(AppConfig cfg)
    {
        return _configManager.Validate(cfg);
    }

    private static bool ShouldReconfigure()
    {
        try
        {
            ConsoleUi.Log("configuration_service", "Press [R] to reconfigure");

            var timeout = TimeSpan.FromSeconds(3);
            var start = DateTime.Now;
            while (DateTime.Now - start < timeout)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    return key.Key == ConsoleKey.R;
                }
                Thread.Sleep(100);
            }
            Console.WriteLine();
            return false;
        }
        catch (InvalidOperationException)
        {
            // No console available, just continue
            Console.WriteLine("  No console input available, continuing...");
            return false;
        }
    }
}
