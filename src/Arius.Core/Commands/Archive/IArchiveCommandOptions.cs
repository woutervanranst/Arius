using Arius.Core.Facade;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using System;
using System.IO;

namespace Arius.Core.Commands.Archive;

internal interface IArchiveCommandOptions : IRepositoryOptions
{
    bool          FastHash    { get; }
    bool          RemoveLocal { get; }
    AccessTier    Tier        { get; }
    bool          Dedup       { get; }
    DirectoryInfo Path        { get; }
    DateTime      VersionUtc  { get; }


    int IndexBlock_Parallelism => Environment.ProcessorCount * 8; //index AND hash options. A low count doesnt achieve a high throughput when there are a lot of small files

    int BinariesToUpload_BufferSize => 100; //apply backpressure if we cannot upload fast enough

    int UploadBinaryFileBlock_BinaryFileParallelism => Environment.ProcessorCount * 2;
    int TransferChunked_ChunkBufferSize             => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    int TransferChunked_ParallelChunkTransfers      => 128; // 128 * 2; -- NOTE sep22 this was working before but now getting ResourceUnavailable errors --> throttling?

    int PointersToCreate_BufferSize => 1000;

    int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    int PointerFileEntriesToCreate_BufferSize => 1000;

    int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    int BinariesToDelete_BufferSize => 1000;

    int DeleteBinaryFilesBlock_Parallelism => 1;

    int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;

    int UpdateTierBlock_Parallelism => 10;


#pragma warning disable CS0108 // Member hides inherited member; missing new keyword -- not required
    internal class Validator : AbstractValidator<IArchiveCommandOptions>
#pragma warning restore CS0108
    {
        public Validator()
        {
            // validate the IRepositoryOptions (AccountName, AccountKey, Container, Passphrase)
            RuleFor(o => (IRepositoryOptions)o)
                .SetInheritanceValidator(v => 
                    v.Add<IRepositoryOptions>(new IRepositoryOptions.Validator()));
            
            // Validate Path
            RuleFor(o => o.Path)
                .Custom((path, context) =>
                {
                    if (path is null)
                        context.AddFailure("Path is not specified");
                    else if (path is not DirectoryInfo)
                        context.AddFailure("Path must be a directory");
                    else if (!path.Exists)
                        context.AddFailure($"Directory {path} does not exist.");
                });

            // Validate Tier
            RuleFor(o => o.Tier)
                .Must(tier =>
                    tier == AccessTier.Hot ||
                    tier == AccessTier.Cool ||
                    tier == AccessTier.Cold ||
                    tier == AccessTier.Archive);
        }
    }
}

internal record ArchiveCommandOptions : RepositoryOptions, IArchiveCommandOptions
{
    public ArchiveCommandOptions(IRepositoryOptions options, DirectoryInfo root, bool fastHash, bool removeLocal, AccessTier tier, bool dedup, DateTime versionUtc) : base(options)
    {
        this.FastHash    = fastHash;
        this.RemoveLocal = removeLocal;
        this.Tier        = tier;
        this.Dedup       = dedup;
        this.Path        = root; // TODO rename to Root
        this.VersionUtc  = versionUtc;
    }
    public ArchiveCommandOptions(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool fastHash, bool removeLocal, AccessTier tier, bool dedup, DateTime versionUtc) : base(accountName, accountKey, containerName, passphrase)
    {
        this.FastHash    = fastHash;
        this.RemoveLocal = removeLocal;
        this.Tier        = tier;
        this.Dedup       = dedup;
        this.Path        = root;
        this.VersionUtc  = versionUtc;
    }

    public bool          FastHash    { get; }
    public bool          RemoveLocal { get; }
    public AccessTier    Tier        { get; }
    public bool          Dedup       { get; }
    public DirectoryInfo Path        { get; }
    public DateTime      VersionUtc  { get; }
}