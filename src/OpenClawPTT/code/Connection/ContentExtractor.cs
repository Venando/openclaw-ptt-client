using System.Text.RegularExpressions;

namespace OpenClawPTT;

/// <summary>
/// Default implementation of IContentExtractor for extracting marked audio/text content.
/// </summary>
public class ContentExtractor : IContentExtractor
{
    public (bool hasAudio, bool hasText, string audioText, string textContent) ExtractMarkedContent(string fullMessage)
    {
        var audioText = string.Empty;
        var textContent = string.Empty;

        var audioMatch = Regex.Match(fullMessage, @"\[audio\](.*?)\[/audio\]", RegexOptions.Singleline);
        if (audioMatch.Success)
        {
            audioText = audioMatch.Groups[1].Value.Trim();
        }
        else
        {
            var openTagIndex = fullMessage.IndexOf("[audio]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                audioText = fullMessage.Substring(openTagIndex + 7).Trim();
            }
        }

        var textMatch = Regex.Match(fullMessage, @"\[text\](.*?)\[/text\]", RegexOptions.Singleline);
        if (textMatch.Success)
        {
            textContent = textMatch.Groups[1].Value.Trim();
        }
        else
        {
            var openTagIndex = fullMessage.IndexOf("[text]", StringComparison.OrdinalIgnoreCase);
            if (openTagIndex >= 0)
            {
                textContent = fullMessage.Substring(openTagIndex + 6).Trim();
            }
        }

        if (string.IsNullOrEmpty(audioText) && string.IsNullOrEmpty(textContent) && !string.IsNullOrEmpty(fullMessage))
        {
            textContent = fullMessage;
        }

        return (!string.IsNullOrEmpty(audioText), !string.IsNullOrEmpty(textContent), audioText, textContent);
    }

    public string StripAudioTags(string text)
    {
        return Regex.Replace(text, @"\[audio\](.*?)\[/audio\]", "$1", RegexOptions.Singleline).Trim();
    }
}
