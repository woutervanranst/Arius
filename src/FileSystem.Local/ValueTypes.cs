using System;

namespace FileSystem.Local;

public record PlatformNeutralPathSegment
{
    protected readonly string _value;

    public PlatformNeutralPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));
        _value = value.ToPlatformNeutralPath();
    }

    public static implicit operator PlatformNeutralPathSegment(string path) => new(path);
    public static implicit operator string(PlatformNeutralPathSegment segment) => segment._value;

    public static PlatformNeutralPathSegment operator +(PlatformNeutralPathSegment left, PlatformNeutralPathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PlatformNeutralPathSegment(SIO.Path.Combine(left, right));
    }

    public override string ToString() => _value;
}

public record RootPathSegment : PlatformNeutralPathSegment
{
    public RootPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static implicit operator RootPathSegment(string path) => new(path);
    public static implicit operator string(RootPathSegment segment) => segment._value;

    public static FullNamePathSegment operator +(RootPathSegment left, RelativePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment(SIO.Path.Combine(left, right));
    }
}

public record RelativePathSegment : PlatformNeutralPathSegment
{
    public RelativePathSegment(string value) : base(value)
    {
        if (SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Relative name cannot be a rooted value.", nameof(value));
    }
}

public record FullNamePathSegment : PlatformNeutralPathSegment
{
    public FullNamePathSegment(string fullName) : base(fullName)
    {
    }

    public FullNamePathSegment(RootPathSegment root, RelativePathSegment relativeName) : this((string)(root + relativeName))
    {
    }

    public static implicit operator FullNamePathSegment(string path) => new(path);
    public static implicit operator string(FullNamePathSegment segment) => segment._value;
}

public record NamePathSegment : PlatformNeutralPathSegment
{
    public NamePathSegment(string name) : base(name)
    {
        if (name.Contains(SIO.Path.PathSeparator))
            throw new ArgumentException("Name cannot contain path separators.", nameof(name));
    }
}