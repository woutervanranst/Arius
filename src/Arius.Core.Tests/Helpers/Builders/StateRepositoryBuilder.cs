using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Tests.Helpers.Builders;

internal class StateRepositoryBuilder
{
    private readonly List<BinaryProperties> binaryProperties = [];
    private          BinaryProperties?      currentBinaryProperty;

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryProperties
        {
            Hash               = hash,
            ParentHash         = null,
            OriginalSize       = originalSize,
            ArchivedSize       = archivedSize,
            StorageTier        = storageTier,
            PointerFileEntries = []
        };
        binaryProperties.Add(currentBinaryProperty);
        return this;
    }

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryProperties
        {
            Hash               = hash,
            ParentHash         = parentHash,
            OriginalSize       = originalSize,
            ArchivedSize       = archivedSize,
            StorageTier        = storageTier,
            PointerFileEntries = []
        };
        binaryProperties.Add(currentBinaryProperty);
        return this;
    }

    public StateRepositoryBuilder WithPointerFileEntry(string relativeName, DateTime? creationTime = null, DateTime? writeTime = null)
    {
        if (currentBinaryProperty == null)
            throw new InvalidOperationException("Must add a binary property before adding pointer file entries");

        currentBinaryProperty.PointerFileEntries.Add(new PointerFileEntry
        {
            RelativeName     = relativeName,
            CreationTimeUtc  = creationTime,
            LastWriteTimeUtc = writeTime
        });

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