using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class ToolDisplayHandler
{
    private readonly int _rightMarginIndent;
    private readonly Dictionary<string, IToolRenderer> _renderers;
    private readonly IToolOutput _output;
    private readonly IStreamShellHost? _shellHost;

    private static readonly Dictionary<string, string> ToolIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"]           = "📄",
        ["write"]          = "📝",
        ["edit"]           = "🪄",
        ["exec"]           = "▶️",
        ["process"]        = "⚙️",
        ["web_search"]     = "🔍",
        ["web_fetch"]      = "🌐",
        ["sessions_list"]  = "📋",
        ["session_status"] = "📋",
        ["memory_search"]  = "📚",
        ["memory_get"]     = "📚",
        ["image_generate"] = "🎨",
        ["subagents"]      = "🎮🤖",
        ["sessions_spawn"] = "➕🤖",
    };

    public ToolDisplayHandler(IToolOutput output, IEnumerable<IToolRenderer> renderers, int rightMarginIndent, IStreamShellHost? shellHost = null)
    {
        _output = output;
        _rightMarginIndent = rightMarginIndent;
        _shellHost = shellHost;
        _renderers = renderers
            .Where(r => !string.IsNullOrEmpty(r.ToolName))
            .ToDictionary(r => r.ToolName, r => r, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Legacy constructor for backward compatibility.
    /// </summary>
    public ToolDisplayHandler(int rightMarginIndent, IStreamShellHost? shellHost = null)
        : this(new ToolOutputHelper(shellHost: shellHost), BuildDefaultRenderers(new ToolOutputHelper(shellHost: shellHost)), rightMarginIndent, shellHost)
    {
    }

    private static IEnumerable<IToolRenderer> BuildDefaultRenderers(IToolOutput output)
    {
        yield return new ReadToolRenderer(output);
        yield return new WriteToolRenderer(output);
        yield return new EditToolRenderer(output);
        yield return new ExecToolRenderer(output);
        yield return new GenericKvpToolRenderer(output); // handles: process, web_search, image_generate
        yield return new WebFetchToolRenderer(output);
        yield return new SessionsListToolRenderer(output);
        yield return new SessionStatusToolRenderer(output);
        yield return new MemorySearchToolRenderer(output);
        yield return new MemoryGetToolRenderer(output);
        yield return new SubagentsToolRenderer(output);
        yield return new SessionsSpawnToolRenderer(output);
    }

    public void Handle(string toolName, string arguments)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            Console.WriteLine();
            return;
        }

        string icon = ToolIcons.TryGetValue(toolName, out var i) ? i : "🔧";
        string displayName = string.Join(" ", toolName.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));
        string headerLine = $"[grey]  {icon} {displayName}  [/]";

        if (_shellHost != null)
        {
            _shellHost.AddMessage(headerLine);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(headerLine);
            Console.ResetColor();
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            Console.WriteLine();
            if (_shellHost != null)
                _shellHost.AddMessage("");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (_renderers.TryGetValue(toolName, out var renderer))
            {
                renderer.Render(doc.RootElement, _rightMarginIndent);
            }
            else
            {
                // Fall back to generic KVP for unregistered tools
                var generic = new GenericKvpToolRenderer(_output);
                generic.Render(doc.RootElement, _rightMarginIndent);
            }
        }
        catch
        {
            var msg = $"[grey]  {Markup.Escape(arguments)}[/]";
            if (_shellHost != null)
                _shellHost.AddMessage(msg);
            else
                Console.Write(arguments);
        }

        Console.WriteLine();
        if (_shellHost != null)
            _shellHost.AddMessage("");
    }
}
