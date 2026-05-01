using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Known OpenClaw slash commands that can be sent as text messages.
/// The gateway interprets these to control AI sessions, models, and tools.
/// Source: https://github.com/openclaw/openclaw/blob/main/docs/tools/slash-commands.md
/// </summary>
public static class OpenClawCommands
{
    /// <summary>
    /// All known OpenClaw slash command names (without leading /) and their descriptions.
    /// </summary>
    /// <remarks>
    /// Commands are handled by the Gateway. Most must be sent as a standalone message
    /// starting with /. Directives (/think, /fast, /verbose, /trace, /reasoning,
    /// /elevated, /exec, /model, /queue) are stripped from messages before the model
    /// sees them — they persist session settings when sent as directive-only messages.
    /// Inline shortcuts (/help, /commands, /status, /whoami, /id) run immediately
    /// for allowlisted senders and are stripped before the remaining text goes to the model.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        // ── Sessions and runs ────────────────────────────────────────────────
        ["new"] = " [model] — starts a new session (alias: /reset)",
        ["reset"] = " [soft [message]] — resets or soft-resets the current session",
        ["compact"] = " [instructions] — compacts session context (see Compaction docs)",
        ["stop"] = " — aborts the current run",
        ["session"] = " idle|max-age — manage thread-binding expiry",
        ["export-session"] = "-session [path] — exports current session to HTML (alias: /export)",
        ["export"] = " [path] — exports current session to HTML",
        ["export-trajectory"] = "-trajectory [path] — exports JSONL trajectory bundle (alias: /trajectory)",
        ["trajectory"] = " [path] — exports JSONL trajectory bundle",

        // ── Model and run controls (directives) ──────────────────────────────
        ["think"] = " <level> — sets thinking level (off, minimal, low, medium, high, xhigh, adaptive, max)",
        ["thinking"] = " <level> — alias for /think",
        ["t"] = " <level> — alias for /think",
        ["verbose"] = " on|off|full — toggles verbose tool output (alias: /v)",
        ["v"] = " on|off|full — alias for /verbose",
        ["trace"] = " on|off — toggles plugin trace output for current session",
        ["fast"] = " [status|on|off] — shows or sets fast mode",
        ["reasoning"] = " [on|off|stream] — toggles reasoning visibility (alias: /reason)",
        ["reason"] = " [on|off|stream] — alias for /reasoning",
        ["elevated"] = " [on|off|ask|full] — toggles elevated mode (alias: /elev)",
        ["elev"] = " [on|off|ask|full] — alias for /elevated",
        ["exec"] = " host=<auto|sandbox|gateway|node> security=<deny|allowlist|full> ask=<off|on-miss|always> node=<id> — shows or sets exec defaults",
        ["model"] = " [name|#|status] — shows or sets the active model",
        ["models"] = " [provider] [page] [limit=|size=|all] — lists configured/auth-available providers or models for a provider",
        ["queue"] = " <mode> — manages queue behavior (steer, followup, collect, steer-backlog, interrupt, etc.)",

        // ── Discovery and status ────────────────────────────────────────────
        ["help"] = " — shows short help summary",
        ["commands"] = " — shows the generated command catalog",
        ["tools"] = " [compact|verbose] — shows what the current agent can use right now",
        ["status"] = " — shows execution/runtime status with provider usage/quota when available",
        ["diagnostics"] = " [note] — owner-only support-report flow for Gateway bugs and Codex harness runs",
        ["crestodian"] = " [request] — runs Crestodian setup and repair helper from an owner DM",
        ["tasks"] = " — lists active/recent background tasks for the current session",
        ["context"] = " [list|detail|json] — explains how context is assembled",
        ["whoami"] = " — shows your sender ID (alias: /id)",
        ["id"] = " — shows your sender ID",
        ["usage"] = " off|tokens|full|cost — controls per-response usage footer or prints a local cost summary",

