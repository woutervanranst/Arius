using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Tests.Builders;

public class FakeStateDatabaseBuilder
{
    private readonly string                   path;
    private readonly string                   stateName;
    private readonly List<BinaryPropertyData> binaryProperties = [];
    private          BinaryPropertyData?      currentBinaryProperty;

    public FakeStateDatabaseBuilder(string path, string stateName)
    {
        this.path      = path;
        this.stateName = stateName;
    }

    public FakeStateDatabaseBuilder WithBinaryProperty(Hash hash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryPropertyData
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

    public FakeStateDatabaseBuilder WithBinaryProperty(Hash hash, Hash parentHash, long originalSize, long? archivedSize = null, StorageTier? storageTier = null)
    {
        currentBinaryProperty = new BinaryPropertyData
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

    public FakeStateDatabaseBuilder WithPointerFileEntry(string relativeName, DateTime? creationTime = null, DateTime? writeTime = null)
    {
        if (currentBinaryProperty == null)
            throw new InvalidOperationException("Must add a binary property before adding pointer file entries");

        currentBinaryProperty.PointerFileEntries.Add(new PointerFileEntryData
        {
            RelativeName     = relativeName,
            CreationTimeUtc  = creationTime,
            LastWriteTimeUtc = writeTime
        });

        return this;
    }

    public FileInfo Build()
    {
        var stateFile = new FileInfo(Path.Combine(path, $"{stateName}.db"));
        var stateRepo = new StateRepository(stateFile, NullLogger<StateRepository>.Instance);

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

        return stateFile;
    }

    private class BinaryPropertyData
    {
        public Hash                       Hash               { get; set; }
        public Hash?                      ParentHash         { get; set; }
        public long                       OriginalSize       { get; set; }
        public long?                      ArchivedSize       { get; set; }
        public StorageTier?               StorageTier        { get; set; }
        public List<PointerFileEntryData> PointerFileEntries { get; set; } = [];
    }

    private class PointerFileEntryData
    {
        public string    RelativeName     { get; set; } = string.Empty;
        public DateTime? CreationTimeUtc  { get; set; }
        public DateTime? LastWriteTimeUtc { get; set; }
    }
}