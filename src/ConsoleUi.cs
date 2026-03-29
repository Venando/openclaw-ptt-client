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
    
    public static void PrintHelpMenu(string hotkeyCombination = "Alt+=")
    {
        Console.ForegroundColor = ConsoleColor.Green;
        
        // Calculate dynamic border width based on hotkey length
        int hotkeyDisplayLength = hotkeyCombination.Length + 2; // +2 for brackets []
        int baseWidth = 42; // Original width
        int extraWidth = Math.Max(0, hotkeyDisplayLength - 7); // "Alt+=" is 5 + 2 = 7
        
        int totalWidth = baseWidth + extraWidth;
        string borderLine = new string('═', totalWidth);
        
        Console.WriteLine($"  ╔{borderLine}╗");
        Console.WriteLine($"  ║  Push-to-Talk ready{new string(' ', totalWidth - 22)}║");
        Console.WriteLine($"  ╠{borderLine}╣");
        Console.WriteLine($"  ║  [{hotkeyCombination}]  Toggle recording{new string(' ', totalWidth - hotkeyDisplayLength - 24)}║");
        Console.WriteLine($"  ║  [Alt+R]  Reconfigure settings{new string(' ', totalWidth - 33)}║");
        Console.WriteLine($"  ║  [T]        Type a text message{new string(' ', totalWidth - 33)}║");
        Console.WriteLine($"  ║  [Q]        Quit{new string(' ', totalWidth - 20)}║");
        Console.WriteLine($"  ╚{borderLine}╝");
        
        Console.ResetColor();
        Console.WriteLine();
    }
    
    public static void PrintRecordingIndicator(bool isRecording, string hotkeyCombination = "Alt+=")
    {
        if (isRecording)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"  ● REC — press {hotkeyCombination} again to stop ");
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