using System;
using System.Collections.Generic;
using Spectre.Console;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly ConfigManager _configManager;
    private readonly IConfigStorage _storage;
    private readonly ConfigurationWizard _wizard;

    public ConfigurationService()
        : this(new FileConfigStorage())
    {
    }

    public ConfigurationService(IConfigStorage storage)
    {
        _storage = storage;
        _configManager = new ConfigManager();
        _wizard = new ConfigurationWizard();
    }

    public async Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false)
    {
        var cfg = _storage.Load();

        if (cfg is null)
        {
            shellHost.AddMessage("[yellow]No configuration found — starting first-time setup.[/]");
            cfg = await _wizard.RunSetupAsync(shellHost);
            _storage.Save(cfg);
            shellHost.AddMessage("[green]Configuration saved.[/]");
            return cfg;
        }

        var issues = _configManager.Validate(cfg);
        bool needsSetup = issues.Count > 0 || forceReconfigure;

        if (issues.Count > 0)
        {
            shellHost.AddMessage("[yellow]Configuration issues detected:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [grey]• {Markup.Escape(i)}[/]");
        }

        if (needsSetup)
        {
            if (forceReconfigure)
                shellHost.AddMessage("[yellow]Starting setup wizard...[/]");
            else
                shellHost.AddMessage("[yellow]Starting setup wizard to fix missing/invalid fields...[/]");

            cfg = await _wizard.RunSetupAsync(shellHost, cfg);
            _storage.Save(cfg);
            shellHost.AddMessage("[green]Configuration updated.[/]");
        }

        return cfg;
    }

    public async Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct)
    {
        shellHost.AddMessage("[yellow]Starting setup wizard...[/]");

        AppConfig newCfg;
        try
        {
            newCfg = await _wizard.RunSetupAsync(shellHost, existing, ct);
        }
        catch (OperationCanceledException)
        {
            return existing;
        }

        _storage.Save(newCfg);
        shellHost.AddMessage("[green]Configuration updated.[/]");
        return newCfg;
    }

    public AppConfig? Load() => _storage.Load();

    public void Save(AppConfig cfg) => _storage.Save(cfg);

    public List<string> Validate(AppConfig cfg) => _configManager.Validate(cfg);
}
