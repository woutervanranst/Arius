using Azure.Storage.Blobs.Models;
using FluentValidation;
using System;
using System.IO;

namespace Arius.Core.Commands.Archive;

internal class ArchiveCommandOptions : 
    ServicesOptions,
    Facade.Facade.IOptions
{
    internal ArchiveCommandOptions(string accountName, string accountKey, string passphrase, bool fastHash, string container, bool removeLocal, string tier, bool dedup, string path, DateTime versionUtc)
        : base(accountName, accountKey, container, passphrase)
    {
        FastHash = fastHash;
        RemoveLocal = removeLocal;
        Tier = tier;
        Dedup = dedup;
        Path = path;
        VersionUtc = versionUtc;
        var validator = new Validator();
        validator.ValidateAndThrow(this);
    }

    public bool FastHash { get; private init; }
    public bool RemoveLocal { get; private init; }
    public AccessTier Tier { get; private init; }
    public bool Dedup { get; private init; }
    public string Path { get; private init; }
    public DateTime VersionUtc { get; private init; }

    public int IndexBlock_Parallelism => 8 * 2; // Environment.ProcessorCount; //index AND hash options

    public int BinariesToUpload_BufferSize => 1000;

    public int UploadBinaryFileBlock_BinaryFileParallelism => 16 * 2;
    public int TransferChunked_ChunkBufferSize => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    public int TransferChunked_ParallelChunkTransfers => 128 * 2;

    public int PointersToCreate_BufferSize => 1000;
    
    public int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    public int PointerFileEntriesToCreate_BufferSize => 1000;

    public int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    public int BinariesToDelete_BufferSize => 1000;
    
    public int DeleteBinaryFilesBlock_Parallelism => 1;

    public int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;


    private class Validator : AbstractValidator<ArchiveCommandOptions>
    {
        public Validator()
        {
            RuleFor(o => o.Path)
                .NotEmpty()
                .Custom((path, context) =>
                {
                    if (!Directory.Exists(path))
                        context.AddFailure($"Directory {path} does not exist.");
                });
            RuleFor(o => o.Tier).Must(tier =>
                tier == AccessTier.Hot ||
                tier == AccessTier.Cool ||
                tier == AccessTier.Archive);
        }
    }
}