using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.Queries.ValidateStorageAccountCredentials;

public class ValidateStorageAccountCredentialsQuery : IRequest<bool>
{
    public required string AccountName { get; init; }
    public required string AccountKey { get; init; }
}

internal class ValidateStorageAccountCredentialsQueryValidator : AbstractValidator<ValidateStorageAccountCredentialsQuery>
{
    public ValidateStorageAccountCredentialsQueryValidator()
    {
        RuleFor(command => new StorageAccountCredentials(command.AccountName, command.AccountKey))
            .SetValidator(new StorageAccountCredentialsValidator());
    }
}

internal class ValidateStorageAccountCredentialsQueryHandler : IRequestHandler<ValidateStorageAccountCredentialsQuery, bool>
{
    private readonly IStorageAccountFactory storageAccountFactory;

    public ValidateStorageAccountCredentialsQueryHandler(IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
    }

    public async Task<bool> Handle(ValidateStorageAccountCredentialsQuery request, CancellationToken cancellationToken)
    {
        await new ValidateStorageAccountCredentialsQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        try
        {
            var credentials = new StorageAccountCredentials(request.AccountName, request.AccountKey);
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