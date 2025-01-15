using System;

namespace FileSystem.Local;

public enum PathSegmentComparison
{
    PlatformInvariant,
    LiteralValue
}

public record PathSegment
{
    private readonly string _value;

    private PathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));

        _value = value;
    }

    public static implicit operator PathSegment(string path) => new(path);

    public static PathSegment operator +(PathSegment left, PathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PathSegment($"{left._value}/{right._value}");
    }

    public virtual PathSegment ToPlatformNeutral() => _value.ToPlatformNeutralPath();
    public virtual PathSegment ToPlatformSpecific() => _value.ToPlatformSpecificPath();

    public virtual bool Equals(PathSegment? obj)
    {
        return Equals(obj, PathSegmentComparison.LiteralValue);
    }
    public bool Equals(PathSegment? obj, PathSegmentComparison comparisonType = PathSegmentComparison.PlatformInvariant)
    {
        if (obj is PathSegment other)
        {
            return comparisonType switch
            {
                PathSegmentComparison.LiteralValue => _value == other._value,
                PathSegmentComparison.PlatformInvariant => ToPlatformNeutral()._value == other.ToPlatformNeutral()._value,
                _ => throw new ArgumentOutOfRangeException(nameof(comparisonType))
            };
        }

        return false;
    }

    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value;
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


//public record RootPathSegment : RootedPathSegment
//{
//    private RootPathSegment(string value) : base(value)
//    {
//        if (!SIO.Path.IsPathRooted(value))
//            throw new ArgumentException("Root must be a rooted path.", nameof(value));
//    }

//    public static FullNamePathSegment operator +(RootPathSegment left, RelativeNamePathSegment right)
//    {
//        ArgumentNullException.ThrowIfNull(left);
//        ArgumentNullException.ThrowIfNull(right);

//        return new FullNamePathSegment($"{left.ToPlatformNeutral()}/{right.ToPlatformNeutral()}");
//    }

//    public static implicit operator RootPathSegment(string path) => new(path);
//}

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

    public static implicit operator NamePathSegment(string path) => new(path);
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

