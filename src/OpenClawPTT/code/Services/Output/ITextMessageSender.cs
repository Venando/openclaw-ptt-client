namespace OpenClawPTT.Services;

/// <summary>Unified text message sending with proper AudioWrapPrompt composition.</summary>
public interface ITextMessageSender
{
    Task SendAsync(string text, CancellationToken ct);
}
