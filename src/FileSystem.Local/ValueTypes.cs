using System;

namespace FileSystem.Local;

public record PathSegment
{
    private readonly string _platformNeutralValue;

    private PathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));

        _platformNeutralValue = value.ToPlatformNeutralPath();
    }

    public static implicit operator PathSegment(string path) => new(path);

    public static PathSegment operator +(PathSegment left, PathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PathSegment($"{left._platformNeutralValue}/{right._platformNeutralValue}");
    }

    public virtual PathSegment ToPlatformNeutral() => _platformNeutralValue;
    public virtual PathSegment ToPlatformSpecific() => _platformNeutralValue.ToPlatformSpecificPath();

    public override string ToString() => _platformNeutralValue;
}

public record RootedPathSegment : PathSegment
{
    private RootedPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static FullNamePathSegment operator +(RootedPathSegment left, NamePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment($"{left.ToPlatformNeutral()}/{right.ToPlatformNeutral()}");
    }

    public static implicit operator RootedPathSegment(string path) => new(path);
}


public record RootPathSegment : RootedPathSegment
{
    private RootPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static FullNamePathSegment operator +(RootPathSegment left, RelativeNamePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment($"{left.ToPlatformNeutral()}/{right.ToPlatformNeutral()}");
    }

    public static implicit operator RootPathSegment(string path) => new(path);
}

public record RelativeNamePathSegment : PathSegment
{
    private RelativeNamePathSegment(string value) : base(value)
    {
        if (SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Relative name cannot be a rooted value.", nameof(value));
    }

    public static implicit operator RelativeNamePathSegment(string path) => new(path);
}


public record RelativePathSegment : PathSegment
{
    private RelativePathSegment(string value) : base(value)
    {
        if (SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Relative name cannot be a rooted value.", nameof(value));
    }

    public static RelativeNamePathSegment operator +(RelativePathSegment left, NamePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return $"{left.ToPlatformNeutral()}/{right.ToPlatformNeutral()}";
    }

    public static implicit operator RelativePathSegment(string path) => new(path);
}

public record NamePathSegment : PathSegment
{
    private NamePathSegment(string name) : base(name)
    {
        if (name.Contains(SIO.Path.PathSeparator))
            throw new ArgumentException("Name cannot contain path separators.", nameof(name));
    }
}

public record FullNamePathSegment : PathSegment
{
    public FullNamePathSegment(string fullName) : base(fullName)
    {
    }

    //public FullNamePathSegment(RootPathSegment rooted, RelativePathSegment relativeName) : this((string)(rooted + relativeName))
    //{
    //}

    public static implicit operator FullNamePathSegment(string path) => new(path);
    //public static implicit operator string(FullNamePathSegment segment) => segment._value;
}

