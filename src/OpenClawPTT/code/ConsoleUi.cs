using System;
using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Central UI output facade. All display methods are static and delegate to the
/// current IConsole implementation via SetConsole().
/// </summary>
public static class ConsoleUi
{
    private static IConsole _impl = new SystemConsole();

    // ── IConsole static surface ──────────────────────────────────

    public static void Write(string? text) => _impl.Write(text);
    public static void WriteLine(string? text = null) => _impl.WriteLine(text);
    public static ConsoleColor ForegroundColor
    {
        get => _impl.ForegroundColor;
        set => _impl.ForegroundColor = value;
    }
    public static void ResetColor() => _impl.ResetColor();
    public static bool KeyAvailable => _impl.KeyAvailable;
    public static ConsoleKeyInfo ReadKey(bool intercept = false) => _impl.ReadKey(intercept);
    public static int WindowWidth => _impl.WindowWidth;
    public static Encoding OutputEncoding
    {
        get => _impl.OutputEncoding;
        set => _impl.OutputEncoding = value;
    }
    public static bool TreatControlCAsInput
    {
        get => _impl.TreatControlCAsInput;
        set => _impl.TreatControlCAsInput = value;
    }


    /// <summary>Swap the console implementation. Use a mock in tests.</summary>
    public static void SetConsole(IConsole console) => _impl = console;

    public static void PrintBanner()
    {
        _impl.ForegroundColor = ConsoleColor.Cyan;
        _impl.WriteLine();
        _impl.WriteLine("  ╔═══════════════════════════════════════╗");
        _impl.WriteLine("  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║");
        _impl.WriteLine("  ╚═══════════════════════════════════════╝");
        _impl.ResetColor();
        _impl.WriteLine();
    }

    public static void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var modeDescription = holdToTalk ? "Hold-to-talk" : "Toggle recording";

        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.WriteLine("  ╔══════════════════════════════════════════╗");
        _impl.WriteLine("  ║  Push-to-Talk ready                      ║");
        _impl.WriteLine("  ╠══════════════════════════════════════════╣");
        _impl.WriteLine(FormatMenuLine($"[{hotkeyCombination}]", modeDescription));
        _impl.WriteLine(FormatMenuLine("[Alt+R]", "Reconfigure settings"));
        _impl.WriteLine(FormatMenuLine("[T]", "Type a text message"));
        _impl.WriteLine(FormatMenuLine("[Q]", "Quit"));
        _impl.WriteLine("  ╚══════════════════════════════════════════╝");
        _impl.ResetColor();
        _impl.WriteLine();
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
            _impl.ForegroundColor = ConsoleColor.Red;
            _impl.WriteLine();
            if (holdToTalk)
            {
                _impl.Write($"  ● REC — release {hotkeyCombination} to stop ");
            }
            else
            {
                _impl.Write($"  ● REC — press {hotkeyCombination} again to stop ");
            }
            _impl.ResetColor();
        }
    }

    public static void PrintSuccess(string message)
    {
        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write($"  ✓ {message}");
        _impl.ResetColor();
    }

    public static void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write(prefix);
        var formatter = new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted: true);
        formatter.ProcessDelta(message);
        formatter.Finish();
        _impl.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        _impl.ForegroundColor = ConsoleColor.Yellow;
        _impl.WriteLine($"  ⚠ {message}");
        _impl.ResetColor();
    }

    public static void PrintError(string message)
    {
        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.WriteLine($"  ✗ {message}");
        _impl.ResetColor();
    }

    public static void PrintInfo(string message)
    {
        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine($"  {message}");
        _impl.ResetColor();
    }

    public static void PrintInlineInfo(string message)
    {
        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine();
        _impl.Write($"  {message}");
        _impl.ResetColor();
    }

    public static void PrintInlineSuccess(string message)
    {
        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.WriteLine(message);
        _impl.ResetColor();
    }

    public static void PrintAgentReply(string prefix, string body)
    {
        _impl.WriteLine();
        _impl.ForegroundColor = ConsoleColor.Cyan;
        _impl.Write(prefix);
        _impl.ResetColor();
        _impl.WriteLine(body);
        _impl.WriteLine();
    }

    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        _impl.Write(delta.Replace("\n", "\n" + newlineSuffix));
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
        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.WriteLine($"\n  Gateway error: {message}");
        _impl.ResetColor();

        if (detailCode != null)
            _impl.WriteLine($"  Detail code : {detailCode}");
        if (recommendedStep != null)
            _impl.WriteLine($"  Recommended : {recommendedStep}");
    }

    public static void Log(string tag, string msg)
    {
        _impl.ForegroundColor = ConsoleColor.DarkGray;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }

    public static void LogOk(string tag, string msg)
    {
        _impl.ForegroundColor = ConsoleColor.Green;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }

    public static void LogError(string tag, string msg)
    {
        _impl.ForegroundColor = ConsoleColor.Red;
        _impl.Write($"  [{tag}] ");
        _impl.ResetColor();
        _impl.WriteLine(msg);
    }
}
