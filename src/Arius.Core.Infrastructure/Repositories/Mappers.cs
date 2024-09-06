using Arius.Core.Domain;

namespace Arius.Core.Infrastructure.Repositories;

internal static class Mappers
{
    public static PointerFileEntryDto ToDto(this PointerFileEntry pfe)
    {
        return new PointerFileEntryDto
        {
            Hash             = pfe.Hash.Value,
            RelativeName     = pfe.RelativeName,
            CreationTimeUtc  = pfe.CreationTimeUtc,
            LastWriteTimeUtc = pfe.LastWriteTimeUtc,
            //BinaryProperties = pfe.BinaryProperties.ToDto()
        };
    }
    public static PointerFileEntry ToEntity(this PointerFileEntryDto pfe)
    {
        return new PointerFileEntry
        {
            Hash             = pfe.Hash,
            RelativeName     = pfe.RelativeName,
            CreationTimeUtc  = pfe.CreationTimeUtc ?? throw new ArgumentException($"{nameof(pfe.CreationTimeUtc)} is null"),
            LastWriteTimeUtc = pfe.LastWriteTimeUtc ?? throw new ArgumentException($"{nameof(pfe.LastWriteTimeUtc)} is null")
            //BinaryProperties = pfe.BinaryProperties.ToEntity()
        };
    }

    
    public static BinaryPropertiesDto ToDto(this BinaryProperties bp)
    {
        return new BinaryPropertiesDto
        {
            Hash              = bp.Hash.Value,
            OriginalLength    = bp.OriginalLength,
            ArchivedLength    = bp.ArchivedLength,
            IncrementalLength = bp.IncrementalLength,
            StorageTier       = bp.StorageTier,
            //PointerFileEntries = bp.PointerFileEntries.Select(pfe => pfe.ToDto()).ToList()
        };
    }
    public static BinaryProperties ToEntity(this BinaryPropertiesDto bp)
    {
        return new BinaryProperties
        {
            Hash              = bp.Hash,
            OriginalLength    = bp.OriginalLength,
            ArchivedLength    = bp.ArchivedLength,
            IncrementalLength = bp.IncrementalLength,
            StorageTier       = bp.StorageTier,
            //PointerFileEntries = bp.PointerFileEntries.Select(pfe => pfe.ToEntity()).ToList()
        };
    }
}