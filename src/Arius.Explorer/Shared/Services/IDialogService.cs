using Arius.Explorer.Settings;

namespace Arius.Explorer.Shared.Services;

public interface IDialogService
{
    /// <summary>
    /// Shows the Choose Repository dialog.
    /// </summary>
    /// <param name="initialRepository">The initial repository options to populate the dialog with, or null for empty form.</param>
    /// <returns>The selected repository if OK was clicked, null if canceled.</returns>
    RepositoryOptions? ShowChooseRepositoryDialog(RepositoryOptions? initialRepository = null);
}