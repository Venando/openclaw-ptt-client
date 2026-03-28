namespace OpenClawPTT;

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
        PrintBanner();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            // ── 1. Configuration ────────────────────────────────────
            var cfgMgr = new ConfigManager();
            var cfg = cfgMgr.Load();

            if (cfg is null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  No configuration found — starting first-time setup.\n");
                Console.ResetColor();
                cfg = await cfgMgr.RunSetup(cancellationToken: cts.Token);
                cfgMgr.Save(cfg);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  ✓ Configuration saved.\n");
                Console.ResetColor();
            }
            else
            {
                if (reconfigureFlag)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  Reconfigure flag set — starting setup wizard.\n");
                    Console.ResetColor();
                    cfg = await cfgMgr.RunSetup(cfg, cts.Token);
                    cfgMgr.Save(cfg);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n  ✓ Configuration updated.\n");
                    Console.ResetColor();
                }
                else
                {
                    var issues = cfgMgr.Validate(cfg);
                    if (issues.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("  Configuration issues:");
                        Console.ResetColor();
                        foreach (var i in issues)
                            Console.WriteLine($"    • {i}");
                        Console.WriteLine();

                        cfg = await cfgMgr.RunSetup(cfg, cts.Token);
                        cfgMgr.Save(cfg);
                    }
                    else
                    {
                        Console.WriteLine($"  Config loaded: {cfg.GatewayUrl}");
                        if (ShouldReconfigure())
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("  Starting setup wizard...\n");
                            Console.ResetColor();
                            cfg = await cfgMgr.RunSetup(cfg, cts.Token);
                            cfgMgr.Save(cfg);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("\n  ✓ Configuration updated.\n");
                            Console.ResetColor();
                        }
                    }
                }
            }

            // ── 2. Device identity ──────────────────────────────────
            var device = new DeviceIdentity(cfg.DataDir);
            device.EnsureKeypair();
            Console.WriteLine($"  Device ID: {device.DeviceId[..16]}…");
            Console.WriteLine();

            // ── 3. Gateway handshake ────────────────────────────────
            using var gw = new GatewayClient(cfg, device);

            const string agentReplayPrefix = "  🤖 Agent: ";

            var prefixLenght = agentReplayPrefix.Length;
            var newlineSuffix = new string(' ', prefixLenght);

            // wire up agent reply display before connecting
            gw.AgentReplyFull += body =>
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(agentReplayPrefix);
                Console.ResetColor();
                Console.WriteLine(body);
                Console.WriteLine();
            };

            gw.AgentReplyDeltaStart += () =>
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(agentReplayPrefix);
                Console.ResetColor();
            };

            gw.AgentReplyDelta += delta =>
            {
                Console.Write(delta.Replace("\n", "\n" + newlineSuffix));
            };

            gw.AgentReplyDeltaEnd += Console.WriteLine; 

            gw.EventReceived += (name, json) =>
            {
                // health and tick events not sure what to do with them
                return;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[event]: " + name);
                Console.ResetColor();
            };

            await gw.ConnectAsync(cts.Token);


            // ── 4. Push-to-talk ─────────────────────────────────────
            Console.WriteLine();
            return await RunPttLoop(gw, cfg, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n  Shutting down.");
            return 0;
        }
        catch (GatewayException gex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Gateway error: {gex.Message}");

            if (gex.DetailCode != null)
                Console.WriteLine($"  Detail code : {gex.DetailCode}");
            if (gex.RecommendedStep != null)
                Console.WriteLine($"  Recommended : {gex.RecommendedStep}");

            Console.ResetColor();
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Fatal: {ex.Message}");
            Console.ResetColor();
#if DEBUG
            Console.WriteLine(ex.StackTrace);
#endif
            return 1;
        }
    }

    private static bool ShouldReconfigure()
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

    // ─── PTT loop ───────────────────────────────────────────────────

    private static async Task<int> RunPttLoop(GatewayClient gw, AppConfig cfg, CancellationToken ct)
    {
        using var recorder = new AudioRecorder(cfg.SampleRate, cfg.Channels, cfg.BitsPerSample, cfg.MaxRecordSeconds);
        using var groqTranscriber = new GroqTranscriber(cfg.GroqApiKey);
        using var hotkeyHook = GlobalHotkeyHookFactory.Create();

        hotkeyHook.HotkeyPressed += () => _hotkeyFired = true;
        hotkeyHook.Start();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ╔══════════════════════════════════════════╗");
        Console.WriteLine("  ║  Push-to-Talk ready                      ║");
        Console.WriteLine("  ╠══════════════════════════════════════════╣");
        Console.WriteLine("  ║  [Alt+=]  Toggle recording               ║");
        Console.WriteLine("  ║  [T]        Type a text message          ║");
        Console.WriteLine("  ║  [Q]        Quit                         ║");
        Console.WriteLine("  ╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            // ── global hotkey ─────────────────────────────────────────
            if (_hotkeyFired)
            {
                _hotkeyFired = false;
                await ToggleRecordingAsync(recorder, groqTranscriber, gw, ct);
                continue;
            }

            // non-blocking key poll
            if (!Console.KeyAvailable)
            {
                await Task.Delay(50, ct);
                continue;
            }

            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Q)
            {
                Console.WriteLine("  Bye!");
                return 0;
            }

            if (key.Key == ConsoleKey.T)
            {
                Console.Write("  ✏️  Type message: ");
                var text = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(text))
                    await SendTextAsync(gw, text, ct);
            }
        }

        return 0;
    }

    private static async Task ToggleRecordingAsync(AudioRecorder recorder,
        GroqTranscriber transcriber, GatewayClient gw, CancellationToken ct)
    {
        if (!recorder.IsRecording)
        {
            recorder.StartRecording();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  ● REC — press Alt+= again to stop ");
            Console.ResetColor();
            return;
        }

        var wav = recorder.StopRecording();
        Console.WriteLine("■");

        if (wav.Length < 1024)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⏭  Too short (<1KB), skipped.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Sending to Groq {wav.Length / 1024.0:F1} KB…");
        Console.ResetColor();

        var transcribed = await transcriber.TranscribeAsync(wav);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓ Transcribed: {transcribed}");
        Console.ResetColor();

        await SendTextAsync(gw,
            "[The following text is a raw speech-to-text transcription]: " + transcribed, ct);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" Waiting for agent…");
        Console.ResetColor();
    }

    private static async Task SendTextAsync(GatewayClient gw, string text, CancellationToken ct)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Sending… ");
        try
        {
            await gw.SendTextAsync(text, ct);
            Console.WriteLine("sent.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"failed: {ex.Message}");
            Console.ResetColor();
        }
    }

    // ─── banner ─────────────────────────────────────────────────────

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔═══════════════════════════════════════╗");
        Console.WriteLine("  ║    🐾  OpenClaw Push-to-Talk  v1.0    ║");
        Console.WriteLine("  ╚═══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }
}