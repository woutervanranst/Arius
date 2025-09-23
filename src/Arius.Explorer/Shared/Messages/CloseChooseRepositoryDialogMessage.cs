using Arius.Explorer.Settings;

namespace Arius.Explorer.Shared.Messages;

public record CloseChooseRepositoryDialogMessage(RepositoryOptions? SelectedRepository = null, bool Success = false);