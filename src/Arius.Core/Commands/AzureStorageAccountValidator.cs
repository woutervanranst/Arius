using FluentValidation;
using System.Linq.Expressions;

namespace Arius.Core.Commands;

public static class AzureStorageAccountValidator
{
    public static void AddAccountNameValidation<T>(this AbstractValidator<T> validator, Expression<Func<T, string>> accountNameSelector)
    {
        validator.RuleFor(accountNameSelector)
            .NotEmpty()
            .WithMessage("AccountName cannot be empty.");
    }

    public static void AddAccountKeyValidation<T>(this AbstractValidator<T> validator, Expression<Func<T, string>> accountKeySelector)
    {
        validator.RuleFor(accountKeySelector)
            .NotEmpty()
            .WithMessage("AccountKey cannot be empty.");
    }

    public static void AddContainerNameValidation<T>(this AbstractValidator<T> validator, Expression<Func<T, string>> containerNameSelector)
    {
        validator.RuleFor(containerNameSelector)
            .NotEmpty()
            .WithMessage("ContainerName cannot be empty.")
            .Must(BeValidAzureContainerName)
            .WithMessage("ContainerName must be a valid Azure container name (lowercase letters, numbers, and hyphens only, 3-63 characters).");
    }

    private static bool BeValidAzureContainerName(string containerName)
    {
        if (string.IsNullOrEmpty(containerName))
            return false;

        if (containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (containerName.StartsWith('-') || containerName.EndsWith('-'))
            return false;

        if (containerName.Contains("--"))
            return false;

        return containerName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }
}