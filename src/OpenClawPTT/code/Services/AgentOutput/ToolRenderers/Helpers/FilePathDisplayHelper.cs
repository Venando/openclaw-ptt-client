using System.IO;

namespace OpenClawPTT.Services;

/// <summary>
/// Shared logic for displaying file paths in tool renderers.
/// Truncates long paths by shortening the directory portion.
/// </summary>
internal static class FilePathDisplayHelper
{
    /// <summary>
    /// The maximum number of characters to display for the full path.
    /// </summary>
    private const int MaxLength = 50;

    /// <summary>
    /// Length of the ".." separator used when truncating.
    /// </summary>
    private const int EllipsisPrefixLength = 5;

    /// <summary>
    /// Formats a file path for display, truncating the directory portion
    /// if the full path exceeds <see cref="MaxLength"/> characters.
    /// </summary>
    /// <param name="filePath">The full file path.</param>
    /// <returns>A path string suitable for display, possibly shortened.</returns>
    internal static string FormatDisplayPath(string filePath)
    {
        if (filePath.Length <= MaxLength)
            return filePath;

        string fileName = Path.GetFileName(filePath);
        string folder = Path.GetDirectoryName(filePath) ?? "";
        int availableFolderLength = MaxLength - fileName.Length - EllipsisPrefixLength;

        string shortFolder = folder.Length > availableFolderLength
            ? string.Concat("..", folder.AsSpan(folder.Length - availableFolderLength))
            : folder;

        return shortFolder + Path.DirectorySeparatorChar + fileName;
    }
}
