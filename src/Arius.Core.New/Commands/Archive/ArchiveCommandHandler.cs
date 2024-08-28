using System.Threading.Channels;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Services;
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

        // 1. Index the request.LocalRoot
        var filesToHash = GetBoundedChannel<FilePair>(request.FilesToHash_BufferSize, true);
        var indexTask = Task.Run(async () =>
        {
            foreach (var fp in IndexFiles(fileSystem, request.LocalRoot))
            {
                await filesToHash.Writer.WriteAsync(fp, cancellationToken);
            }

            filesToHash.Writer.Complete();
        }, cancellationToken);

        // 2. Hash the filepairs
        var binariesToUpload           = GetBoundedChannel<FilePairWithHash>(request.BinariesToUpload_BufferSize, false);
        var pointerFileEntriesToCreate = GetBoundedChannel<PointerFileWithHash>(request.PointerFileEntriesToCreate_BufferSize, false);
        var hvp                        = new SHA256Hasher(request.Repository.Passphrase);
        var hashTask = Parallel.ForEachAsync(
            filesToHash.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.Hash_Parallelism),
            async (pair, ct) =>
            {
                var filePairWithHash = await HashFilesAsync(request.FastHash, hvp, pair);

                var pfwh = CreatePointerIfNotExist(filePairWithHash);

                if (filePairWithHash.BinaryFile is not null)
                    // There is a binary file that may need to be uploaded
                    await binariesToUpload.Writer.WriteAsync(filePairWithHash, ct);
                else if (filePairWithHash.BinaryFile is null && filePairWithHash.PointerFile is not null)
                    // There is only a pointerfileentry to be created
                    await pointerFileEntriesToCreate.Writer.WriteAsync(filePairWithHash.PointerFile, ct);
            });

        hashTask.ContinueWith(_ => binariesToUpload.Writer.Complete());

        var stateDbRepository = await stateDbRepositoryTask;

        // 3. Upload the binaries that are not present on the remote
        var uploadTask = Parallel.ForEachAsync(
            binariesToUpload.Reader.ReadAllAsync(cancellationToken),
            GetParallelOptions(request.UploadBinaryFileBlock_BinaryFileParallelism),
            async (pair, ct) =>
            {
                // if not present on the remote
                if (await stateDbRepository.BinaryExistsAsync(pair.BinaryFile.Hash))
                {
                    // TODO binariesThatWillBeUploaded -- 
                    //await stateDbRepository.UploadBinaryFileAsync(bfwh);
                }
                
                await pointerFileEntriesToCreate.Writer.WriteAsync(pair.PointerFile, ct);
            });

        Task.WhenAll(hashTask, uploadTask).ContinueWith(_ => pointerFileEntriesToCreate.Writer.Complete());

        await Task.WhenAll(indexTask, hashTask);


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

    internal record FilePairWithHash(PointerFileWithHash? PointerFile, BinaryFileWithHash? BinaryFile)
    {
        public static implicit operator FilePair(FilePairWithHash filePairWithHash)
        {
            return new FilePair(filePairWithHash.PointerFile, filePairWithHash.BinaryFile);
        }
    }

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

    internal static async Task<FilePairWithHash> HashFilesAsync(bool fastHash, IHashValueProvider hvp, FilePair pair)
    {
        if (pair.PointerFile is not null && pair.BinaryFile is not null)
        {
            // A PointerFile with corresponding BinaryFile
            if (fastHash)
            {
                // FastHash option - take the hash from the PointerFile
                if (pair.PointerFile.LastWriteTimeUtc == pair.BinaryFile.LastWriteTimeUtc)
                {
                    // The file has not been modified
                    var pfwh0 = pair.PointerFile.GetPointerFileWithHash();
                    var bfwh0 = pair.BinaryFile.GetBinaryFileWithHash(pfwh0.Hash);

                    return new(pfwh0, bfwh0);
                }
            }

            // Fasthash is off, or the File has been modified
            var h1    = await hvp.GetHashAsync(pair.BinaryFile);
            var bfwh1 = pair.BinaryFile.GetBinaryFileWithHash(h1);

            var pfwh1 = pair.PointerFile.GetPointerFileWithHash();
            if (pfwh1.Hash != bfwh1.Hash)
                throw new InvalidOperationException($"The PointerFile {pfwh1} is not valid for the BinaryFile '{bfwh1.FullName}' (BinaryHash does not match). Has the BinaryFile been updated? Delete the PointerFile and try again.");

            return new(pfwh1, bfwh1);
        }
        else if (pair.PointerFile is not null && pair.BinaryFile is null)
        {
            // A PointerFile without a BinaryFile
            var pfwh = pair.PointerFile.GetPointerFileWithHash();

            return new(pfwh, null);
        }
        else if (pair.PointerFile is null && pair.BinaryFile is not null)
        {
            // A BinaryFile without a PointerFile
            var h = await hvp.GetHashAsync(pair.BinaryFile);
            var bfwh = pair.BinaryFile.GetBinaryFileWithHash(h);

            return new(null, bfwh);
        }
        else
            throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    }

    internal static PointerFileWithHash CreatePointerIfNotExist(FilePairWithHash pair)
    {
        if (pair.PointerFile is not null && pair.BinaryFile is not null)
        {
            // A PointerFile with corresponding BinaryFile
            return pair.PointerFile;
        }
        else if (pair.PointerFile is not null && pair.BinaryFile is null)
        {
            // A PointerFile without a BinaryFile
            return pair.PointerFile;
        }
        else if (pair.PointerFile is null && pair.BinaryFile is not null)
        {
            // A BinaryFile without a PointerFile
            var pfwh = pair.BinaryFile.GetPointerFileWithHash();
            pfwh.Save();

            return pfwh;
        }
        else
            throw new InvalidOperationException("Both PointerFile and BinaryFile are null");
    }
}