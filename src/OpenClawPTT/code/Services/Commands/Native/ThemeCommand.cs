using System.Linq;
using OpenClawPTT.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Native command: /theme — shows current theme and available themes,
/// or switches to a named theme from the themes folder.
/// </summary>
public sealed class ThemeCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly ThemeService _themeService;

    public string Name => "theme";
    public string Description => "Show current theme or switch to a theme. \"/theme\" to list, \"/theme <name>\" to switch";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Configuration;
    public string[]? Suggestions => _themeService.GetAvailableThemes();

    public ThemeCommand(IStreamShellHost host, ThemeService themeService)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length > 0)
            return HandleSwitchThemeAsync(args[0]);

        return HandleListThemeAsync();
    }

    private Task HandleListThemeAsync()
    {
        var currentName = _themeService.GetCurrentThemeName();
        var current = _themeService.CurrentTheme;

        _host.AddMessage($"[cyan2]Current theme:[/] [bold]{Markup.Escape(currentName)}[/]");
        _host.AddMessage($"  [grey]Author:[/] {Markup.Escape(current.Author)}");
        _host.AddMessage($"  [grey]Accent:[/] [{current.AccentColor}]{Markup.Escape(current.AccentColor)}[/]  [grey]Secondary:[/] [{current.SecondaryColor}]{Markup.Escape(current.SecondaryColor)}[/]");
        _host.AddMessage($"  [grey]Foreground:[/] [{current.ForegroundColor}]{Markup.Escape(current.ForegroundColor)}[/]  [grey]Border:[/] [{current.BorderColor}]{Markup.Escape(current.BorderColor)}[/]");
        _host.AddMessage("");

        var available = _themeService.GetAvailableThemes();
        if (available.Length == 0)
        {
            _host.AddMessage("[yellow]  No custom theme files found in themes folder.[/]");
            _host.AddMessage("[grey]  Use /appconfig to set ThemeFile to a theme name, or create a .json in the themes folder.[/]");
        }
        else
        {
            _host.AddMessage("[cyan2]Available themes:[/]");
            foreach (var themeName in available)
            {
                var marker = string.Equals(themeName, currentName, StringComparison.OrdinalIgnoreCase)
                    ? " [bold]►[/]"
                    : "  ";
                _host.AddMessage($"  {marker} {Markup.Escape(themeName)}");
            }
            _host.AddMessage("");
            _host.AddMessage("[grey]  Use /theme <name> to switch[/]");
        }

        return Task.CompletedTask;
    }

    private Task HandleSwitchThemeAsync(string themeName)
    {
        if (_themeService.TrySwapTheme(themeName))
        {
            var current = _themeService.CurrentTheme;
            _host.AddMessage($"[green]Switched to theme:[/] [bold]{Markup.Escape(current.Name)}[/]");
        }
        else
        {
            _host.AddMessage($"[red]Theme not found or failed to load:[/] {Markup.Escape(themeName)}");
            _host.AddMessage("[grey]  Theme files are stored in the themes folder as .json files.[/]");
            _host.AddMessage("[grey]  Use /theme to list available themes.[/]");
        }

        return Task.CompletedTask;
    }
}
