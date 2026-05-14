using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using OpenClawPTT.Services.Themes;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Orchestrates the whisper-specific configuration flow: binary type selection,
/// model selection (delegated to <see cref="WhisperModelSelector"/>), and optional
/// model download (delegated to <see cref="WhisperDownloadProgress"/>).
/// Unified flow for both Python openai-whisper and C++ whisper.cpp binary types.
/// </summary>
public sealed class WhisperConfigFlow
{
    private const string TypePython = "python";
    private const string TypeCpp = "cpp";
    private const string CancelSentinel = "__cancel__";

    // ── Public entry point ───────────────────────────────────────────

    /// <summary>
    /// Runs the full whisper configuration flow.
    /// Returns true if any config value was changed.
    /// </summary>
    public async Task<bool> RunAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var allBinaries = WhisperCppModelManager.FindAllWhisperBinaries();
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);

        // ── Step 1: Binary type selection (loop until available or cancelled) ──
        BinaryTypeOption binaryType;
        string resolvedBinaryPath = null!;

        while (true)
        {
            var selected = await SelectBinaryTypeAsync(host, allBinaries, ct);
            if (selected == null)
                return false;

            binaryType = selected.Value;
            resolvedBinaryPath = ResolveBinaryForType(binaryType.Type, allBinaries)!;
            if (resolvedBinaryPath != null)
                break;

            await HandleMissingBinaryAsync(host, binaryType.Type, ct);
            // Loop back — user can pick the other binary type
        }

        var resolvedBinary = new WhisperBinaryInfo(resolvedBinaryPath, binaryType.Type);

        bool changed = false;
        if (resolvedBinaryPath != config.WhisperCppBinaryPath)
        {
            config.WhisperCppBinaryPath = resolvedBinaryPath;
            changed = true;
        }

        var isPython = binaryType.Type == WhisperType.Python;

        // ── Step 2: Model selection ──
        var modelResult = await WhisperModelSelector.SelectModelAsync(
            host, modelManager, isPython, config.WhisperCppModel, ct);
        if (modelResult == null)
        {
            if (changed)
                host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Binary: {binaryType.DisplayText}[/]");
            return changed;
        }

        if (modelResult != config.WhisperCppModel)
        {
            config.WhisperCppModel = modelResult;
            changed = true;
        }

        // ── Step 3: Download model if needed (both Python and C++) ──
        if (isPython)
        {
            if (!WhisperCppModelManager.IsPythonModelCached(modelResult))
            {
                await WhisperDownloadProgress.DownloadPythonAsync(
                    host, resolvedBinaryPath, modelResult, ct);
            }
        }
        else
        {
            if (!modelManager.IsDownloaded(modelResult))
            {
                await WhisperDownloadProgress.DownloadCppAsync(
                    host, modelManager, modelResult, ct);
            }
        }

        // ── Log final config ──
        if (changed)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Binary: {binaryType.DisplayText}[/]");
            if (resolvedBinary != null)
                host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Path: {resolvedBinary.Path}[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Model: {modelResult}[/]");
        }

        return changed;
    }

    // ── Binary type selection ────────────────────────────────────────

    internal struct BinaryTypeOption
    {
        public string Id;
        public WhisperType Type;
        public string DisplayText;
        public bool IsAvailable;
        public string AvailabilityText;
    }

    /// <summary>
    /// Presents binary type (Python vs C++) as a PromptSelection.
    /// Both options shown regardless of availability; unavailable ones use [red] markup.
    /// </summary>
    private static async Task<BinaryTypeOption?> SelectBinaryTypeAsync(
        IStreamShellHost host, IReadOnlyList<WhisperBinaryInfo> allBinaries, CancellationToken ct)
    {
        var hasPython = allBinaries.Any(b => b.Type == WhisperType.Python);
        var hasCpp = allBinaries.Any(b => b.Type == WhisperType.Cpp);

        var options = new List<BinaryTypeOption>
        {
            new()
            {
                Id = TypePython,
                Type = WhisperType.Python,
                DisplayText = "Python (openai-whisper)",
                IsAvailable = hasPython,
                AvailabilityText = hasPython ? $"[{ThemeProvider.Current.Tools.Messages.Success}]available[/]" : $"[{ThemeProvider.Current.Tools.Messages.Error}]not found[/]",
            },
            new()
            {
                Id = TypeCpp,
                Type = WhisperType.Cpp,
                DisplayText = "C++ (whisper.cpp)",
                IsAvailable = hasCpp,
                AvailabilityText = hasCpp ? $"[{ThemeProvider.Current.Tools.Messages.Success}]available[/]" : $"[{ThemeProvider.Current.Tools.Messages.Error}]not found[/]",
            },
        };

        var variants = new List<IVariant>();
        foreach (var opt in options)
        {
            var name = opt.IsAvailable
                ? $"{opt.DisplayText}  — {opt.AvailabilityText}"
                : $"[{ThemeProvider.Current.Tools.Messages.Error}]{opt.DisplayText}  — {opt.AvailabilityText}[/]";
            variants.Add(new ConfigVariant(name, opt.Id));
        }
        variants.Add(new ConfigVariant("", ""));
        variants.Add(new ConfigVariant($"[{ThemeProvider.Current.Tools.General.Muted}]Cancel[/]", CancelSentinel));

        var result = await host.PromptSelection("Select whisper binary type:", variants.ToArray());
        if (result is not { Length: > 0 } || result[0] is not ConfigVariant cv)
            return null;

        if (cv.Value == CancelSentinel)
            return null;

        return options.FirstOrDefault(o => o.Id == cv.Value);
    }

    /// <summary>Resolve the first detected binary for a given type, or null if none found.</summary>
    private static string? ResolveBinaryForType(WhisperType type, IReadOnlyList<WhisperBinaryInfo> allBinaries)
        => allBinaries.FirstOrDefault(b => b.Type == type)?.Path;

    /// <summary>
    /// Shows install instructions when a binary type was chosen but no binary detected.
    /// Returns immediately — caller loops back to binary type selection.
    /// </summary>
    private static async Task HandleMissingBinaryAsync(
        IStreamShellHost host, WhisperType type, CancellationToken ct)
    {
        var typeName = type == WhisperType.Python ? "Python openai-whisper" : "C++ whisper.cpp";

        host.AddMessage("");
        host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ⚠ No {typeName} binary detected on your system.[/]");
        host.AddMessage("");
        host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Install {typeName}:[/]");
        host.AddMessage(type == WhisperType.Python
            ? $"[{ThemeProvider.Current.Tools.General.Muted}]    pip install openai-whisper[/]"
            : $"[{ThemeProvider.Current.Tools.General.Muted}]    Build from: https://github.com/ggerganov/whisper.cpp[/]");
        host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Add it to PATH, then re-run this configuration.[/]");
        host.AddMessage("");

        await Task.CompletedTask;
    }
}
