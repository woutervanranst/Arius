using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Queries.RepositoryStatistics;

public record RepositoryStatisticsQuery : IRequest<RepositoryStatisticsQueryResponse>
{
    public required RemoteRepositoryOptions  RemoteRepository { get; init; }
    public          RepositoryVersion? Version    { get; init; }
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
    private readonly IRemoteStateRepository remoteStateRepository;

    public RepositoryStatisticsQueryHandler(IRemoteStateRepository remoteStateRepository)
    {
        this.remoteStateRepository = remoteStateRepository;
    }

    public async Task<RepositoryStatisticsQueryResponse> Handle(RepositoryStatisticsQuery request, CancellationToken cancellationToken)
    {
        await new RepositoryStatisticsQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        var stateDbRepository = await remoteStateRepository.CreateAsync(request.RemoteRepository, request.Version);

        var binaryFilesCount       = stateDbRepository.CountBinaryProperties();
        var pointerFilesEntryCount = stateDbRepository.CountPointerFileEntries();
        var sizes                  = stateDbRepository.GetSizes();

        return new RepositoryStatisticsQueryResponse
        {
            BinaryFilesCount       = binaryFilesCount,
            PointerFilesEntryCount = pointerFilesEntryCount,
            Sizes = sizes
        };
    }
}