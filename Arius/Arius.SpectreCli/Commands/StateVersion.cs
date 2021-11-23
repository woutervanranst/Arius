using System;

namespace Arius.CliSpectre.Commands;

internal class StateVersion
{
    public StateVersion(DateTime versionUtc)
    {
        VersionUtc = versionUtc;
    }
    public DateTime VersionUtc { get; }
}