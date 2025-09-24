using FluentValidation;

namespace Arius.Core.Features.Queries.PointerFileEntries;

internal sealed class PointerFileEntriesQueryValidator : AbstractValidator<PointerFileEntriesQuery>
{
    public PointerFileEntriesQueryValidator()
    {
        Include(new RepositoryCommandValidator());

        RuleFor(x => x.LocalPath)
            .NotEmpty()
            .WithMessage("LocalPath cannot be empty.");

        RuleFor(x => x.Prefix)
            .NotEmpty()
            .WithMessage("Prefix cannot be empty.");

        RuleFor(x => x.Prefix)
            .Must(p => p.StartsWith('/'))
            .WithMessage("Prefix must start with a '/' character.");

        RuleFor(x => x.Prefix)
            .Must(p => p.EndsWith('/'))
            .WithMessage("Prefix must end with a '/' character.");
    }
}