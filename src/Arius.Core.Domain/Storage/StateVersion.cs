using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Domain.Storage;

public record StateVersion
{
    public StateVersion(string name)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }
    public string   Name             { get; }

    public string FileSystemName => $"{Name.Replace(":", "-")}";
    public string FileSystemNameWithExtension => $"{Name.Replace(":", "-")}{IStateDatabaseFile.Extension}";

    public static StateVersion FromName(string name)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(name);

        if (DateTime.TryParseExact(name, "yyyy-MM-ddTHH-mm-ss", null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDateTime))
        {
            var v = new DateTimeStateVersion(parsedDateTime);
            if (v.FileSystemName != name)
                throw new ArgumentException("DateTimeStateVersion.Name != name");

            return v;
        }
        else
        {
            return new StateVersion(name);
        }
    }

    public static StateVersion FromUtcNow()
    {
        return new DateTimeStateVersion(DateTime.UtcNow);
    }
}

public record DateTimeStateVersion : StateVersion
{
    public DateTimeStateVersion(DateTime name) : base($"{name:s}")
    {
        OriginalDateTime = name;
    }

    public static DateTimeStateVersion FromDateTime(DateTime name) => new(name.ToUniversalTime());
    public static DateTimeStateVersion FromUtcNow() => new(DateTime.UtcNow);

    public DateTime OriginalDateTime { get; }
}