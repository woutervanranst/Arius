using FluentValidation;

namespace Arius.Core.Application.Commands;

public class ArchiveCommandValidator : AbstractValidator<ArchiveCommand>
{
    public ArchiveCommandValidator()
    {
        RuleFor(x => x.FilePath).NotEmpty().WithMessage("FilePath cannot be empty.");
    }
}