        // ── Skills, allowlists, approvals ───────────────────────────────────
        ["skill"] = " <name> [input] — runs a skill by name",
        ["allowlist"] = " [list|add|remove] ... — manages allowlist entries (text-only)",
        ["approve"] = " <id> <decision> — resolves exec approval prompts",
        ["btw"] = " <question> — asks a side question without changing future session context",

        // ── Subagents and ACP ───────────────────────────────────────────────
        ["subagents"] = " list|kill|log|info|send|steer|spawn — manages sub-agent runs for the current session",
        ["acp"] = " spawn|cancel|steer|close|sessions|status|... — manages ACP sessions and runtime options",
        ["focus"] = " <target> — binds current Discord thread or Telegram topic/conversation to a session target",
        ["unfocus"] = " — removes current thread binding",
        ["agents"] = " — lists thread-bound agents for the current session",
        ["kill"] = " <id|#|all> — aborts one or all running sub-agents",
        ["steer"] = " <id|#> <message> — sends steering to a running sub-agent (alias: /tell)",
        ["tell"] = " <id|#> <message> — alias for /steer",

        // ── Owner-only admin / config writes ────────────────────────────────
        ["config"] = " show|get|set|unset — reads or writes openclaw.json (owner-only, requires commands.config: true)",
        ["mcp"] = " show|get|set|unset — reads or writes MCP server config (owner-only, requires commands.mcp: true)",
        ["plugins"] = " list|inspect|show|get|install|enable|disable — plugin management (owner-only for writes, requires commands.plugins: true)",
        ["plugin"] = " — alias for /plugins",
        ["debug"] = " show|set|unset|reset — manages runtime-only config overrides (owner-only, requires commands.debug: true)",
        ["restart"] = " — restarts OpenClaw gateway when enabled (default: enabled)",
        ["send"] = " on|off|inherit — sets send policy (owner-only)",

        // ── Voice, TTS, channel control ─────────────────────────────────────
        ["tts"] = " on|off|status|chat|latest|provider|limit|summary|audio|help — controls TTS",
        ["activation"] = " mention|always — sets group activation mode",
        ["bash"] = " <command> — runs a host shell command (text-only, requires commands.bash: true + tools.elevated allowlists)",

        // ── Bundled plugin commands ─────────────────────────────────────────
        ["dreaming"] = " [on|off|status|help] — toggles memory dreaming",
        ["pair"] = " [qr|status|pending|approve|cleanup|notify] — manages device pairing/setup flow",
        ["phone"] = " status|arm [duration]|disarm — temporarily arms high-risk phone node commands",
        ["voice"] = " status|list [limit]|set <voiceId|name> — manages Talk voice config",
        ["card"] = " ... — sends LINE rich card presets",
        ["codex"] = " status|models|threads|resume|compact|review|diagnostics|account|mcp|skills — Codex harness control",

        // ── Discord native commands (reference; not text-available on other surfaces) ──
        ["vc"] = " join|leave|status — controls Discord voice channels (Discord native command only)",

        // ── Generated dock commands ─────────────────────────────────────────
        ["dock-discord"] = "-discord — switch session reply route to Discord",
        ["dock_discord"] = " — alias for /dock-discord",
        ["dock-mattermost"] = "-mattermost — switch session reply route to Mattermost",
        ["dock_mattermost"] = " — alias for /dock-mattermost",
        ["dock-slack"] = "-slack — switch session reply route to Slack",
        ["dock_slack"] = " — alias for /dock-slack",
        ["dock-telegram"] = "-telegram — switch session reply route to Telegram",
        ["dock_telegram"] = " — alias for /dock-telegram",
    };

    /// <summary>All known OpenClaw slash command names (without leading /).</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(Descriptions.Keys);

    /// <summary>Tries to get a description for a command name. Returns null if unknown.</summary>
    public static string? GetDescription(string commandName) =>
        Descriptions.TryGetValue(commandName, out var desc) ? desc : null;
}
