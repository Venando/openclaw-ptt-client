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
            if (meta.IsChained)
            {
                _output.Print("\n", ConsoleColor.Gray);
            }

            RenderCommand(meta);
        }
    }

    private void RenderCommand(CommandMetadata meta)
    {
        // ── Working directory prefix ────────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.WorkingDirectory))
        {
            _output.Print("📂 ", ConsoleColor.DarkGray);
            _output.Print(FilePathDisplayHelper.FormatDisplayPath(meta.WorkingDirectory), ConsoleColor.DarkGray);
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
            _output.Print(FormatToken(pos), ConsoleColor.Cyan);
        }

        // ── Script body (compact display) ────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.ScriptBody))
        {
            _output.Print(" ", ConsoleColor.White);
            int bodyLen = meta.ScriptBody.Length;
            if (bodyLen <= 40)
            {
                _output.Print(meta.ScriptBody, ConsoleColor.DarkGray);
            }
            else
            {
                var preview = meta.ScriptBody.Replace('\n', ' ').Replace('\r', ' ');
                if (preview.Length > 37) preview = preview[..37] + "...";
                _output.Print(preview, ConsoleColor.DarkGray);
            }
        }

        // ── Flags ──────────────────────────────────────────────────────────
        foreach (var flag in meta.Flags)
        {
            if (flag.StartsWith("--"))
            {
                _output.Print(" ", ConsoleColor.White);
                _output.Print(FormatToken(flag), ConsoleColor.Green);
            }
            else
            {
                _output.Print(" ", ConsoleColor.White);
                _output.Print(FormatToken(flag), ConsoleColor.DarkYellow);
            }
        }

        // ── Redirects ─────────────────────────────────────────────────────
        foreach (var redir in meta.Redirects)
        {
            _output.Print(" ", ConsoleColor.White);
            _output.Print(FormatToken(redir), ConsoleColor.Gray);
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

    /// <summary>
    /// Formats a token for display: if it looks like a file path, use
    /// <see cref="FilePathDisplayHelper.FormatDisplayPath"/>; otherwise
    /// fall back to truncation for very long tokens.
    /// </summary>
    private static string FormatToken(string token)
    {
        if (LooksLikePath(token))
            return FilePathDisplayHelper.FormatDisplayPath(token);
        return TruncateLong(token);
    }

    /// <summary>
    /// Heuristic: does <paramref name="token"/> look like a file or dir path?
    /// Checks for absolute/home/relative path prefixes or directory separators.
    /// </summary>
    private static bool LooksLikePath(string token)
    {
        if (token.Length == 0) return false;
        if (token[0] == '/' || token[0] == '~') return true;
        if (token.StartsWith("./") || token.StartsWith("../")) return true;
        // Only match '/' if the token doesn't contain brackets (likely markup remnants)
        if (token.Contains('[') || token.Contains(']')) return false;
        return token.Contains('/');
    }

    /// <summary>
    /// Truncates long tokens to a max width, replacing newlines with spaces
    /// and appending "..." when truncated. Tokens under 100 chars pass through.
    /// </summary>
    private static string TruncateLong(string token)
    {
        if (token.Length <= 100) return token;
        var preview = token.Replace('\n', ' ').Replace('\r', ' ');
        if (preview.Length > 97) preview = preview[..97] + "...";
        return preview;
    }
}
