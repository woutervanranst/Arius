using Arius.Explorer.Settings;

namespace Arius.Explorer.Shared.Messages;

public record CloseChooseRepositoryDialogMessage(RepositoryOptions? Repository = null, bool Success = false);