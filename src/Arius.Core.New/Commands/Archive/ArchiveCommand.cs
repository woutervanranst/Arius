using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Commands.Archive;

public record ArchiveCommand : IRequest
{
    public required RepositoryOptions  Repository  { get; init; }
    public required bool               FastHash    { get; init; }
    public required bool               RemoveLocal { get; init; }
    public required StorageTier        Tier        { get; init; }
    public required DirectoryInfo      LocalRoot   { get; init; }
    public          RepositoryVersion? VersionName { get; init; }

    internal int FilesToHash_BufferSize => 1000;

    internal int Hash_Parallelism => Environment.ProcessorCount * 8; // A low count doesnt achieve a high throughput when there are a lot of small files




    internal int BinariesToUpload_BufferSize => 100; //apply backpressure if we cannot upload fast enough

    internal int UploadBinaryFileBlock_BinaryFileParallelism => Environment.ProcessorCount * 2;

    internal int PointersToCreate_BufferSize => 1000;

    internal int CreatePointerFileIfNotExistsBlock_Parallelism => 1;

    internal int PointerFileEntriesToCreate_BufferSize => 1000;

    internal int CreatePointerFileEntryIfNotExistsBlock_Parallelism => 1;

    internal int BinariesToDelete_BufferSize => 1000;

    internal int DeleteBinaryFilesBlock_Parallelism => 1;

    internal int CreateDeletedPointerFileEntryForDeletedPointerFilesBlock_Parallelism => 1;

    internal int UpdateTierBlock_Parallelism => 10;

    internal readonly Dictionary<long, StorageTier> storageTiering = new()
    {
        { 1024L * 1024, StorageTier.Cold },    // Files less than 1MB -> Cool Tier
    };
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