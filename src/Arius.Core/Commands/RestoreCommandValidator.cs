using FluentValidation;

namespace Arius.Core.Commands;

public class RestoreCommandValidator : AbstractValidator<RestoreCommand>
{
    public RestoreCommandValidator()
    {
        this.AddAccountNameValidation(x => x.AccountName);
        this.AddAccountKeyValidation(x => x.AccountKey);
        this.AddContainerNameValidation(x => x.ContainerName);

        RuleFor(x => x.Passphrase)
            .NotEmpty()
            .WithMessage("Passphrase cannot be empty.");

        RuleFor(x => x.Targets)
            .NotEmpty()
            .WithMessage("At least one target path must be specified.");

        RuleFor(x => x.Targets)
            .Must(AllPathsExist)
            .WithMessage("All specified paths must exist.");

        RuleFor(x => x.Targets)
            .Must(BeValidTargetCombination)
            .WithMessage("Targets must be either: an empty directory, a non-empty directory, one file, or multiple files. Cannot mix files and directories.");


        static bool AllPathsExist(string[] targets)
        {
            return targets.All(target => File.Exists(target) || Directory.Exists(target));
        }

        static bool BeValidTargetCombination(string[] targets)
        {
            if (targets.Length == 0)
                return false;

            var files       = targets.Where(File.Exists).ToArray();
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
}