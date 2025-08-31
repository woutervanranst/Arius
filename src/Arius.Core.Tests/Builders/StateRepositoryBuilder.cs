using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using static Arius.Core.Repositories.StateRepositoryDbContext;

namespace Arius.Core.Tests.Builders;

internal class StateRepositoryBuilder
{
    private readonly List<BinaryPropertiesDto> binaryProperties = [];
    private          BinaryPropertiesDto?      currentBinaryProperty;

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
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

    public StateRepositoryBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
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

    public StateRepositoryBuilder WithPointerFileEntry(string relativeName, DateTime? creationTime = null, DateTime? writeTime = null)
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

    public IStateRepository BuildInMemory()
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

    public IStateRepository BuildOnDisk(string path, string stateName)
    {
        var stateFile = new FileInfo(Path.Combine(path, $"{stateName}.db"));
        var stateRepo = new StateRepository(stateFile, true, NullLogger<StateRepository>.Instance);

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

        stateRepo.AddBinaryProperties(binaryPropertiesDtos);

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

        stateRepo.UpsertPointerFileEntries(pointerFileEntryDtos);

        return stateRepo;
    }
}