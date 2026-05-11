using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures harness selection and gateway connection settings.</summary>
public sealed class HarnessConfigSection : IConfigSectionWizard
{
    public string Name => "Harness";
    public string Description => "Harness type and gateway connection";

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── Harness type ──
        var harnessOptions = new (string Name, string Value)[]
        {
            ("OpenClaw", "openclaw"),
            ("Nanobot (not supported)", "nanobot"),
        };

        string harness;
        if (isInitialSetup)
        {
            harness = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose harness:", harnessOptions, "openclaw", allowCancel: false, ct);
        }
        else
        {
            var harnessResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose harness:", harnessOptions, "openclaw", ct);
            if (harnessResult == null)
                return false;
            harness = harnessResult;
        }

        // For now only OpenClaw is supported; Nanobot is a placeholder
        if (harness == "nanobot")
        {
            host.AddMessage("[yellow]  Nanobot harness is not yet supported. Using OpenClaw.[/]");
            harness = "openclaw";
        }

        // ── Gateway URL ──
        var gatewayUrl = await PromptTextAsync(host, "Gateway URL",
            config.GatewayUrl,
            v => Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == "ws" || uri.Scheme == "wss"),
            "Expected ws:// or wss:// URL",
            isInitialSetup, ct);
        if (gatewayUrl != null && gatewayUrl != config.GatewayUrl)
        {
            config.GatewayUrl = gatewayUrl;
            changed = true;
        }

        // ── Auth token ──
        var authToken = await PromptTextAsync(host, "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
            config.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            _ => true,
            null,
            isInitialSetup, ct, isSecret: true, allowEmpty: true);
        if (authToken != null)
        {
            var newValue = string.IsNullOrWhiteSpace(authToken) ? null : authToken;
            if (newValue != config.AuthToken)
            {
                config.AuthToken = newValue;
                changed = true;
            }
        }

        // ── TLS fingerprint (only for wss://) ──
        if (config.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            var tlsFingerprint = await PromptTextAsync(host, "TLS cert fingerprint (optional, for wss:// pinning)",
                config.TlsFingerprint ?? "",
                _ => true,
                null,
                isInitialSetup, ct, allowEmpty: true);
            if (tlsFingerprint != null)
            {
                var newValue = string.IsNullOrWhiteSpace(tlsFingerprint) ? null : tlsFingerprint;
                if (newValue != config.TlsFingerprint)
                {
                    config.TlsFingerprint = newValue;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Prompts for free-text input using the legacy text-input approach.
    /// Since PromptSelection is for selections only, we still need text input for free-form values.
    /// Returns null if user cancelled (only possible when allowCancel is true).
    /// </summary>
    private static async Task<string?> PromptTextAsync(
        IStreamShellHost host,
        string description,
        string defaultValue,
        Func<string, bool> validate,
        string? validationHint,
        bool isInitialSetup,
        CancellationToken ct,
        bool isSecret = false,
        bool allowEmpty = false)
    {
        var tcs = new TaskCompletionSource<string?>();

        void OnInput(StreamShell.UserInputSubmittedEventArgs e)
        {
            var input = (e.TextWithoutAttachments ?? e.RawOutput).Trim();

            if (string.IsNullOrEmpty(input))
            {
                if (allowEmpty)
                {
                    tcs.TrySetResult("");
                    return;
                }
                tcs.TrySetResult(defaultValue);
                return;
            }

            if (!validate(input))
            {
                host.AddMessage($"[red]  ✗ Invalid value.{(validationHint != null ? " " + validationHint : "")}[/]");
                SendTextPrompt(host, description, defaultValue, isSecret);
                return;
            }

            var displayValue = isSecret ? MaskSecret(input) : input;
            host.AddMessage($"[green]  ✓ {Spectre.Console.Markup.Escape(displayValue)}[/]");
            tcs.TrySetResult(input);
        }

        host.UserInputSubmitted += OnInput;
        try
        {
            SendTextPrompt(host, description, defaultValue, isSecret);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally
        {
            host.UserInputSubmitted -= OnInput;
        }
    }

    private static void SendTextPrompt(IStreamShellHost host, string description, string defaultValue, bool isSecret)
    {
        host.AddMessage($"[cyan2]▸ {Spectre.Console.Markup.Escape(description)}[/]");
        var displayDefault = isSecret ? MaskSecret(defaultValue) : defaultValue;
        if (!string.IsNullOrEmpty(displayDefault))
            host.AddMessage($"  [grey](current: {Spectre.Console.Markup.Escape(displayDefault)}, press Enter to keep)[/]");
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";
        if (value.Length <= 4)
            return new string('*', value.Length);
        return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
    }
}
