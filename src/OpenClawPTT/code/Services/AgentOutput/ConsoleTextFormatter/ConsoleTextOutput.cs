using System;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of ITextOutput that delegates to System.Console.
/// </summary>
public sealed class ConsoleTextOutput : IConsoleTextOutput
{
    public void Write(string? text) => Console.Write(text);

    public void WriteLine() => Console.WriteLine();

    public int WindowWidth
    {
        get
        {
            try
            {
                return Console.WindowWidth;
            }
            catch
            {
                return 80;
            }
        }
    }
}
