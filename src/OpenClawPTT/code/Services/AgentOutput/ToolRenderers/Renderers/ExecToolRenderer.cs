using System.Text.Json;
using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class ExecToolRenderer : ToolRendererBase
{
    public ExecToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "exec";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("command", out var cmdProp))
            return;

        string command = cmdProp.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(command))
            return;

        // If the command has many lines (e.g., a heredoc with a long script body),
        // show a compact truncated preview instead of the full parsed rendering.
        int lineCount = command.Split('\n').Length;
        const int maxPreviewLines = 6;
        if (lineCount > maxPreviewLines)
        {
            Output.PrintTruncated(command, "  ", rightMarginIndent, Style.Label, maxRows: maxPreviewLines);
            return;
        }

        var parsed = TerminalCommandParser.Parse(command);

        if (parsed.Count == 0)
        {
            PrintValue(command, Style.Label);
            return;
        }

        bool needNewline = false;
        foreach (var meta in parsed)
        {
            if (needNewline)
            {
                Output.PrintLine("", Style.Separator);
            }

            RenderCommand(meta);
            needNewline = meta.IsChained;
        }
    }

    private void RenderCommand(CommandMetadata meta)
    {
        // ── Working directory prefix ────────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.WorkingDirectory))
        {
            Output.Print("\U0001f4c2 ", Style.ExecPathIcon);
            Output.Print(FilePathDisplayHelper.FormatDisplayPath(meta.WorkingDirectory), Style.ExecPathText);
            Output.Print(" ", Style.ExecPathText);
        }

        // ── Inline env vars ────────────────────────────────────────────────
        if (meta.InlineEnv.Count > 0)
        {
            foreach (var kvp in meta.InlineEnv)
            {
                Output.Print($"{kvp.Key}=", Style.ExecEnvKey);
                Output.Print($"{kvp.Value} ", Style.ExecEnvValue);
            }
        }

        // ── Executable ─────────────────────────────────────────────────────
        string execName = System.IO.Path.GetFileName(meta.Executable);
        string execStyle = GetExecutableStyle(meta.Type);
        Output.Print(execName, execStyle);

        // ── Positional arguments (with safety limit) ─────────────────────
        int posCount = 0;
        int posChars = 0;
        const int maxPositionals = 20;
        const int maxPosChars = 300;
        foreach (var pos in meta.Positionals)
        {
            if (posCount >= maxPositionals || posChars >= maxPosChars)
            {
                var remaining = meta.Positionals.Count - posCount;
                if (remaining > 0)
                    Output.Print($" ... ({remaining} more tokens)", Style.Muted);
                break;
            }
            string fmt = FormatToken(pos);
            posChars += fmt.Length;
            PrintSpace();
            Output.Print(fmt, Style.ExecPositional);
            posCount++;
        }

        // ── Script body (compact display) ────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.ScriptBody))
        {
            PrintSpace();
            int bodyLen = meta.ScriptBody.Length;
            if (bodyLen <= 40)
            {
                Output.Print(meta.ScriptBody, Style.ExecScriptBody);
            }
            else
            {
                var preview = meta.ScriptBody.Replace('\n', ' ').Replace('\r', ' ');
                if (preview.Length > 37) preview = preview[..37] + "...";
                Output.Print(preview, Style.ExecScriptBody);
            }
        }

        // ── Flags ──────────────────────────────────────────────────────────
        foreach (var flag in meta.Flags)
        {
            if (flag.StartsWith("--"))
            {
                PrintSpace();
                Output.Print(FormatToken(flag), Style.ExecLongFlag);
            }
            else
            {
                PrintSpace();
                Output.Print(FormatToken(flag), Style.ExecShortFlag);
            }
        }

        // ── Redirects ─────────────────────────────────────────────────────
        foreach (var redir in meta.Redirects)
        {
            PrintSpace();
            Output.Print(FormatToken(redir), Style.Separator);
        }

        // ── Pipe / chain indicators ────────────────────────────────────────
        if (meta.IsPiped)
        {
            Output.Print(" | ", Style.Separator);
        }
        if (meta.IsChained)
        {
            Output.Print(" && ", Style.Separator);
        }

        // ── Here-doc summary ───────────────────────────────────────────────
        if (meta.HereDoc != null)
        {
            Output.Print(" << '", Style.Separator);
            Output.Print(meta.HereDoc.Delimiter, Style.Separator);
            Output.Print("'", Style.Separator);

            if (!string.IsNullOrEmpty(meta.HereDoc.TargetFile))
            {
                Output.Print(" > ", Style.Separator);
                Output.Print(meta.HereDoc.TargetFile, Style.Separator);
            }

            int bodyLines = meta.HereDoc.Body.Split('\n').Length;
            Output.Print($" ({bodyLines} line{(bodyLines == 1 ? "" : "s")})", Style.ExecHereDocSummary);
        }
    }

    private void PrintSpace() => Output.Print(" ", Style.Separator);

    private static string GetExecutableStyle(CommandType type)
    {
        var s = Style;
        return type switch
        {
            CommandType.FileSystem => s.ExecFileSystem,
            CommandType.FileContent => s.ExecFileContent,
            CommandType.Build => s.ExecBuild,
            CommandType.PackageManager => s.ExecPackageManager,
            CommandType.Network => s.ExecNetwork,
            CommandType.Scripting => s.ExecScripting,
            CommandType.Process => s.ExecProcess,
            CommandType.HereDoc => s.ExecHereDoc,
            CommandType.Vcs => s.ExecVcs,
            CommandType.Pipe => s.Separator,
            CommandType.Chain => s.Separator,
            _ => s.Value,
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
