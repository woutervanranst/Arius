using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Tests.Helpers.Builders;

internal class StateRepositoryBuilder
{
    private readonly List<BinaryProperties> binaryProperties = [];

    public class PointerFileEntryBuilder
    {
        internal readonly List<PointerFileEntry> PointerFileEntries = [];

        public PointerFileEntryBuilder WithPointerFileEntry(string binaryFileRelativeName, DateTime? creationTime = null, DateTime? writeTime = null)
        {
            if (binaryFileRelativeName.EndsWith(PointerFile.Extension))
                throw new ArgumentException($"BinaryFileRelativeName must not end with '{PointerFile.Extension}'", nameof(binaryFileRelativeName));

            PointerFileEntries.Add(new PointerFileEntry
            {
                RelativeName     = $"{binaryFileRelativeName}{PointerFile.Extension}",
                CreationTimeUtc  = creationTime,
                LastWriteTimeUtc = writeTime
            });

            return this;
        }
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, Action<PointerFileEntryBuilder> pointerFileEntries)
    {
        return WithBinaryProperty(hash, originalSize, null, null, pointerFileEntries);
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize, Action<PointerFileEntryBuilder> pointerFileEntries)
    {
        return WithBinaryProperty(hash, originalSize, archivedSize, null, pointerFileEntries);
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null, Action<PointerFileEntryBuilder>? pointerFileEntries = null)
    {
        var pointerFileBuilder = new PointerFileEntryBuilder();
        pointerFileEntries?.Invoke(pointerFileBuilder);

        var binaryProperty = new BinaryProperties
        {
            Hash               = hash,
            ParentHash         = null,
            OriginalSize       = originalSize,
            ArchivedSize       = archivedSize,
            StorageTier        = storageTier,
            PointerFileEntries = pointerFileBuilder.PointerFileEntries
        };
        
        binaryProperties.Add(binaryProperty);
        return this;
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, Action<PointerFileEntryBuilder> pointerFileEntries)
    {
        return WithBinaryProperty(hash, parentHash, originalSize, null, null, pointerFileEntries);
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize, Action<PointerFileEntryBuilder> pointerFileEntries)
    {
        return WithBinaryProperty(hash, parentHash, originalSize, archivedSize, null, pointerFileEntries);
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null, Action<PointerFileEntryBuilder>? pointerFileEntries = null)
    {
        var pointerFileBuilder = new PointerFileEntryBuilder();
        pointerFileEntries?.Invoke(pointerFileBuilder);

        var binaryProperty = new BinaryProperties
        {
            Hash               = hash,
            ParentHash         = parentHash,
            OriginalSize       = originalSize,
            ArchivedSize       = archivedSize,
            StorageTier        = storageTier,
            PointerFileEntries = pointerFileBuilder.PointerFileEntries
        };
        
        binaryProperties.Add(binaryProperty);
        return this;
    }

    public IStateRepository BuildFake()
    {
        var repository = new InMemoryStateRepository();

        // Add all binary properties
        var binaryPropertiesDtos = binaryProperties
            .Select(bp => new BinaryProperties
            {
                Hash               = bp.Hash,
                ParentHash         = bp.ParentHash,
                OriginalSize       = bp.OriginalSize,
                ArchivedSize       = bp.ArchivedSize,
                StorageTier        = bp.StorageTier,
                PointerFileEntries = []
            })
            .ToArray();

        repository.AddBinaryProperties(binaryPropertiesDtos);

        // Add all pointer file entries
        var pointerFileEntryDtos = binaryProperties
            .SelectMany(bp => bp.PointerFileEntries.Select(pfe => new PointerFileEntry
            {
                Hash             = bp.Hash,
                RelativeName     = pfe.RelativeName,
                CreationTimeUtc  = pfe.CreationTimeUtc,
                LastWriteTimeUtc = pfe.LastWriteTimeUtc,
                BinaryProperties = null!
            }))
            .ToArray();

        repository.UpsertPointerFileEntries(pointerFileEntryDtos);

        return repository;
    }

    public IStateRepository Build(string path, string stateName)
    {
        var stateFile   = new FileInfo(Path.Combine(path, $"{stateName}.db"));
        var contextPool = new StateRepositoryDbContextPool(stateFile, true, NullLogger<StateRepositoryDbContextPool>.Instance);
        var stateRepo   = new StateRepository(contextPool);

        // Add all binary properties
        var bps = binaryProperties
            .Select(bp => new BinaryProperties
            {
                Hash               = bp.Hash,
                ParentHash         = bp.ParentHash,
                OriginalSize       = bp.OriginalSize,
                ArchivedSize       = bp.ArchivedSize,
                StorageTier        = bp.StorageTier,
                PointerFileEntries = []
            })
            .ToArray();

        stateRepo.AddBinaryProperties(bps);

        // Add all pointer file entries
        var pfes = binaryProperties
            .SelectMany(bp => bp.PointerFileEntries.Select(pfe => new PointerFileEntry
            {
                Hash             = bp.Hash,
                RelativeName     = pfe.RelativeName,
                CreationTimeUtc  = pfe.CreationTimeUtc,
                LastWriteTimeUtc = pfe.LastWriteTimeUtc,
                BinaryProperties = null!
            }))
            .ToArray();

        stateRepo.UpsertPointerFileEntries(pfes);

        return stateRepo;
    }
}