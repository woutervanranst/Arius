using FluentValidation;

namespace Arius.Core.Commands.RestoreCommand;

public class RestoreCommandValidator : AbstractValidator<RestoreCommand>
{
    public RestoreCommandValidator()
    {
        Include(new RepositoryCommandValidator());

        RuleFor(x => x.Targets)
            .NotEmpty()
            .WithMessage("At least one target path must be specified.");

        RuleFor(x => x.Targets)
            .Must(targets => targets.All(target => target.StartsWith("./")))
            .WithMessage("All targets must start with './'.");

        RuleFor(x => x.Targets)
            .Must(targets => targets.All(IsValidPath))
            .WithMessage("All targets must be valid paths.");

        RuleFor(x => x.Targets)
            .Must(targets => targets.Length > 0)
            .WithMessage("No target(s) specified.");

        RuleFor(x => x.LocalRoot)
            .NotNull()
            .WithMessage("LocalRoot must be specified.");

        RuleFor(x => x.LocalRoot)
            .Must(localRoot => localRoot.Exists)
            .WithMessage("LocalRoot directory must exist.");
    }

    private static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            Path.GetFullPath(path);
            return path.IndexOfAny(Path.GetInvalidPathChars()) == -1;
        }
        catch
        {
            return false;
        }
    }
}