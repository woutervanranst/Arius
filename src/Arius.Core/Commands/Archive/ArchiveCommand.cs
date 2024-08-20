using System;
using System.IO;
using Arius.Core.Facade;
using Azure.Storage.Blobs.Models;
using MediatR;

namespace Arius.Core.Commands.Archive;

internal record ArchiveCommand : RepositoryOptions //, IRequest<CommandResultStatus>
{
    public ArchiveCommand(RepositoryOptions options, DirectoryInfo root, bool fastHash, bool removeLocal, string tier, bool dedup, DateTime versionUtc) : base(options)
    {
        this.FastHash    = fastHash;
        this.RemoveLocal = removeLocal;
        this.Tier        = tier;
        this.Dedup       = dedup;
        this.Path        = root; // TODO rename to Root
        this.VersionUtc  = versionUtc;
    }
    public ArchiveCommand(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool fastHash, bool removeLocal, string tier, bool dedup, DateTime versionUtc) : base(accountName, accountKey, containerName, passphrase)
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


    public int IndexBlock_Parallelism => Environment.ProcessorCount * 8; //index AND hash options. A low count doesnt achieve a high throughput when there are a lot of small files

    public int BinariesToUpload_BufferSize => 100; //apply backpressure if we cannot upload fast enough

    public int UploadBinaryFileBlock_BinaryFileParallelism => Environment.ProcessorCount * 2;
    public int TransferChunked_ChunkBufferSize             => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    public int TransferChunked_ParallelChunkTransfers      => 128; // 128 * 2; -- NOTE sep22 this was working before but now getting ResourceUnavailable errors --> throttling?

    public int PointersToCreate_BufferSize => 1000;

    public int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    public int PointerFileEntriesToCreate_BufferSize => 1000;

    public int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    public int BinariesToDelete_BufferSize => 1000;

    public int DeleteBinaryFilesBlock_Parallelism => 1;

    public int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;

    public int UpdateTierBlock_Parallelism => 10;

    public override void Validate()
    {
        base.Validate();

        if (Path is null)
            throw new ArgumentException("Path is not specified", nameof(Path));
        if (Path is not DirectoryInfo)
            throw new ArgumentException("Path must be a directory", nameof(Path));
        if (!Path.Exists)
            throw new ArgumentException($"Directory {Path} does not exist.", nameof(Path));

        if (Tier != AccessTier.Hot && 
            Tier != AccessTier.Cold && 
            Tier != AccessTier.Cool && 
            Tier != AccessTier.Archive)
            throw new ArgumentException($"Tier {Tier} is not supported", nameof(Tier));

        //#pragma warning disable CS0108 // Member hides inherited member; missing new keyword -- not required
        //    internal class Validator : AbstractValidator<IArchiveCommandOptions>
        //#pragma warning restore CS0108
        //    {
        //        public Validator()
        //        {
        //            // validate the IRepositoryOptions (AccountName, AccountKey, Container, Passphrase)
        //            RuleFor(o => (IRepositoryOptions)o)
        //                .SetInheritanceValidator(v => 
        //                    v.Add<IRepositoryOptions>(new IRepositoryOptions.Validator()));

        //            // Validate Path
        //            RuleFor(o => o.Path)
        //                .Custom((path, context) =>
        //                {
        //                    if (path is null)
        //                        context.AddFailure("Path is not specified");
        //                    else if (path is not DirectoryInfo)
        //                        context.AddFailure("Path must be a directory");
        //                    else if (!path.Exists)
        //                        context.AddFailure($"Directory {path} does not exist.");
        //                });

        //            // Validate Tier
        //            RuleFor(o => o.Tier)
        //                .Must(tier =>
        //                    tier == AccessTier.Hot ||
        //                    tier == AccessTier.Cool ||
        //                    tier == AccessTier.Cold ||
        //                    tier == AccessTier.Archive);
        //        }
        //    }
    }
}