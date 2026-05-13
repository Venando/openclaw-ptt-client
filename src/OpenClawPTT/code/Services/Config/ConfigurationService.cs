using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenClawPTT.ConfigWizard;
using Spectre.Console;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.Services;

public class ConfigurationService : IConfigurationService
{
    public record Variant(string Name) : IVariant;

    private readonly IConfigStorage _storage;
    private readonly ModularConfigurationWizard _wizard;
    private readonly object _saveLock = new();

    // Cached property info for diff computation (immutable per AppConfig type)
    private static readonly PropertyInfo[] AppConfigProperties = typeof(AppConfig)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetMethod?.IsPublic == true && !p.IsDefined(typeof(JsonIgnoreAttribute)))
        .ToArray();

    /// <inheritdoc />
    public event Action<ConfigChangedEventArgs>? ConfigSaved;

    public ConfigurationService()
        : this(new FileConfigStorage())
    {
    }

    public ConfigurationService(IConfigStorage storage)
    {
        _storage = storage;
        _wizard = new ModularConfigurationWizard();
    }

    public async Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default)
    {
        var cfg = _storage.Load();

        if (cfg is null)
        {
            shellHost.AddMessage($"[bold cyan2]────● No configuration found — starting first-time setup.      [/]");

            await shellHost.PromptSelection("Continue?", [new Variant("Yes") ]);

            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            PersistAndNotifyAllChanged(cfg);
            shellHost.AddMessage("[green]Configuration saved.[/]");
            return cfg;
        }

        var issues = Validate(cfg);
        bool needsSetup = issues.Count > 0 || forceReconfigure;

        if (issues.Count > 0)
        {
            shellHost.AddMessage("[cyan2]Configuration issues detected:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [grey]• {Markup.Escape(i)}[/]");
        }

        if (needsSetup)
        {
            if (forceReconfigure)
                shellHost.AddMessage("[cyan2]Starting setup wizard...[/]");
            else
                shellHost.AddMessage("[cyan2]Starting setup wizard to fix missing/invalid fields...[/]");

            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            PersistAndNotifyAllChanged(cfg);
        }

        return cfg;
    }

    public async Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct)
    {
        shellHost.AddMessage("[cyan2]Starting reconfiguration wizard...[/]");

        AppConfig newCfg;
        try
        {
            newCfg = await _wizard.RunReconfigureAsync(shellHost, existing, ct);
        }
        catch (OperationCanceledException)
        {
            return existing;
        }

        // Validate the result — the wizard may skip sections, leaving issues.
        var issues = Validate(newCfg);
        if (issues.Count > 0)
        {
            shellHost.AddMessage("[yellow]Configuration issues found after reconfiguration:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [grey]\u2022 {Markup.Escape(i)}[/]");
            shellHost.AddMessage("[grey](the wizard may have skipped some sections)[/]");

            // Offer to re-enter the wizard to fix remaining issues
            var retry = await PromptSelectionHelper.PromptStringAsync(
                shellHost, "Re-enter wizard to fix issues?",
                new (string Name, string Value)[]
                {
                    ("Yes, re-enter wizard", "yes"),
                    ("No, save as-is", "no"),
                },
                defaultValue: "yes", allowCancel: false, cancellationToken: ct);

            if (retry == "yes")
            {
                return await ReconfigureAsync(shellHost, newCfg, ct);
            }
        }

        PersistAndNotify(newCfg);
        return newCfg;
    }

    public AppConfig? Load() => _storage.Load();

    public void Save(AppConfig cfg)
    {
        lock (_saveLock)
        {
            // Load the old persisted config for diff comparison
            var oldCfg = _storage.Load();
            var changed = ComputeChangedProperties(oldCfg, cfg);
            _storage.Save(cfg);
            ConfigSaved?.Invoke(new ConfigChangedEventArgs(changed, cfg));
        }
    }

    /// <summary>
    /// Persists the config and fires ConfigSaved with all properties marked as changed.
    /// Used after initial setup or full wizard runs where everything may have changed.
    /// </summary>
    private void PersistAndNotifyAllChanged(AppConfig cfg)
    {
        lock (_saveLock)
        {
            var allChanged = new HashSet<string>(AppConfigProperties.Select(p => p.Name));
            _storage.Save(cfg);
            ConfigSaved?.Invoke(new ConfigChangedEventArgs(allChanged, cfg));
        }
    }

    /// <summary>
    /// Persists the config and fires ConfigSaved with a diff against the previously
    /// persisted state. Used by <see cref="ReconfigureAsync"/> where only some sections change.
    /// </summary>
    private void PersistAndNotify(AppConfig cfg)
    {
        lock (_saveLock)
        {
            var oldCfg = _storage.Load();
            var changed = ComputeChangedProperties(oldCfg, cfg);
            _storage.Save(cfg);
            ConfigSaved?.Invoke(new ConfigChangedEventArgs(changed, cfg));
        }
    }

    /// <summary>
    /// Computes which <see cref="AppConfig"/> properties differ between old and new.
    /// Uses JSON serialization for robust comparison (handles enums, nulls).
    /// Returns an empty set when both are null.
    /// </summary>
    internal static HashSet<string> ComputeChangedProperties(AppConfig? oldCfg, AppConfig? newCfg)
    {
        var changed = new HashSet<string>();

        if (ReferenceEquals(oldCfg, newCfg))
            return changed;

        foreach (var prop in AppConfigProperties)
        {
            var oldVal = oldCfg != null ? prop.GetValue(oldCfg) : null;
            var newVal = newCfg != null ? prop.GetValue(newCfg) : null;

            var oldJson = JsonSerializer.SerializeToNode(oldVal);
            var newJson = JsonSerializer.SerializeToNode(newVal);

            if (!JsonNode.DeepEquals(oldJson, newJson))
                changed.Add(prop.Name);
        }

        return changed;
    }

    public List<string> Validate(AppConfig? cfg)
    {
        var issues = new List<string>();

        if (cfg == null)
        {
            issues.Add("Configuration is null.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(cfg.GatewayUrl))
            issues.Add("Gateway URL is required.");

        if (!Uri.TryCreate(cfg.GatewayUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            issues.Add("Gateway URL must start with ws:// or wss://");

        if (string.IsNullOrWhiteSpace(cfg.AuthToken)
            && string.IsNullOrWhiteSpace(cfg.DeviceToken))
            issues.Add("Auth token or device token is required.");

        if (cfg.SampleRate is < 8000 or > 48000)
            issues.Add("Sample rate must be between 8000 and 48000.");

        if (cfg.ReconnectDelaySeconds <= 0)
            issues.Add("Reconnect delay must be positive.");

        if (!Enum.IsDefined(typeof(VisualMode), cfg.VisualMode))
            issues.Add($"VisualMode must be a defined value (current: {cfg.VisualMode}).");

        return issues;
    }
}