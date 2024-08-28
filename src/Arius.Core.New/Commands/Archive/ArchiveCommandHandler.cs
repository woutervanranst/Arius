using System.Threading.Channels;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage.FileSystem;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Commands.Archive;

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
        var stateDbRepositoryTask = Task.Run(async () => await stateDbRepositoryFactory.CreateAsync(request.Repository), cancellationToken);

        // Index the request.LocalRoot
        var filesToHash = GetBoundedChannel<FilePair>(request.FilesToHash_BufferSize, true);
        var indexTask = Task.Run(async () =>
        {
            foreach (var fp in IndexFiles(fileSystem, request.LocalRoot))
            {
                await filesToHash.Writer.WriteAsync(fp, cancellationToken);
                await Task.Delay(1000000);
            }

            filesToHash.Writer.Complete();
        }, cancellationToken);

        // Hash the filepairs
        var hashTask = Parallel.ForEachAsync(
            filesToHash.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.Hash_Parallelism),  async (pair, token) =>
            {
                await Task.CompletedTask;
            });

        await Task.WhenAll(indexTask, stateDbRepositoryTask);

        var stateDbRepository = await stateDbRepositoryTask;

        return;


        static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter)
        {
            return Channel.CreateBounded<T>(GetBoundedChannelOptions(capacity, singleWriter));
        }
        
        static BoundedChannelOptions GetBoundedChannelOptions(int capacity, bool singleWriter)
        {
            return new BoundedChannelOptions(capacity)
            {
                FullMode                      = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
                SingleWriter                  = singleWriter,
                SingleReader                  = false
            };
        }

        ParallelOptions GetParallelOptions(int maxDegreeOfParallelism)
        {
            return new ParallelOptions() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };
        }
    }

    internal record FilePair(PointerFile? PointerFile, BinaryFile? BinaryFile);

    internal static IEnumerable<FilePair> IndexFiles(IFileSystem fileSystem, DirectoryInfo root)
    {
        var seenFiles        = new HashSet<string>();
        var currentDirectory = root.FullName;

        foreach (var file in fileSystem.EnumerateFiles(root))
        {
            // Check if the directory has changed, if so clear the seenFiles HashSet
            if (!string.Equals(currentDirectory, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                seenFiles.Clear();
                currentDirectory = file.Path; // Update the current directory to the file's directory
            }

            if (!seenFiles.Add(file.BinaryFileFullName))
                continue;

            if (file.IsPointerFile)
            {
                // this is a PointerFile
                var pf = file.GetPointerFile(root);

                if (pf.GetBinaryFile(root) is { Exists: true } bf)
                {
                    // BinaryFile exists too
                    yield return new(pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return new(pf, null);
                }
            }
            else
            {
                // this is a BinaryFile
                var bf = file.GetBinaryFile(root);

                if (bf.GetPointerFile(root) is { Exists : true } pf)
                {
                    // PointerFile exists too
                    yield return new(pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return new(null, bf);
                }
            }
        }
    }
}