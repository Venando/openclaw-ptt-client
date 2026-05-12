using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Speech-To-Text settings.</summary>
public sealed class SttConfigSection : ConfigSectionBase
{
    public override string Name => "Speech-To-Text";
    public override string Description => "STT provider and transcription settings";

    // Provider tag constants
    private const string TagGroq = "groq";
    private const string TagOpenAi = "openai";
    private const string TagWhisperCpp = "whisper-cpp";

    private static readonly (string Name, string Value)[] ProviderOptions =
    {
        ("Groq", TagGroq),
        ("OpenAI", TagOpenAi),
        ("Whisper.cpp (local)", TagWhisperCpp),
    };

    public SttConfigSection()
    {
        // Universal items (always prompted)
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "Locale (e.g. en-US, ja-JP, ru-RU)",
                fieldName: nameof(AppConfig.Locale),
                validator: v => v.Length >= 2,
                validationHint: "At least 2 characters",
                isEmptyToDefault:true),
        });

        // ── Groq items ──
        AddConfigItem(TagGroq, ConfigSetupItem.ForString(
            title: "Groq API key (starts with gsk_)",
            fieldName: nameof(AppConfig.GroqApiKey),
            validator: v => v.StartsWith("gsk_"),
            validationHint: "Must start with gsk_",
            isSecret: true));

        AddConfigItem(TagGroq, ConfigSetupItem.ForString(
            title: "Groq STT model",
            fieldName: nameof(AppConfig.GroqModel),
            isEmptyToDefault:true));

        // ── OpenAI items ──
        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI API key for STT",
            fieldName: nameof(AppConfig.OpenAiApiKey),
            isSecret: true,
            isEmptyToDefault: true));

        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI STT model",
            fieldName: nameof(AppConfig.OpenAiModel)));
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupStt = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
                "Setup Speech-To-Text?", allowCancel: true, cancellationToken: ct);
            if (!setupStt.HasValue || !setupStt.Value)
            {
                host.AddMessage("[grey]  Skipped STT setup.[/]");
                result.IsChanged = false;
                return result;
            }
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // Show current provider if set
        if (!string.IsNullOrEmpty(config.SttProvider))
        {
            var currentModel = config.SttProvider switch
            {
                "groq" => config.GroqModel ?? "whisper-large-v3",
                "openai" => config.OpenAiModel ?? "whisper-1",
                "whisper-cpp" => config.WhisperCppModel ?? "none",
                _ => "?"
            };
            host.AddMessage($"[grey]  Current: [bold]{config.SttProvider}[/] ({currentModel})[/]");
        }

        // ── Provider selection ──
        string? provider = await PromptSelectionHelper.PromptStringAsync(host,
            "Choose STT provider:", ProviderOptions, cancellationToken: ct);

        if (provider == null)
        {
            result.IsChanged = false;
            return result;
        }

        ConfigSelectionHelper.PrintSubSection(host, provider, "");

        if (provider != config.SttProvider)
        {
            config.SttProvider = provider;
            changed = true;
        }
        else if (!isInitialSetup)
        {
            // During reconfigure, user explicitly chose same provider — mark changed
            // to ensure config is saved (items may have been reviewed/re-entered)
            changed = true;
        }

        // ── Seed provider-specific defaults ──
        config.GroqModel ??= "whisper-large-v3";
        config.OpenAiModel ??= "whisper-1";

        // ── Whisper.cpp: custom model selection flow ──
        if (provider == TagWhisperCpp)
        {
            var whisperChanged = await RunWhisperCppFlowAsync(host, config, ct);
            if (whisperChanged)
                changed = true;

            // C7: Whisper model info in settings summary
            result.Settings.Add(new ConfigSectionResult.SettingRecord("Whisper Model", config.WhisperCppModel ?? "none"));
        }
        else
        {
            // ── Run provider-specific items by tag for Groq/OpenAI ──
            if (await RunConfigItemsByTagAsync(provider, host, config, isInitialSetup, ct, result))
                changed = true;
        }

        // ── Generic config items (Locale) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }

    // ── Whisper.cpp model selection flow ─────────────────────────────

    private async Task<bool> RunWhisperCppFlowAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);
        var currentModel = config.WhisperCppModel;
        bool whisperChanged = false; // H8: track actual changes

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var downloadedModels = modelManager.GetDownloadedModels();
            var binaryPath = WhisperCppModelManager.FindWhisperBinary();

            // Show binary status
            if (binaryPath != null)
            {
                host.AddMessage($"[green]  ✓ whisper binary found:[/] [grey]{binaryPath}[/]");
            }
            else
            {
                host.AddMessage("[yellow]  ⚠ whisper binary not found on PATH.[/]");
                host.AddMessage("[grey]    Install whisper.cpp first: https://github.com/ggerganov/whisper.cpp[/]");

                // H6: Without a binary AND no models, user can't use whisper-cpp at all
                if (downloadedModels.Count == 0)
                {
                    host.AddMessage("[yellow]  No models downloaded either. Download is pointless without the whisper binary.[/]");
                }
            }

            // Show downloaded models
            if (downloadedModels.Count > 0)
            {
                host.AddMessage("");
                host.AddMessage("[bold]  Downloaded models:[/]");
                foreach (var model in downloadedModels)
                {
                    var info = WhisperCppModelManager.AvailableModels
                        .FirstOrDefault(m => m.Name == model);
                    var desc = info != null ? $" [grey]({info.Description})[/]" : "";
                    var isActive = model == currentModel ? " [cyan][[active]][/]" : "";
                    host.AddMessage($"    [green]● {model}[/]{desc}{isActive}");
                }
            }
            else
            {
                host.AddMessage("");
                host.AddMessage("[grey]  No models downloaded.[/]");
            }

            // Show available models not downloaded
            var notDownloaded = WhisperCppModelManager.AvailableModels
                .Where(m => !downloadedModels.Contains(m.Name))
                .ToList();

            // Build menu options
            var menuOptions = new List<(string Name, string Value)>();

            // Select model from downloaded
            foreach (var model in downloadedModels)
            {
                menuOptions.Add(($"[green]Use: {model}[/]", $"use:{model}"));
            }

            // H6: Only show download options if binary is found or models already downloaded
            if (notDownloaded.Count > 0 && (binaryPath != null || downloadedModels.Count > 0))
            {
                menuOptions.Add(("", ""));
                menuOptions.Add(("[bold cyan]── Download ──[/]", "__download_header__"));
                foreach (var model in notDownloaded)
                {
                    menuOptions.Add(($"[grey]Download: {model.Name}[/] [grey]({model.Description})[/]", $"download:{model.Name}"));
                }
            }

            // Delete downloaded model
            if (downloadedModels.Count > 0)
            {
                menuOptions.Add(("", ""));
                menuOptions.Add(("[bold red]── Remove ──[/]", "__remove_header__"));
                foreach (var model in downloadedModels)
                {
                    menuOptions.Add(($"[red]Remove: {model}[/]", $"remove:{model}"));
                }
            }

            // H7: Add option to specify binary path
            if (binaryPath == null)
            {
                menuOptions.Add(("", ""));
                menuOptions.Add(("[bold]Specify path...[/]", "__specify_path__"));
            }

            // Done / Back
            menuOptions.Add(("", ""));
            menuOptions.Add(("[bold]Done[/]", "__done__"));

            host.AddMessage("");
            var variants = menuOptions
                .Where(o => !string.IsNullOrEmpty(o.Name))
                .Select(o => new ConfigVariant(o.Name, o.Value))
                .ToArray<IVariant>();

            var selection = await host.PromptSelection(
                "Select model, download, remove, or Done:", variants);

            if (selection == null || selection.Length == 0)
                break;

            var choice = ((ConfigVariant)selection[0]).Value;

            if (choice == "__done__")
                break;

            if (choice.StartsWith("use:"))
            {
                var model = choice.Substring(4);
                if (model != currentModel)
                {
                    config.WhisperCppModel = model;
                    currentModel = model;
                    whisperChanged = true;
                    host.AddMessage($"[green]  Selected model: {model}[/]");
                    host.AddMessage("");
                    break; // Done after selecting
                }
                host.AddMessage($"[grey]  Already using {model}.[/]");
            }
            else if (choice.StartsWith("download:"))
            {
                var modelName = choice.Substring(9);
                await DownloadModelWithProgressAsync(host, modelManager, modelName, ct);
                whisperChanged = true;

                // MEDIUM: No auto-select — user must explicitly "Use:" after download
            }
            else if (choice.StartsWith("remove:"))
            {
                var modelName = choice.Substring(7);

                // Confirm removal
                var confirm = await host.PromptSelection($"Remove model '{modelName}'?",
                    [new ConfigVariant("[red]Yes, remove[/]", "yes"), new ConfigVariant("Cancel", "no")]);

                if (confirm is { Length: > 0 } && ((ConfigVariant)confirm[0]).Value == "yes")
                {
                    if (modelManager.DeleteModel(modelName))
                    {
                        whisperChanged = true;
                        host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
                        if (modelName == currentModel)
                        {
                            config.WhisperCppModel = null;
                            currentModel = null;
                        }
                    }
                }
            }
            else if (choice == "__specify_path__")
            {
                // H7: Guide user to add whisper to PATH or set path in config
                host.AddMessage("");
                host.AddMessage("[yellow]  To use whisper.cpp STT:[/]");
                host.AddMessage("[grey]    1. Add whisper binary to your PATH[/]");
                host.AddMessage("[grey]    2. Or set WhisperCppBinaryPath in config[/]");
                host.AddMessage("[grey]    Current binary search locations:[/]");
                host.AddMessage("[grey]      - PATH directories[/]");
                host.AddMessage("[grey]      - ~/bin/, /usr/local/bin/, /usr/bin/[/]");
                host.AddMessage("");
                continue; // C8: removed unreachable __done__ branch
            }

            // Small pause to let user read messages
            host.AddMessage("");
        }

        return whisperChanged; // H8: return actual change status
    }

    /// <summary>
    /// Downloads a whisper model with progress displayed via SetBottomPanel.
    /// </summary>
    private static async Task DownloadModelWithProgressAsync(
        IStreamShellHost host, WhisperCppModelManager modelManager,
        string modelName, CancellationToken ct)
    {
        var progressPanel = new DownloadProgressBottomPanel();
        host.SetBottomPanel(progressPanel);

        try
        {
            var tcs = new TaskCompletionSource<bool>();

            await modelManager.DownloadModelAsync(
                modelName,
                progressCallback: (fileName, status, downloaded, total, complete) =>
                {
                    progressPanel.SetProgress(fileName, status, downloaded, total, complete);
                },
                ct: ct);

            // Give the UI a moment to show completion
            await Task.Delay(800, ct);
        }
        catch (OperationCanceledException)
        {
            host.AddMessage("[yellow]  Download cancelled.[/]");
        }
        catch (Exception ex)
        {
            host.AddMessage($"[red]  Download failed: {ex.Message}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }
}
