using System;

namespace FileSystem.Local;

public abstract record PathSegment
{
    internal readonly string _value;

    protected PathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));

        _value = value;
    }

    public abstract PathSegment ToPlatformNeutral();

    public abstract PathSegment ToPlatformSpecific();

    public override string ToString() => _value;
}

public record PlatformNeutralPathSegment : PathSegment
{
    public PlatformNeutralPathSegment(string value) : base(value.ToPlatformNeutralPath())
    {
    }

    public static implicit operator PlatformNeutralPathSegment(string path) => new(path);
    public static implicit operator string(PlatformNeutralPathSegment segment) => segment._value;

    public static PlatformNeutralPathSegment operator +(PlatformNeutralPathSegment left, PlatformNeutralPathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PlatformNeutralPathSegment(SIO.Path.Combine(left._value, right._value));
    }

    public override PlatformNeutralPathSegment ToPlatformNeutral() => this;
    public override PlatformSpecificPathSegment ToPlatformSpecific() => _value;
    
    public override string ToString() => _value;
}

public record PlatformSpecificPathSegment : PathSegment
{
    public PlatformSpecificPathSegment(string value) : base(value.ToPlatformSpecificPath())
    {
    }

    public static implicit operator PlatformSpecificPathSegment(string path) => new(path);
    public static implicit operator string(PlatformSpecificPathSegment segment) => segment._value;

    public static PlatformSpecificPathSegment operator +(PlatformSpecificPathSegment left, PlatformSpecificPathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PlatformSpecificPathSegment(SIO.Path.Combine(left._value, right._value));
    }

    public override PlatformNeutralPathSegment ToPlatformNeutral() => _value;
    public override PlatformSpecificPathSegment ToPlatformSpecific() => this;

    public override string ToString() => _value;
}

public record DirectoryPathSegment : PlatformNeutralPathSegment
{
    public DirectoryPathSegment(string value) : base(value)
    {
        if (!SIO.Path.HasExtension(value))
            throw new ArgumentException("Directory name cannot have an extension.", nameof(value));
    }

    public static implicit operator DirectoryPathSegment(string path) => new(path);
    public static implicit operator string(DirectoryPathSegment segment) => segment._value;

    public static FullNamePathSegment operator +(DirectoryPathSegment left, NamePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment(SIO.Path.Combine(left._value, right._value));
    }

    public static FullNamePathSegment operator +(DirectoryPathSegment left, RelativePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment(SIO.Path.Combine(left._value, right._value));
    }
}

public record RootPathSegment : DirectoryPathSegment
{
    public RootPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static implicit operator RootPathSegment(string path) => new(path);
    public static implicit operator string(RootPathSegment segment) => segment._value;
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