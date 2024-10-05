using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace Arius.Core.New.Queries.RepositoryStatistics;

public record RepositoryStatisticsQuery : IRequest<RepositoryStatisticsQueryResponse>
{
    public required RemoteRepositoryOptions RemoteRepository { get; init; }
    public          StateVersion?           Version          { get; init; }
}

internal class RepositoryStatisticsQueryValidator : AbstractValidator<RepositoryStatisticsQuery>
{
    public RepositoryStatisticsQueryValidator()
    {
        RuleFor(command => command.RemoteRepository).SetValidator(new RepositoryOptionsValidator());
    }
}

public record RepositoryStatisticsQueryResponse
{
    public required long        BinaryFilesCount       { get; init; }
    public required long        PointerFilesEntryCount { get; init; }
    public required SizeMetrics Sizes                  { get; init; }
}

internal class RepositoryStatisticsQueryHandler : IRequestHandler<RepositoryStatisticsQuery, RepositoryStatisticsQueryResponse>
{
    private readonly IStorageAccountFactory storageAccountFactory;
    private readonly AriusConfiguration     config;

    public RepositoryStatisticsQueryHandler(
        IOptions<AriusConfiguration> config,
        IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
        this.config                = config.Value;
    }

    public async Task<RepositoryStatisticsQueryResponse> Handle(RepositoryStatisticsQuery request, CancellationToken cancellationToken)
    {
        await new RepositoryStatisticsQueryValidator().ValidateAndThrowAsync(request, cancellationToken);


        var remoteRepository = storageAccountFactory.GetRemoteRepository(request.RemoteRepository);
        var remoteStateRepository = remoteRepository.GetRemoteStateRepository();

        var localStateDatabaseCacheDirectory = config.GetLocalStateDatabaseCacheDirectoryForContainerName(request.RemoteRepository.ContainerName);
        var localStateRepository = await remoteStateRepository.GetLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, request.Version);

        var binaryFilesCount       = localStateRepository.CountBinaryProperties();
        var pointerFilesEntryCount = localStateRepository.CountPointerFileEntries();
        var sizes                  = localStateRepository.GetSizes();

        return new RepositoryStatisticsQueryResponse
        {
            BinaryFilesCount       = binaryFilesCount,
            PointerFilesEntryCount = pointerFilesEntryCount,
            Sizes                  = sizes
        };
    }
}