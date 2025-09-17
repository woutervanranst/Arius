using System;

namespace Arius.Explorer.Shared.Services;

/// <summary>
/// Simple fallback implementation that returns the initial directory or the user's documents folder when none is provided.
/// </summary>
public sealed class DesignTimeFolderDialogService : IFolderDialogService
{
    public string? BrowseForFolder(string? initialPath)
    {
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            return initialPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
