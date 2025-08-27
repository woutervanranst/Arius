using FluentValidation;

namespace Arius.Core.Commands;

public class RestoreCommandValidator : AbstractValidator<RestoreCommand>
{
    public RestoreCommandValidator()
    {
        Include(new RepositoryCommandValidator());

        RuleFor(x => x.Targets)
            .NotEmpty()
            .WithMessage("At least one target path must be specified.");

        RuleFor(x => x.Targets)
            .Must(BeValidTargetCombination)
            .WithMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");


        static bool BeValidTargetCombination(string[] targets)
        {
            if (targets.Length == 0)
                return false;

            var files       = targets.Where(IsFile).ToArray();
            var directories = targets.Where(IsDirectory).ToArray();

            // Cannot mix files and directories
            if (files.Length > 0 && directories.Length > 0)
                return false;

            // If all are directories, must be exactly one
            if (directories.Length > 0)
                return directories.Length == 1;

            // If all are files, can be one or more
            return files.Length >= 1;

            static bool IsDirectory(string path) => path.EndsWith('/') || path.EndsWith('\\');
            static bool IsFile(string path)      => !IsDirectory(path);
        }
    }
}