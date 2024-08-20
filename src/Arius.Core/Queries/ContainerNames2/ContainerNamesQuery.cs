using Arius.Core.Facade;
using MediatR;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arius.Core.Domain.Storage;
using FluentValidation;

namespace Arius.Core.Queries.ContainerNames;

public record ContainerNamesQuery : IRequest<IAsyncEnumerable<string>>
{
    public required StorageAccountCredentials StorageAccountCredentials { get; init; }
    public required int                       MaxRetries  { get; init; }
}

internal class ContainerNamesQueryValidator : AbstractValidator<ContainerNamesQuery>
{
    public ContainerNamesQueryValidator()
    {
        RuleFor(query => query.StorageAccountCredentials)
            .SetValidator(new StorageAccountCredentialsValidator());

        RuleFor(query => query.MaxRetries)
            .GreaterThanOrEqualTo(0)
            .WithMessage("MaxRetries must be greater than or equal to 0.");
    }
}



internal class ContainerNamesQueryHandler : IRequestHandler<ContainerNamesQuery, IAsyncEnumerable<string>>
{
    private readonly ILogger<ContainerNamesQueryHandler> logger;

    public ContainerNamesQueryHandler(ILogger<ContainerNamesQueryHandler> logger)
    {
        this.logger = logger;
    }

    public async Task<IAsyncEnumerable<string>> Handle(ContainerNamesQuery request, CancellationToken cancellationToken)
    {
        await new ContainerNamesQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        return GetContainerNames(cancellationToken);


        async IAsyncEnumerable<string> GetContainerNames([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var sao = new StorageAccountOptions(request.StorageAccountCredentials.AccountName, request.StorageAccountCredentials.AccountKey);

            var bsc = sao.GetBlobServiceClient(request.MaxRetries, TimeSpan.FromSeconds(5));

            await foreach (var container in bsc.GetBlobContainersAsync(cancellationToken: cancellationToken))
            {
                yield return container.Name;
            }
        }
    }
}