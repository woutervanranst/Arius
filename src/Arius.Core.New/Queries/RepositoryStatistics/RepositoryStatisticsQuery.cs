using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Queries.RepositoryStatistics;

public record RepositoryStatisticsQuery : IRequest<RepositoryStatisticsQueryResponse>
{
    public required CloudRepositoryOptions  CloudRepository { get; init; }
    public          RepositoryVersion? Version    { get; init; }
}

internal class RepositoryStatisticsQueryValidator : AbstractValidator<RepositoryStatisticsQuery>
{
    public RepositoryStatisticsQueryValidator()
    {
        RuleFor(command => command.CloudRepository).SetValidator(new RepositoryOptionsValidator());
    }
}

public record RepositoryStatisticsQueryResponse
{
    public required long BinaryFilesCount       { get; init; }
    public required long ArchiveSize            { get; init; }
    public required long PointerFilesEntryCount { get; init; }
}

internal class RepositoryStatisticsQueryHandler : IRequestHandler<RepositoryStatisticsQuery, RepositoryStatisticsQueryResponse>
{
    private readonly IRemoteStateRepository remoteStateRepository;

    public RepositoryStatisticsQueryHandler(IRemoteStateRepository remoteStateRepository)
    {
        this.remoteStateRepository = remoteStateRepository;
    }

    public async Task<RepositoryStatisticsQueryResponse> Handle(RepositoryStatisticsQuery request, CancellationToken cancellationToken)
    {
        await new RepositoryStatisticsQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        var stateDbRepository = await remoteStateRepository.CreateAsync(request.CloudRepository, request.Version);

        var binaryFilesCount       = stateDbRepository.CountBinaryProperties();
        var archiveSize            = stateDbRepository.GetArchiveSize();
        var pointerFilesEntryCount = stateDbRepository.CountPointerFileEntries();

        return new RepositoryStatisticsQueryResponse
        {
            BinaryFilesCount       = binaryFilesCount,
            ArchiveSize            = archiveSize,
            PointerFilesEntryCount = pointerFilesEntryCount
        };
    }
}