using Azure.Storage.Blobs.Models;
using FluentValidation;
using System;
using System.IO;
using Arius.Core.Services;

namespace Arius.Core.Commands.Archive;

public interface IArchiveCommandOptions :  // the interface is public, the implementation internal
    IRepositoryOptions
    //,
    //IHashValueProvider.IOptions
{
    string AccountName { get; }
    string AccountKey { get; }
    string Container { get; }
    string Passphrase { get; }
    bool FastHash { get; }
    bool RemoveLocal { get; }
    AccessTier Tier { get; }
    bool Dedup { get; }
    public DirectoryInfo Path { get; }
    public DateTime VersionUtc { get; }

    public int IndexBlock_Parallelism => 1; //8 * 2; // Environment.ProcessorCount; //index AND hash options

    public int BinariesToUpload_BufferSize => 1000;

    public int UploadBinaryFileBlock_BinaryFileParallelism => 1; // 16 * 2;
    public int TransferChunked_ChunkBufferSize => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    public int TransferChunked_ParallelChunkTransfers => 128 * 2;

    public int PointersToCreate_BufferSize => 1000;

    public int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    public int PointerFileEntriesToCreate_BufferSize => 1000;

    public int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    public int BinariesToDelete_BufferSize => 1000;

    public int DeleteBinaryFilesBlock_Parallelism => 1;

    public int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;
}

internal class IArchiveCommandValidator : AbstractValidator<IArchiveCommandOptions>
{
    public IArchiveCommandValidator()
    {
        RuleFor(o => o.Path)
            .NotEmpty()
            .Custom((path, context) =>
            {
                if (!path.Exists)
                    context.AddFailure($"Directory {path} does not exist.");
            });
        RuleFor(o => o.Tier).Must(tier =>
            tier == AccessTier.Hot ||
            tier == AccessTier.Cool ||
            tier == AccessTier.Archive);
    }
}
