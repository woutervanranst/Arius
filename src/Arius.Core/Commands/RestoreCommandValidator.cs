using FluentValidation;

namespace Arius.Core.Commands;

public class RestoreCommandValidator : AbstractValidator<RestoreCommand>
{
    public RestoreCommandValidator()
    {
        RuleFor(x => x.Targets)
            .NotEmpty()
            .WithMessage("At least one target path must be specified.");

        RuleFor(x => x.Targets)
            .Must(AllPathsExist)
            .WithMessage("All specified paths must exist.");

        RuleFor(x => x.Targets)
            .Must(BeValidTargetCombination)
            .WithMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");
    }

    private static bool AllPathsExist(string[] targets)
    {
        return targets.All(target => File.Exists(target) || Directory.Exists(target));
    }

    private static bool BeValidTargetCombination(string[] targets)
    {
        if (targets.Length == 0)
            return false;

        var files = targets.Where(File.Exists).ToArray();
        var directories = targets.Where(Directory.Exists).ToArray();

        // Cannot mix files and directories
        if (files.Length > 0 && directories.Length > 0)
            return false;

        // If all are directories, must be exactly one
        if (directories.Length > 0)
            return directories.Length == 1;

        // If all are files, can be one or more
        return files.Length >= 1;
    }
}