using System;
using System.Threading;
using System.Text;

namespace OpenClawPTT;

public static class ConsoleUi
{
    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔═══════════════════════════════════════╗");
        Console.WriteLine("  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║");
        Console.WriteLine("  ╚═══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }
    
    public static void PrintHelpMenu()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ╔══════════════════════════════════════════╗");
        Console.WriteLine("  ║  Push-to-Talk ready                      ║");
        Console.WriteLine("  ╠══════════════════════════════════════════╣");
        Console.WriteLine("  ║  [Alt+=]  Toggle recording               ║");
        Console.WriteLine("  ║  [Alt+R]  Reconfigure settings           ║");
        Console.WriteLine("  ║  [T]        Type a text message          ║");
        Console.WriteLine("  ║  [Q]        Quit                         ║");
        Console.WriteLine("  ╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }
    
    public static void PrintRecordingIndicator(bool isRecording)
    {
        if (isRecording)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  ● REC — press Alt+= again to stop ");
            Console.ResetColor();
        }
    }
    
    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ {message}");
        Console.ResetColor();
    }
    
    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ⚠ {message}");
        Console.ResetColor();
    }
    
    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ {message}");
        Console.ResetColor();
    }
    
    public static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {message}");
        Console.ResetColor();
    }
    
    public static void PrintInlineInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {message}");
        Console.ResetColor();
    }
    
    public static void PrintInlineSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    
    public static void PrintAgentReply(string prefix, string body)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(prefix);
        Console.ResetColor();
        Console.WriteLine(body);
        Console.WriteLine();
    }
    
    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        Console.Write(delta.Replace("\n", "\n" + newlineSuffix));
    }
    
    /// <summary>
    /// Creates a formatter for streaming agent replies with word wrap and right margin indent.
    /// </summary>
    public static AgentReplyFormatter CreateAgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted = false)
    {
        return new AgentReplyFormatter(prefix, rightMarginIndent, prefixAlreadyPrinted);
    }
    
    /// <summary>
    /// Prints agent reply delta with word wrapping and right margin indent based on configuration.
    /// </summary>
    public static void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        if (config.EnableWordWrap)
        {
            // Use formatter for wrapped output
            // Since delta may be chunked, we need stateful formatter; caller should manage formatter lifetime.
            // For backward compatibility, we fall back to old behavior when word wrap is disabled.
            // This overload is provided for convenience but it's recommended to use CreateAgentReplyFormatter
            // for streaming deltas within a single reply.
            throw new InvalidOperationException("Use CreateAgentReplyFormatter for word-wrapped streaming.");
        }
        else
        {
            PrintAgentReplyDelta(prefix, delta, newlineSuffix);
        }
    }
    
    public static void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n  Gateway error: {message}");
        Console.ResetColor();
        
        if (detailCode != null)
            Console.WriteLine($"  Detail code : {detailCode}");
        if (recommendedStep != null)
            Console.WriteLine($"  Recommended : {recommendedStep}");
    }
}