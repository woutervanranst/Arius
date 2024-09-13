using FluentValidation;

namespace Arius.Core.Domain.Storage;

public record CloudRepositoryOptions : ContainerOptions
{
    public required string Passphrase { get; init; }
}

public class RepositoryOptionsValidator : AbstractValidator<CloudRepositoryOptions>
{
    public RepositoryOptionsValidator()
    {
        RuleFor(command => new ContainerOptions { AccountName = command.AccountName, AccountKey = command.AccountKey, ContainerName = command.ContainerName })
            .SetValidator(new ContainerOptionsValidator());

        RuleFor(x => x.Passphrase)
            .NotEmpty()
            .WithMessage("Passphrase cannot be empty.");
        // You can add more specific validation rules for the passphrase if needed
    }
}