namespace Arius.Core.Domain;

public record SizeMetrics
{
    public SizeMetrics(long AllUniqueOriginalSize, long AllUniqueArchivedSize, long AllOriginalSize, long AllArchivedSize, long ExistingUniqueOriginalSize, long ExistingUniqueArchivedSize, long ExistingOriginalSize, long ExistingArchivedSize)
    {
        this.AllUniqueOriginalSize      = AllUniqueOriginalSize;
        this.AllUniqueArchivedSize      = AllUniqueArchivedSize;
        this.AllOriginalSize            = AllOriginalSize;
        this.AllArchivedSize            = AllArchivedSize;
        this.ExistingUniqueOriginalSize = ExistingUniqueOriginalSize;
        this.ExistingUniqueArchivedSize = ExistingUniqueArchivedSize;
        this.ExistingOriginalSize       = ExistingOriginalSize;
        this.ExistingArchivedSize       = ExistingArchivedSize;
    }

    public long AllUniqueOriginalSize      { get; }
    public long AllUniqueArchivedSize      { get; }
    public long AllOriginalSize            { get; }
    public long AllArchivedSize            { get; }
    public long ExistingUniqueOriginalSize { get; }
    public long ExistingUniqueArchivedSize { get; }
    public long ExistingOriginalSize       { get; }
    public long ExistingArchivedSize       { get; }

    /// <summary>
    /// Gets the requested size based on the provided parameters.
    /// </summary>
    /// <param name="includeDeleted">Specifies whether to include deleted entries.</param>
    /// <param name="unique">Specifies whether to include only unique entries.</param>
    /// <param name="sizeType">Specifies whether to return the original or archived size.</param>
    /// <returns>The appropriate size metric.</returns>
    public long GetSize(bool includeDeleted, bool unique, SizeType sizeType)
    {
        return (includeDeleted, unique, sizeType) switch
        {
            (true, true, SizeType.Original)   => AllUniqueOriginalSize,
            (true, true, SizeType.Archived)   => AllUniqueArchivedSize,
            (true, false, SizeType.Original)  => AllOriginalSize,
            (true, false, SizeType.Archived)  => AllArchivedSize,
            (false, true, SizeType.Original)  => ExistingUniqueOriginalSize,
            (false, true, SizeType.Archived)  => ExistingUniqueArchivedSize,
            (false, false, SizeType.Original) => ExistingOriginalSize,
            (false, false, SizeType.Archived) => ExistingArchivedSize,
            _                                 => throw new InvalidOperationException("Invalid combination of parameters.")
        };
    }

    public void Deconstruct(out long AllUniqueOriginalSize, out long AllUniqueArchivedSize, out long AllOriginalSize, out long AllArchivedSize, out long ExistingUniqueOriginalSize, out long ExistingUniqueArchivedSize, out long ExistingOriginalSize, out long ExistingArchivedSize)
    {
        AllUniqueOriginalSize      = this.AllUniqueOriginalSize;
        AllUniqueArchivedSize      = this.AllUniqueArchivedSize;
        AllOriginalSize            = this.AllOriginalSize;
        AllArchivedSize            = this.AllArchivedSize;
        ExistingUniqueOriginalSize = this.ExistingUniqueOriginalSize;
        ExistingUniqueArchivedSize = this.ExistingUniqueArchivedSize;
        ExistingOriginalSize       = this.ExistingOriginalSize;
        ExistingArchivedSize       = this.ExistingArchivedSize;
    }
}

public enum SizeType
{
    Original,
    Archived
}


