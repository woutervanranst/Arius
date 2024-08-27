using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
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
    private readonly IFileSystem                    fileSystem;
    private readonly IStateDbRepositoryFactory      stateDbRepositoryFactory;
    private readonly ILogger<ArchiveCommandHandler> logger;

    public ArchiveCommandHandler(
        IFileSystem fileSystem,
        IStateDbRepositoryFactory stateDbRepositoryFactory, 
        ILogger<ArchiveCommandHandler> logger)
    {
        this.fileSystem               = fileSystem;
        this.stateDbRepositoryFactory = stateDbRepositoryFactory;
        this.logger                   = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        // Download latest state database
        var stateDbRepository = await stateDbRepositoryFactory.CreateAsync(request.Repository);

        //Index the request.LocalRoot

        //var downloadStateDbCommand = new DownloadStateDbCommand
        //{
        //    Repository = request.Repository,
        //    LocalPath  = request.LocalRoot.FullName
        //};

        //await mediator.Send(downloadStateDbCommand);



        throw new NotImplementedException();

    }

    private IEnumerable<(PointerFile? pf, BinaryFile? bf)> EnumerateAllowsFiles(DirectoryInfo root)
    {
        var seenFiles = new HashSet<string>();

        foreach (var file in fileSystem.EnumerateFiles(root))
        {
            if (!seenFiles.Add(file.BinaryFileFullName))
                continue;

            if (file.IsPointerFile)
            {
                // PointerFile exists
                var pf = file.GetPointerFile();

                if (pf.GetBinaryFile() is { Exists: true } bf)
                {
                    // BinaryFile exists too
                    yield return (pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return (pf, null);
                }
            }
            else
            {
                // BinaryFile exists
                var bf = file.GetBinaryFile();

                if (bf.GetPointerFile() is { Exists : true } pf)
                {
                    // PointerFile exists too
                    yield return (pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return (null, bf);
                }
            }
        }
    }
}

