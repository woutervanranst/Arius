using Arius.Core.Domain;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class HashTests
{
    [Fact]
    public void Equals_SameValue_ShouldBeEqual()
    {
        var h1 = new Hash("ABCD".StringToBytes());
        var h2 = new Hash("ABCD".StringToBytes());

        h1.Equals(h2).Should().BeTrue();
        h1.GetHashCode().Should().Be(h2.GetHashCode());
    }
}