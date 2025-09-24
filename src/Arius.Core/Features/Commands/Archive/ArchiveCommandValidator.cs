using FluentValidation;

namespace Arius.Core.Features.Commands.Archive;

internal class ArchiveCommandValidator : AbstractValidator<ArchiveCommand>
{
    public ArchiveCommandValidator()
    {
        Include(new RepositoryCommandValidator());

        RuleFor(x => x.LocalRoot)
            .NotNull()
            .WithMessage("LocalRoot cannot be null.")
            .Must(ExistAndBeDirectory)
            .WithMessage("LocalRoot must exist and be a valid directory.");

        RuleFor(x => x.Parallelism)
            .GreaterThan(0)
            .WithMessage("Parallelism must be greater than 0.");

        RuleFor(x => x.SmallFileBoundary)
            .GreaterThan(0)
            .WithMessage("SmallFileBoundary must be greater than 0.");

        static bool ExistAndBeDirectory(DirectoryInfo? directory)
        {
            return directory?.Exists == true;
        }
    }
}