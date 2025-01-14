using System;

namespace FileSystem.Local;

public record PathSegment
{
    public PathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public static implicit operator PathSegment(string path)
    {
        return new PathSegment(path);
    }

    public static implicit operator string(PathSegment segment)
    {
        return segment.Value;
    }

    public static PathSegment operator +(PathSegment left, PathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PathSegment(SIO.Path.Combine(left.Value, right.Value));
    }

    public override string ToString()
    {
        return Value;
    }
}

public record RootPathSegment : PathSegment
{
    public RootPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static implicit operator RootPathSegment(string path)
    {
        return new RootPathSegment(path);
    }

    public static implicit operator string(RootPathSegment segment)
    {
        return segment.Value;
    }

    public static FullNamePathSegment operator +(RootPathSegment left, RelativePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment(left, right);
    }
}

public record RelativePathSegment : PathSegment
{
    public RelativePathSegment(string value) : base(value)
    {
        if (SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Relative name cannot be a rooted value.", nameof(value));
    }
}

public record FullNamePathSegment : PathSegment
{
    public FullNamePathSegment(string fullName) : base(fullName)
    {
    }

    public FullNamePathSegment(RootPathSegment root, RelativePathSegment relativeName) : base((string)(root + relativeName))
    {
    }

    public static implicit operator FullNamePathSegment(string path)
    {
        return new FullNamePathSegment(path);
    }

    public static implicit operator string(FullNamePathSegment segment)
    {
        return segment.Value;
    }
}

public record NamePathSegment : PathSegment
{
    public NamePathSegment(string name) : base(name)
    {
        if (name.Contains(SIO.Path.PathSeparator))
            throw new ArgumentException("Name cannot contain path separators.", nameof(name));
    }
}