using FluentValidation;

namespace Arius.Core.Features;

internal sealed class StorageAccountValidator : AbstractValidator<StorageAccountCommandProperties>
{
    public StorageAccountValidator()
    {
        RuleFor(x => x.AccountName)
            .NotEmpty()
            .WithMessage("AccountName cannot be empty.");

        RuleFor(x => x.AccountKey)
            .NotEmpty()
            .WithMessage("AccountKey cannot be empty.");
    }
}