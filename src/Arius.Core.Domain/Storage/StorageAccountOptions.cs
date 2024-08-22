using FluentValidation;

namespace Arius.Core.Domain.Storage;

public record StorageAccountOptions
{
    public required string AccountName { get; init; }
    public required string AccountKey  { get; init; }
}

public class StorageAccountOptionsValidator : AbstractValidator<StorageAccountOptions>
{
    public StorageAccountOptionsValidator()
    {
        RuleFor(x => x.AccountName)
            .NotEmpty()
            .WithMessage("Account name cannot be empty.")
            .Matches(@"^[a-z0-9]{3,24}$") // Azure storage account names must be 3-24 characters long and can only contain lowercase letters and numbers
            .WithMessage("Account name must be between 3 and 24 characters and can only contain lowercase letters and numbers.");

        RuleFor(x => x.AccountKey)
            .NotEmpty()
            .WithMessage("Account key cannot be empty.");
        // You can add more specific validation rules for the account key if needed
    }
}