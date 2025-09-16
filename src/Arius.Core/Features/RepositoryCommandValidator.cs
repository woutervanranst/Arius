using FluentValidation;

namespace Arius.Core.Features;

internal sealed class RepositoryCommandValidator : AbstractValidator<RepositoryCommandProperties>
{
    public RepositoryCommandValidator()
    {
        // Reuse the StorageAccountValidator rules
        Include(new StorageAccountValidator());

        RuleFor(x => x.ContainerName)
            .NotEmpty()
            .WithMessage("ContainerName cannot be empty.")
            .Must(BeValidAzureContainerName)
            .WithMessage("ContainerName must be a valid Azure container name (lowercase letters, numbers, and hyphens only, 3-63 characters).");

        RuleFor(x => x.Passphrase)
            .NotEmpty()
            .WithMessage("Passphrase cannot be empty.");
    }

    private static bool BeValidAzureContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName))
            return false;

        if (containerName.Length is < 3 or > 63)
            return false;

        if (containerName.StartsWith('-') || containerName.EndsWith('-'))
            return false;

        if (containerName.Contains("--"))
            return false;

        return containerName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }
}