namespace OpenClawPTT;

/// <summary>
/// Extracts and processes marked content from messages (audio/text tags).
/// </summary>
public interface IContentExtractor
{
    /// <summary>
    /// Extracts content marked with [audio] and [text] tags from a message.
    /// Supports both complete tags and partial tags (open without close).
    /// </summary>
    /// <param name="fullMessage">The full message text to parse.</param>
    /// <returns>A tuple containing:
    /// - hasAudio: Whether audio content was found
    /// - hasText: Whether text content was found
    /// - audioText: The extracted audio content (empty if none found)
    /// - textContent: The extracted text content (empty if none found)</returns>
    (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage);

    /// <summary>
    /// Strips [audio] tags from text, leaving only the content within.
    /// </summary>
    /// <param name="text">The text containing audio tags.</param>
    /// <returns>The text with audio tags removed, leaving only the inner content.</returns>
    string StripAudioTags(string text);
}
