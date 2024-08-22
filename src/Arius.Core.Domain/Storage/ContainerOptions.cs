using FluentValidation;

namespace Arius.Core.Domain.Storage;

public record ContainerOptions : StorageAccountOptions
{
    public required string ContainerName { get; init; }
}

public class ContainerOptionsValidator : AbstractValidator<ContainerOptions>
{
    public ContainerOptionsValidator()
    {
        RuleFor(command => new StorageAccountOptions { AccountName = command.AccountName, AccountKey = command.AccountKey })
            .SetValidator(new StorageAccountOptionsValidator());

        RuleFor(x => x.ContainerName)
            .NotEmpty()
            .WithMessage("Container name cannot be empty.")
            .Matches(@"^[a-z0-9]{3,63}$") // Azure storage container names must be 3-63 characters long and can only contain lowercase letters, numbers, and hyphens
            .WithMessage("Container name must be between 3 and 63 characters and can only contain lowercase letters, numbers, and hyphens.");

    }
}