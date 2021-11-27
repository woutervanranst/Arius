using System;

namespace Arius.Cli.Commands;

internal class StateVersion
{
    public StateVersion(DateTime versionUtc)
    {
        VersionUtc = versionUtc;
    }
    public DateTime VersionUtc { get; }
}