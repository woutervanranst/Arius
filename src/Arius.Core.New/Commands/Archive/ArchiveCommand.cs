using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.DownloadStateDb;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Commands.Archive;

public record ArchiveCommand : IRequest
{
    public required RepositoryOptions Repository  { get; init; }
    public required bool              FastHash    { get; init; }
    public required bool              RemoveLocal { get; init; }
    public required StorageTier       Tier        { get; init; }
    public required DirectoryInfo     LocalRoot   { get; init; }
    public required RepositoryVersion VersionName { get; init; }


    internal int IndexBlock_Parallelism => Environment.ProcessorCount * 8; //index AND hash options. A low count doesnt achieve a high throughput when there are a lot of small files

    internal int BinariesToUpload_BufferSize => 100; //apply backpressure if we cannot upload fast enough

    internal int UploadBinaryFileBlock_BinaryFileParallelism => Environment.ProcessorCount * 2;
    internal int TransferChunked_ChunkBufferSize             => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    internal int TransferChunked_ParallelChunkTransfers      => 128; // 128 * 2; -- NOTE sep22 this was working before but now getting ResourceUnavailable errors --> throttling?

    internal int PointersToCreate_BufferSize => 1000;

    internal int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    internal int PointerFileEntriesToCreate_BufferSize => 1000;

    internal int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    internal int BinariesToDelete_BufferSize => 1000;

    internal int DeleteBinaryFilesBlock_Parallelism => 1;

    internal int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;

    internal int UpdateTierBlock_Parallelism => 10;
}

internal class ArchiveCommandValidator : AbstractValidator<ArchiveCommand>
{
    public ArchiveCommandValidator()
    {
        RuleFor(command => command.Repository).SetValidator(new RepositoryOptionsValidator());
        RuleFor(command => command.LocalRoot)
            .NotEmpty()
            .Must(localRoot => localRoot.Exists).WithMessage("The local root does not exist.");
    }
}

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly IMediator                      mediator;
    private readonly ILogger<ArchiveCommandHandler> logger;

    public ArchiveCommandHandler(IMediator mediator, ILogger<ArchiveCommandHandler> logger)
    {
        this.mediator = mediator;
        this.logger   = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        // Download latest state database
        var downloadStateDbCommand = new DownloadStateDbCommand
        {
            Repository = request.Repository,
            LocalPath  = request.LocalRoot.FullName
        };

        await mediator.Send(downloadStateDbCommand);
    }
}