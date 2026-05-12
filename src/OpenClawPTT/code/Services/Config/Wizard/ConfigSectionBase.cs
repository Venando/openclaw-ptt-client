using System.Collections.Generic;
using System.Linq;
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
    /// Config items to run unconditionally. Subclasses populate this eagerly or dynamically.
    /// </summary>
    protected readonly List<ConfigSetupItem> _configItems = new();

    /// <summary>
    /// Config items organized by tag (e.g. provider name), for conditional/selective execution.
    /// Subclasses populate via <see cref="AddConfigItem"/> in their constructors or dynamically.
    /// </summary>
    protected readonly Dictionary<string, List<ConfigSetupItem>> _configItemsByTag = new();

    /// <summary>Adds an item under a named tag for selective execution.</summary>
    protected void AddConfigItem(string tag, ConfigSetupItem item)
    {
        if (!_configItemsByTag.TryGetValue(tag, out var list))
        {
            list = new List<ConfigSetupItem>();
            _configItemsByTag[tag] = list;
        }
        list.Add(item);
    }

    /// <summary>
    /// Runs all registered <see cref="_configItems"/> in sequence.
    /// Automatically populates <paramref name="result"/>.<see cref="ConfigSectionResult.Settings"/>
    /// with the display value of each item after prompting.
    /// Returns true if any item reported a change.
    /// </summary>
    protected async Task<bool> RunConfigItemsAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct,
        ConfigSectionResult result)
    {
        bool changed = false;
        foreach (var item in _configItems)
        {
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                item.Title, item.GetDisplayValue(config)));
        }
        return changed;
    }

    /// <summary>
    /// Runs all items registered under the given <paramref name="tag"/>.
    /// Automatically populates <paramref name="result"/>.<see cref="ConfigSectionResult.Settings"/>
    /// with the display value of each item after prompting.
    /// Returns true if any item reported a change.
    /// </summary>
    protected async Task<bool> RunConfigItemsByTagAsync(
        string tag, IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct,
        ConfigSectionResult result)
    {
        if (!_configItemsByTag.TryGetValue(tag, out var items))
            return false;

        bool changed = false;
        foreach (var item in items)
        {
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                item.Title, item.GetDisplayValue(config)));
        }
        return changed;
    }

    /// <summary>
    /// Finds a <see cref="ConfigSetupItem"/> by its <see cref="ConfigSetupItem.Title"/> and re-runs
    /// just that single item. Returns true if the value changed, false if not found or unchanged.
    /// </summary>
    public async Task<bool> TryRerunItemAsync(
        string title, IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var item = FindItemByTitle(title);
        if (item != null)
            return await item.RunAsync(host, config, isInitialSetup, ct);
        return false;
    }

    /// <summary>
    /// Returns the display value for a ConfigSetupItem by title, or null if not found.
    /// </summary>
    public string? TryGetItemDisplayValue(string title, AppConfig config)
    {
        return FindItemByTitle(title)?.GetDisplayValue(config);
    }

    private ConfigSetupItem? FindItemByTitle(string title)
    {
        var item = _configItems.FirstOrDefault(i => i.Title == title);
        if (item != null)
            return item;

        foreach (var kvp in _configItemsByTag)
        {
            item = kvp.Value.FirstOrDefault(i => i.Title == title);
            if (item != null)
                return item;
        }

        return null;
    }

    public abstract Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct);
}
