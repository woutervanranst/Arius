using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public record RepositoryVersion
{
    public RepositoryVersion(string name)
    {
        Name = name;
    }
    public string   Name             { get; }

    public string FileSystemName => $"{Name.Replace(":", "-")}";
    public string FileSystemNameWithExtension => $"{Name.Replace(":", "-")}{IStateDatabaseFile.Extension}";

    public static RepositoryVersion FromName(string name)
    {
        if (DateTime.TryParseExact(name, "yyyy-MM-ddTHH-mm-ss", null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDateTime))
        {
            var v = new DateTimeRepositoryVersion(parsedDateTime);
            if (v.FileSystemName != name)
                throw new ArgumentException("DateTimeRepositoryVersion.Name != name");

            return v;
        }
        else
        {
            return new RepositoryVersion(name);
        }
    }
}

public record DateTimeRepositoryVersion : RepositoryVersion
{
    public DateTimeRepositoryVersion(DateTime name) : base($"{name:s}")
    {
        OriginalDateTime = name;
    }

    public static DateTimeRepositoryVersion FromDateTime(DateTime name) => new(name.ToUniversalTime());
    public static DateTimeRepositoryVersion FromUtcNow() => new(DateTime.UtcNow);

    public DateTime OriginalDateTime { get; }
}