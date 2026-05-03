using System.Text.Json;
using Spectre.Console;

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
        ConsoleColor execColor = GetExecutableColor(meta.Type);
        _output.Print(execName, execColor);

        // ── Positional arguments ───────────────────────────────────────────
        foreach (var pos in meta.Positionals)
        {
            _output.Print(" ", ConsoleColor.White);
            _output.Print(pos, ConsoleColor.Cyan);
        }

        // ── Flags ──────────────────────────────────────────────────────────
        foreach (var flag in meta.Flags)
        {
            if (flag.StartsWith("--"))
            {
                _output.Print(" ", ConsoleColor.White);
                _output.Print(flag, ConsoleColor.Green);
            }
            else
            {
                _output.Print(" ", ConsoleColor.White);
                _output.Print(flag, ConsoleColor.DarkYellow);
            }
        }

        // ── Redirects ─────────────────────────────────────────────────────
        foreach (var redir in meta.Redirects)
        {
            _output.Print(" ", ConsoleColor.White);
            _output.Print(redir, ConsoleColor.Gray);
        }

        // ── Pipe / chain indicators ────────────────────────────────────────
        if (meta.IsPiped)
        {
            _output.Print(" | ", ConsoleColor.Gray);
        }
        if (meta.IsChained)
        {
            _output.Print(" && ", ConsoleColor.Gray);
        }

        // ── Here-doc summary ───────────────────────────────────────────────
        if (meta.HereDoc != null)
        {
            _output.Print(" << '", ConsoleColor.Gray);
            _output.Print(meta.HereDoc.Delimiter, ConsoleColor.Gray);
            _output.Print("'", ConsoleColor.Gray);

            if (!string.IsNullOrEmpty(meta.HereDoc.TargetFile))
            {
                _output.Print(" > ", ConsoleColor.Gray);
                _output.Print(meta.HereDoc.TargetFile, ConsoleColor.Gray);
            }

            int bodyLines = meta.HereDoc.Body.Split('\n').Length;
            _output.Print($" ({bodyLines} line{(bodyLines == 1 ? "" : "s")})", ConsoleColor.DarkGray);
        }
    }

    private static ConsoleColor GetExecutableColor(CommandType type)
    {
        return type switch
        {
            CommandType.FileSystem => ConsoleColor.Green,
            CommandType.FileContent => ConsoleColor.Blue,
            CommandType.Build => ConsoleColor.Magenta,
            CommandType.PackageManager => ConsoleColor.Red,
            CommandType.Network => ConsoleColor.Cyan,
            CommandType.Scripting => ConsoleColor.Yellow,
            CommandType.Process => ConsoleColor.DarkYellow,
            CommandType.HereDoc => ConsoleColor.DarkCyan,
            CommandType.Pipe => ConsoleColor.Gray,
            CommandType.Chain => ConsoleColor.Gray,
            _ => ConsoleColor.White,
        };
    }
}
