using Azure.Storage.Blobs.Models;
using FluentValidation;
using System;
using System.IO;
using Arius.Core.Services;

namespace Arius.Core.Commands.Archive;

public interface IArchiveCommandOptions : IRepositoryOptions // the interface is public, the implementation internal
{
    bool FastHash { get; }
    bool RemoveLocal { get; }
    AccessTier Tier { get; }
    bool Dedup { get; }
    DirectoryInfo Path { get; }
    DateTime VersionUtc { get; }


    int IndexBlock_Parallelism => 1; //8 * 2; // Environment.ProcessorCount; //index AND hash options

    int BinariesToUpload_BufferSize => 1000;

    int UploadBinaryFileBlock_BinaryFileParallelism => 1; // 16 * 2;
    int TransferChunked_ChunkBufferSize => 1024; //put lower on systems with low memory -- if unconstrained, it will load all the BinaryFiles in memory
    int TransferChunked_ParallelChunkTransfers => 128 * 2;

    int PointersToCreate_BufferSize => 1000;

    int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    int PointerFileEntriesToCreate_BufferSize => 1000;

    int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    int BinariesToDelete_BufferSize => 1000;

    int DeleteBinaryFilesBlock_Parallelism => 1;

    int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;


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
                .NotEmpty()
                .Custom((path, context) =>
                {
                    if (path is not DirectoryInfo)
                        context.AddFailure("Path must be a directory");
                    if (!path.Exists)
                        context.AddFailure($"Directory {path} does not exist.");
                });

            // Validate Tier
            RuleFor(o => o.Tier)
                .Must(tier =>
                    tier == AccessTier.Hot ||
                    tier == AccessTier.Cool ||
                    tier == AccessTier.Archive);
        }
    }
}