using System;
using System.Linq;
using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class ToolDisplayHandler
{
    private readonly int _rightMarginIndent;

    private record ToolInfo(string Icon, Action<JsonDocument, int> Handler);

    private static readonly Dictionary<string, ToolInfo> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"]          = new("📄",   (doc, _) => HandleRead(doc)),
        ["write"]         = new("📝",   (doc, ri) => HandleWrite(doc, ri)),
        ["edit"]          = new("🪄",   (doc, ri) => HandleEdit(doc, ri)),
        ["exec"]          = new("▶️",   (doc, _) => HandleExec(doc)),
        ["process"]       = new("⚙️",   (doc, _) => HandleGenericKvp(doc)),
        ["web_search"]    = new("🔍",   (doc, _) => HandleGenericKvp(doc)),
        ["web_fetch"]      = new("🌐",   (doc, _) => HandleWebFetch(doc)),
        ["sessions_list"] = new("📋",   (doc, _) => HandleSessionsList(doc)),
        ["session_status"]= new("📋",   (doc, _) => HandleSessionStatus(doc)),
        ["memory_search"] = new("📚",   (doc, _) => HandleMemorySearch(doc)),
        ["memory_get"]    = new("📚",   (doc, _) => HandleMemoryGet(doc)),
        ["image_generate"]= new("🎨",   (doc, _) => HandleGenericKvp(doc)),
        ["subagents"]     = new("🎮🤖", (doc, _) => HandleSubagents(doc)),
        ["sessions_spawn"]= new("➕🤖",  (doc, ri) => HandleSessionsSpawn(doc, ri)),
    };

    public ToolDisplayHandler(int rightMarginIndent)
    {
        _rightMarginIndent = rightMarginIndent;
    }

    public void Handle(string toolName, string arguments)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        string icon = Tools.TryGetValue(toolName, out var t) ? t.Icon : "🔧";
        string toolPrefix = " ";
        string displayName = string.Join(" ", toolName.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));
        Console.Write($"  {icon}{toolPrefix}{displayName}  ");
        Console.ResetColor();

        if (string.IsNullOrWhiteSpace(arguments)) return;

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (Tools.TryGetValue(toolName, out var tool) && tool.Handler != null)
            {
                tool.Handler(doc, _rightMarginIndent);
            }
            else
            {
                HandleGenericKvp(doc);
            }
        }
        catch
        {
            Console.Write(arguments);
        }

        Console.WriteLine();
    }

    private static void TruncatedPrint(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor contentColor = ConsoleColor.White)
    {
        if (string.IsNullOrEmpty(text)) return;
        var allLines = text.Split('\n');
        var displayLines = allLines.Take(4).ToArray();
        var displayContent = string.Join("\n", displayLines);
        bool hasMore = allLines.Length > 4;
        Console.ForegroundColor = contentColor;
        var formatter = new AgentReplyFormatter(continuationPrefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(displayContent);
        formatter.Finish();
        Console.ResetColor();
        if (hasMore)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  ... ({allLines.Length - 4} more lines)");
            Console.ResetColor();
        }
    }

    private static void HandleRead(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("file", out var fileProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(fileProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("offset", out var offsetProp) &&
            doc.RootElement.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (lines {offset}-{offset + limit - 1})");
        }
        else if (doc.RootElement.TryGetProperty("limit", out var limitProp2))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (lines 1-{limitProp2.GetInt32()})");
        }
    }

    private static void HandleWrite(JsonDocument doc, int rightMarginIndent)
    {
        if (doc.RootElement.TryGetProperty("path", out var pathProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(pathProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("content", out var contentProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string contentPrefix = "Content:  ";
            Console.Write(contentPrefix);
            Console.ResetColor();
            var content = contentProp.GetString() ?? "";
            TruncatedPrint(content, contentPrefix, rightMarginIndent);
        }
    }

    private static void HandleEdit(JsonDocument doc, int rightMarginIndent)
    {
        if (doc.RootElement.TryGetProperty("file_path", out var fileProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(fileProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("old_string", out var oldProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string oldPrefix = "  old: ";
            Console.Write(oldPrefix);
            Console.ResetColor();
            TruncatedPrint(oldProp.GetString() ?? "", oldPrefix, rightMarginIndent);
        }
        if (doc.RootElement.TryGetProperty("newString", out var newProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string newPrefix = "  new: ";
            Console.Write(newPrefix);
            Console.ResetColor();
            TruncatedPrint(newProp.GetString() ?? "", newPrefix, rightMarginIndent);
        }
    }

    private static void HandleExec(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("command", out var cmdProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(cmdProp.GetString());
        }
    }

    private static void HandleGenericKvp(JsonDocument doc)
    {
        bool first = true;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (first)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(prop.Value.GetString());
                first = false;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($", {prop.Name}: ");
                Console.ResetColor();
                Console.Write(prop.Value.GetString());
            }
        }
    }

    private static void HandleWebFetch(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("url", out var urlProp))
        {
            var url = urlProp.GetString() ?? "";
            // Strip protocol prefix for cleaner display
            url = url.Replace("https://", "").Replace("http://", "").Replace("www.", "");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(url);
        }
        if (doc.RootElement.TryGetProperty("maxChars", out var maxCharsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" (max {maxCharsProp.GetInt32()} chars)");
        }
    }

    private static void HandleSessionsList(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("limit", out var limitProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"limit: ");
            Console.ResetColor();
            Console.Write($"{limitProp.GetInt32()}");
        }
        if (doc.RootElement.TryGetProperty("kinds", out var kindsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", kinds: ");
            Console.ResetColor();
            Console.Write(kindsProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("messageLimit", out var msgLimitProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", messages: ");
            Console.ResetColor();
            Console.Write($"{msgLimitProp.GetInt32()}");
        }
        if (doc.RootElement.TryGetProperty("activeMinutes", out var activeMinProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", in last ");
            Console.ResetColor();
            Console.Write($"{activeMinProp.GetInt32()} minutes");
        }
    }

    private static void HandleSessionStatus(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("sessionKey", out var keyProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"key: ");
            Console.ResetColor();
            Console.Write(keyProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("model", out var modelProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", model: ");
            Console.ResetColor();
            Console.Write(modelProp.GetString());
        }
    }

    private static void HandleMemorySearch(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("query", out var queryProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(queryProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("maxResults", out var maxResultsProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", max results: ");
            Console.ResetColor();
            Console.Write($"{maxResultsProp.GetInt32()}");
        }
        if (doc.RootElement.TryGetProperty("minScore", out var minScoreProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", min score: ");
            Console.ResetColor();
            Console.Write($"{minScoreProp.GetDouble():F2}");
        }
    }

    private static void HandleMemoryGet(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("path", out var pathProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(pathProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("from", out var fromProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", from: ");
            Console.ResetColor();
            Console.Write($"{fromProp.GetInt32()}");
        }
        if (doc.RootElement.TryGetProperty("lines", out var linesProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($", lines: ");
            Console.ResetColor();
            Console.Write($"{linesProp.GetInt32()}");
        }
    }

    private static void HandleSubagents(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("action", out var actionProp)) return;
        string action = actionProp.GetString() ?? "";

        if (action == "list")
        {
            Console.Write("list");
            if (doc.RootElement.TryGetProperty("recentMinutes", out var rmProp) && rmProp.ValueKind == JsonValueKind.Number)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($", last {rmProp.GetInt32()} minutes");
            }
        }
        else if (action == "kill")
        {
            Console.Write("kill");
            if (doc.RootElement.TryGetProperty("target", out var targetProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", target: ");
                Console.ResetColor();
                Console.Write(targetProp.GetString());
            }
        }
        else if (action == "steer")
        {
            Console.Write("steer");
            if (doc.RootElement.TryGetProperty("target", out var targetProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", target: ");
                Console.ResetColor();
                Console.Write(targetProp.GetString());
            }
            if (doc.RootElement.TryGetProperty("message", out var msgProp))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", message: ");
                Console.ResetColor();
                Console.Write(msgProp.GetString());
            }
        }
        else
        {
            Console.Write(action);
        }
    }

    private static void HandleSessionsSpawn(JsonDocument doc, int rightMarginIndent)
    {
        if (doc.RootElement.TryGetProperty("label", out var labelProp))
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(labelProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("runtime", out var runtimeProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", runtime: ");
            Console.ResetColor();
            Console.Write(runtimeProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("mode", out var modeProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", mode: ");
            Console.ResetColor();
            Console.Write(modeProp.GetString());
        }
        if (doc.RootElement.TryGetProperty("runTimeoutSeconds", out var timeoutProp))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(", timeout: ");
            Console.ResetColor();
            Console.Write($"{timeoutProp.GetInt32()} seconds");
        }
        if (doc.RootElement.TryGetProperty("task", out var taskProp))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            const string taskPrefix = "  Task: ";
            Console.Write(taskPrefix);
            Console.ResetColor();
            TruncatedPrint(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, ConsoleColor.Gray);
        }
    }
}
