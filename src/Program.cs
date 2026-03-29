namespace OpenClawPTT;

using OpenClawPTT.Services;

/// <summary>
/// Improvements:
/// 1. Shortcut settings: any keys, hold/toggle option
/// 2. Config reconfigure option
/// 3. Transcriber platform selection
/// 4. Start transcribing in chunk if talking for long ??? (Not sure might be buggy)
/// 5. Option to show show graphics outside of terminal when recording (for example red dot)
/// 6. Option to minimize app to the tray when folded
/// 7. Text to speach agent reply (through openclaw or no)
/// 8. Figure out how to send raw audio to openclaw and let it interpret it
/// 9. Remove SessionKey and SessionKey from config
/// 10. Refactor/Clean up Program.cs and other files
/// 11. Check if works on linux/macos
/// 12. Add session selection (Currently attaches to "main")
/// 13. Fix: Ctrl + C, not working during config setup
/// </summary>

internal static class Program
{
    private static volatile bool _hotkeyFired;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ConsoleUi.PrintBanner();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            // ── 1. Configuration ────────────────────────────────────
            var configService = new ConfigurationService();
            var cfg = await configService.LoadOrSetupAsync();

            // Main loop that can restart when config changes
            int result;
            do
            {
                result = await RunMainLoop(cfg, cts.Token);
                if (result == 100) // Restart code - config was updated
                {
                    // Reload config from file for next iteration
                    cfg = configService.Load();
                    if (cfg == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  Failed to reload config after update.");
                        Console.ResetColor();
                        return 1;
                    }
                }
            } while (result == 100);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n  Shutting down.");
            return 0;
        }
        catch (GatewayException gex)
        {
            ConsoleUi.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
            return 1;
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"Fatal: {ex.Message}");
#if DEBUG
            Console.WriteLine(ex.StackTrace);
#endif
            return 1;
        }
    }

    private static bool ShouldReconfigure()
    {
        try
        {
            Console.Write("  Press R to reconfigure, any other key to continue... ");
            var timeout = TimeSpan.FromSeconds(3);
            var start = DateTime.Now;
            while (DateTime.Now - start < timeout)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    return key.Key == ConsoleKey.R;
                }
                Thread.Sleep(100);
            }
            Console.WriteLine();
            return false;
        }
        catch (InvalidOperationException)
        {
            // No console available, just continue
            Console.WriteLine("  No console input available, continuing...");
            return false;
        }
    }

    // ─── Main loop (steps 2-4) that can be restarted ────────────────

    private static async Task<int> RunMainLoop(AppConfig cfg, CancellationToken ct)
    {
        using var gateway = new GatewayService(cfg);
        await gateway.ConnectAsync(ct);

        // ── 4. Push-to-talk ─────────────────────────────────────
        Console.WriteLine();
        return await RunPttLoop(gateway, cfg, ct);
    }

    // ─── PTT loop ───────────────────────────────────────────────────

    private static async Task<int> RunPttLoop(GatewayService gateway, AppConfig cfg, CancellationToken ct)
    {
        using var audioService = new AudioService(
            cfg.SampleRate, cfg.Channels, cfg.BitsPerSample, cfg.MaxRecordSeconds, cfg.GroqApiKey);
        using var hotkeyHook = GlobalHotkeyHookFactory.Create();

        hotkeyHook.HotkeyPressed += () => _hotkeyFired = true;
        hotkeyHook.Start();

        ConsoleUi.PrintHelpMenu();
        
        var configService = new ConfigurationService();
        var inputHandler = new InputHandler(gateway, audioService, configService);

        while (!ct.IsCancellationRequested)
        {
            // ── global hotkey ─────────────────────────────────────────
            if (_hotkeyFired)
            {
                _hotkeyFired = false;
                await ToggleRecordingAsync(audioService, gateway, ct);
                continue;
            }

            var result = await inputHandler.HandleInputAsync(ct);
            if (result == -1) return 0; // Quit
            if (result == 100) return 100; // Restart
        }

        return 0;
    }

    private static async Task ToggleRecordingAsync(AudioService audioService, GatewayService gateway, CancellationToken ct)
    {
        if (!audioService.IsRecording)
        {
            audioService.StartRecording();
            return;
        }

        var transcribed = await audioService.StopAndTranscribeAsync(ct);
        if (transcribed != null)
        {
            await SendTextToGatewayAsync(gateway,
                "[The following text is a raw speech-to-text transcription]: " + transcribed, ct);
            
            ConsoleUi.PrintInfo("Waiting for agent…");
        }
    }

    private static async Task SendTextToGatewayAsync(GatewayService gateway, string text, CancellationToken ct)
    {
        ConsoleUi.PrintInfo("Sending… ");
        try
        {
            await gateway.SendTextAsync(text, ct);
            ConsoleUi.PrintSuccess("sent.");
        }
        catch (Exception ex)
        {
            ConsoleUi.PrintError($"failed: {ex.Message}");
        }
    }
}

