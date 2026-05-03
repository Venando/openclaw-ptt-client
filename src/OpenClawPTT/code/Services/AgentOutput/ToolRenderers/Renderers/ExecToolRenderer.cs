using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ExecToolRenderer : IToolRenderer
{
    private readonly IToolOutput _output;

    public ExecToolRenderer(IToolOutput output)
    {
        _output = output;
    }

    public string ToolName => "exec";

    public void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("command", out var cmdProp))
            return;

        string command = cmdProp.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(command))
            return;

        var parsed = TerminalCommandParser.Parse(command);

        if (parsed.Count == 0)
        {
            _output.Print(command, ConsoleColor.Gray);
            return;
        }

        foreach (var meta in parsed)
        {
            RenderCommand(meta);
        }
    }

    private void RenderCommand(CommandMetadata meta)
    {
        // ── Working directory prefix ────────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.WorkingDirectory))
        {
            _output.Print("📂 ", ConsoleColor.DarkGray);
            _output.Print(meta.WorkingDirectory, ConsoleColor.DarkGray);
            _output.Print(" ", ConsoleColor.DarkGray);
        }

        // ── Inline env vars ────────────────────────────────────────────────
        if (meta.InlineEnv.Count > 0)
        {
            foreach (var kvp in meta.InlineEnv)
            {
                _output.Print($"{kvp.Key}=", ConsoleColor.Cyan);
                _output.Print($"{kvp.Value} ", ConsoleColor.Yellow);
            }
        }

        // ── Executable ─────────────────────────────────────────────────────
        string execName = System.IO.Path.GetFileName(meta.Executable);
        (string bg, string fg) = GetExecutableStyle(meta.Type);
        string execTag = $"[{fg} on {bg}] {MarkupEscape(execName)} [/]";
        _output.Print(execTag, ConsoleColor.White);

        // ── Positional arguments ───────────────────────────────────────────
        foreach (var pos in meta.Positionals)
        {
            string posTag = $"[cyan]{MarkupEscape(pos)}[/]";
            _output.Print(" ", ConsoleColor.White);
            _output.Print(posTag, ConsoleColor.Cyan);
        }

        // ── Flags ──────────────────────────────────────────────────────────
        foreach (var flag in meta.Flags)
        {
            if (flag.StartsWith("--"))
            {
                string flagTag = $"[green]{MarkupEscape(flag)}[/]";
                _output.Print(" ", ConsoleColor.White);
                _output.Print(flagTag, ConsoleColor.Green);
            }
            else
            {
                string flagTag = $"[olive]{MarkupEscape(flag)}[/]";
                _output.Print(" ", ConsoleColor.White);
                _output.Print(flagTag, ConsoleColor.DarkYellow);
            }
        }

        // ── Redirects ─────────────────────────────────────────────────────
        foreach (var redir in meta.Redirects)
        {
            string redirTag = $"[grey]{MarkupEscape(redir)}[/]";
            _output.Print(" ", ConsoleColor.White);
            _output.Print(redirTag, ConsoleColor.Gray);
        }

        // ── Pipe / chain indicators ────────────────────────────────────────
        if (meta.IsPiped)
        {
            string pipeTag = " [grey]|[/] ";
            _output.Print(pipeTag, ConsoleColor.Gray);
        }
        if (meta.IsChained)
        {
            string chainTag = " [grey]&&[/] ";
            _output.Print(chainTag, ConsoleColor.Gray);
        }

        // ── Here-doc summary ───────────────────────────────────────────────
        if (meta.HereDoc != null)
        {
            string hereTag = $" [grey]<< '{MarkupEscape(meta.HereDoc.Delimiter)}'[/]";
            _output.Print(hereTag, ConsoleColor.Gray);

            if (!string.IsNullOrEmpty(meta.HereDoc.TargetFile))
            {
                string targetTag = $" [grey]> {MarkupEscape(meta.HereDoc.TargetFile)}[/]";
                _output.Print(targetTag, ConsoleColor.Gray);
            }

            int bodyLines = meta.HereDoc.Body.Split('\n').Length;
            string bodyTag = $" [dim]({bodyLines} line{(bodyLines == 1 ? "" : "s")})[/]";
            _output.Print(bodyTag, ConsoleColor.DarkGray);
        }
    }

    private static string MarkupEscape(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    private static (string bg, string fg) GetExecutableStyle(CommandType type)
    {
        return type switch
        {
            CommandType.FileSystem => ("#1a3a1a", "lime"),
            CommandType.FileContent => ("#1a2a4a", "deepskyblue"),
            CommandType.Build => ("#2a1a4a", "violet"),
            CommandType.PackageManager => ("#3a1a1a", "tomato"),
            CommandType.Network => ("#1a2a4a", "cyan1"),
            CommandType.Scripting => ("#2a2a2a", "orange1"),
            CommandType.Process => ("#3a2a1a", "gold1"),
            CommandType.HereDoc => ("#1a2020", "springgreen"),
            CommandType.Pipe => ("#1a1a1a", "grey"),
            CommandType.Chain => ("#1a1a1a", "grey"),
            _ => ("#222222", "white"),
        };
    }
}
