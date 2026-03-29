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
    
    public static void PrintHelpMenu(string hotkeyCombination, bool holdToTalk)
    {
        var modeDescription = holdToTalk ? "Hold-to-talk" : "Toggle recording";
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ╔══════════════════════════════════════════╗");
        Console.WriteLine("  ║  Push-to-Talk ready                      ║");
        Console.WriteLine("  ╠══════════════════════════════════════════╣");
        Console.WriteLine(FormatMenuLine($"[{hotkeyCombination}]", modeDescription));
        Console.WriteLine(FormatMenuLine("[Alt+R]", "Reconfigure settings"));
        Console.WriteLine(FormatMenuLine("[T]", "Type a text message"));
        Console.WriteLine(FormatMenuLine("[Q]", "Quit"));
        Console.WriteLine("  ╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }
    
    private static string FormatMenuLine(string leftText, string rightText)
    {
        const int totalWidth = 42;
        const int leftPadding = 2; // Space after "║  "
        const int middlePadding = 2; // Space between left and right text
        
        int leftLength = leftText.Length;
        int rightLength = rightText.Length;
        int totalContentLength = leftLength + middlePadding + rightLength;
        int rightPadding = totalWidth - leftPadding - totalContentLength;
        
        // Ensure we have at least 1 space padding on the right
        if (rightPadding < 1) rightPadding = 1;
        
        return $"  ║  {leftText}{new string(' ', middlePadding)}{rightText}{new string(' ', rightPadding)}║";
    }
    
    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (isRecording)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (holdToTalk)
            {
                Console.Write($"  ● REC — release {hotkeyCombination} to stop ");
            }
            else
            {
                Console.Write($"  ● REC — press {hotkeyCombination} again to stop ");
            }
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