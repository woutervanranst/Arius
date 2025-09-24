using FluentValidation;

namespace Arius.Core.Features;

internal sealed class StorageAccountCommandValidator : AbstractValidator<StorageAccountCommandProperties>
{
    public StorageAccountCommandValidator()
    {
        RuleFor(x => x.AccountName)
            .NotEmpty()
            .WithMessage("AccountName cannot be empty.");

        RuleFor(x => x.AccountKey)
            .NotEmpty()
            .WithMessage("AccountKey cannot be empty.");
    }
}