using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.Commands.ValidateStorageAccountCredentials;

public class ValidateStorageAccountCredentialsCommand : IRequest<bool>
{
    public required string AccountName { get; init; }
    public required string AccountKey  { get; init; }
}

internal class ValidateStorageAccountCredentialsCommandValidator : AbstractValidator<ValidateStorageAccountCredentialsCommand>
{
    public ValidateStorageAccountCredentialsCommandValidator()
    {
        RuleFor(command => new StorageAccountCredentials(command.AccountName, command.AccountKey))
            .SetValidator(new StorageAccountCredentialsValidator());
    }
}

internal class ValidateStorageAccountCredentialsCommandHandler : IRequestHandler<ValidateStorageAccountCredentialsCommand, bool>
{
    private readonly IStorageAccountFactory storageAccountFactory;

    public ValidateStorageAccountCredentialsCommandHandler(IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
    }

    public async Task<bool> Handle(ValidateStorageAccountCredentialsCommand request, CancellationToken cancellationToken)
    {
        await new ValidateStorageAccountCredentialsCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        
        try
        {
            var credentials    = new StorageAccountCredentials(request.AccountName, request.AccountKey);
            var storageAccount = storageAccountFactory.Create(credentials, 0, TimeSpan.FromSeconds(2));

            // Attempt to list containers as a validation step
            await foreach (var _ in storageAccount.ListContainers(cancellationToken))
            {
                break; // Successfully accessed the account
            }

            return true; // Credentials are valid
        }
        catch
        {
            return false; // Credentials are invalid
        }
    }
}