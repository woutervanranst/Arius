namespace Arius.Core.Domain.Storage;

public record RepositoryVersion
{
    public required string Name { get; init; }
    
    public static implicit operator RepositoryVersion(DateTime name)
    {
        return new RepositoryVersion { Name = $"{DateTime.UtcNow:s}" };
    }
}