using System;
using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. Static methods delegate to the current IConsole implementation.
/// Defaults to SystemConsole. Tests can swap via SetConsole() for output capture.
/// </summary>
public static class ConsoleUi
{
    private static IConsole _console = new SystemConsole();

    /// <summary>Swap the console implementation. Use a mock in tests.</summary>
    public static void SetConsole(IConsole console) => _console = console;

    public static void PrintBanner()
    {
        _console.ForegroundColor = ConsoleColor.Cyan;
        _console.WriteLine();
        _console.WriteLine("  ╔═══════════════════════════════════════╗");
        _console.WriteLine("  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║");
        _console.WriteLine("  ╚═══════════════════════════════════════╝");
        _console.ResetColor();
        _console.WriteLine();
    }

    public static void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var modeDescription = holdToTalk ? "Hold-to-talk" : "Toggle recording";

        _console.ForegroundColor = ConsoleColor.Green;
        _console.WriteLine("  ╔══════════════════════════════════════════╗");
        _console.WriteLine("  ║  Push-to-Talk ready                      ║");
        _console.WriteLine("  ╠══════════════════════════════════════════╣");
        _console.WriteLine(FormatMenuLine($"[{hotkeyCombination}]", modeDescription));
        _console.WriteLine(FormatMenuLine("[Alt+R]", "Reconfigure settings"));
        _console.WriteLine(FormatMenuLine("[T]", "Type a text message"));
        _console.WriteLine(FormatMenuLine("[Q]", "Quit"));
        _console.WriteLine("  ╚══════════════════════════════════════════╝");
        _console.ResetColor();
        _console.WriteLine();
    }

    private static string FormatMenuLine(string leftText, string rightText)
    {
        const int totalWidth = 42;
        const int leftPadding = 2;
        const int middlePadding = 2;

        int leftLength = leftText.Length;
        int rightLength = rightText.Length;
        int totalContentLength = leftLength + middlePadding + rightLength;
        int rightPadding = totalWidth - leftPadding - totalContentLength;

        if (rightPadding < 1) rightPadding = 1;

        return $"  ║  {leftText}{new string(' ', middlePadding)}{rightText}{new string(' ', rightPadding)}║";
    }

    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (isRecording)
        {
            _console.ForegroundColor = ConsoleColor.Red;
            _console.WriteLine();
            if (holdToTalk)
            {
                _console.Write($"  ● REC — release {hotkeyCombination} to stop ");
            }
            else
            {
                _console.Write($"  ● REC — press {hotkeyCombination} again to stop ");
            }
            _console.ResetColor();
        }
    }

    public static void PrintSuccess(string message)
    {
        _console.ForegroundColor = ConsoleColor.Green;
        _console.Write($"  ✓ {message}");
        _console.ResetColor();
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        _console.ForegroundColor = ConsoleColor.Green;
        _console.Write(prefix);
        var formatter = new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(message);
        formatter.Finish();
        _console.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        _console.ForegroundColor = ConsoleColor.Yellow;
        _console.WriteLine($"  ⚠ {message}");
        _console.ResetColor();
    }

    public static void PrintError(string message)
    {
        _console.ForegroundColor = ConsoleColor.Red;
        _console.WriteLine($"  ✗ {message}");
        _console.ResetColor();
    }

    public static void PrintInfo(string message)
    {
        _console.ForegroundColor = ConsoleColor.DarkGray;
        _console.WriteLine($"  {message}");
        _console.ResetColor();
    }

    public static void PrintInlineInfo(string message)
    {
        _console.ForegroundColor = ConsoleColor.DarkGray;
        _console.WriteLine();
        _console.Write($"  {message}");
        _console.ResetColor();
    }

    public static void PrintInlineSuccess(string message)
    {
        _console.ForegroundColor = ConsoleColor.DarkGray;
        _console.WriteLine(message);
        _console.ResetColor();
    }

    public static void PrintAgentReply(string prefix, string body)
    {
        _console.WriteLine();
        _console.ForegroundColor = ConsoleColor.Cyan;
        _console.Write(prefix);
        _console.ResetColor();
        _console.WriteLine(body);
        _console.WriteLine();
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        _console.Write(delta.Replace("\n", "\n" + newlineSuffix));
    }

    public static IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);

    public static IAgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, int consoleWidth)
        => new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted, consoleWidth);

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        if (config.EnableWordWrap)
        {
            throw new InvalidOperationException("Use CreateAgentReplyFormatter for word-wrapped streaming.");
        }
        else
        {
            PrintAgentReplyDelta(prefix, delta, newlineSuffix);
        }
    }

    public static void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        _console.ForegroundColor = ConsoleColor.Red;
        _console.WriteLine($"\n  Gateway error: {message}");
        _console.ResetColor();

        if (detailCode != null)
            _console.WriteLine($"  Detail code : {detailCode}");
        if (recommendedStep != null)
            _console.WriteLine($"  Recommended : {recommendedStep}");
    }

    public static void Log(string tag, string msg)
    {
        _console.ForegroundColor = ConsoleColor.DarkGray;
        _console.Write($"  [{tag}] ");
        _console.ResetColor();
        _console.WriteLine(msg);
    }

    public static void LogOk(string tag, string msg)
    {
        _console.ForegroundColor = ConsoleColor.Green;
        _console.Write($"  [{tag}] ");
        _console.ResetColor();
        _console.WriteLine(msg);
    }

    public static void LogError(string tag, string msg)
    {
        _console.ForegroundColor = ConsoleColor.Red;
        _console.Write($"  [{tag}] ");
        _console.ResetColor();
        _console.WriteLine(msg);
    }
}
