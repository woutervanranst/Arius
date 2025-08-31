using Arius.Core.Models;
using Arius.Core.Repositories;

namespace Arius.Core.Tests.Builders;

internal class InMemoryStateRepositoryBuilder
{
    private readonly List<BinaryPropertiesDto> binaryProperties = [];
    private          BinaryPropertiesDto?      currentBinaryProperty;

    public InMemoryStateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryPropertiesDto
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

    public InMemoryStateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryPropertiesDto
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

    public InMemoryStateRepositoryBuilder WithPointerFileEntry(string relativeName, DateTime? creationTime = null, DateTime? writeTime = null)
    {
        if (currentBinaryProperty == null)
            throw new InvalidOperationException("Must add a binary property before adding pointer file entries");

        currentBinaryProperty.PointerFileEntries.Add(new PointerFileEntryDto
        {
            RelativeName     = relativeName,
            CreationTimeUtc  = creationTime,
            LastWriteTimeUtc = writeTime
        });

        return this;
    }

    public IStateRepository Build()
    {
        var repository = new InMemoryStateRepository();

        // Add all binary properties
        var binaryPropertiesDtos = binaryProperties
            .Select(bp => new BinaryPropertiesDto
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
            .SelectMany(bp => bp.PointerFileEntries.Select(pfe => new PointerFileEntryDto
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
}