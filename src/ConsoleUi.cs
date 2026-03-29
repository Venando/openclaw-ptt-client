using System;
using System.Threading;

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