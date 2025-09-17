namespace Arius.Explorer.Shared.Services;

/// <summary>
/// Abstraction over the platform specific folder selection dialog.
/// </summary>
public interface IFolderDialogService
{
    /// <summary>
    /// Opens a folder browser dialog and returns the selected path or <c>null</c> when the user cancels the dialog.
    /// </summary>
    /// <param name="initialPath">An optional initial path that is highlighted when the dialog opens.</param>
    string? BrowseForFolder(string? initialPath);
}
