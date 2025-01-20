using Arius.Core.Models;
using FluentAssertions;

namespace Arius.Core.Tests;

public class HashTests
{
    [Fact]
    public void Equals_SameValue_ShouldBeEqual()
    {
        HashValue h1 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";
        HashValue h2 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";

        h1.Equals(h2).Should().BeTrue();
        h1.GetHashCode().Should().Be(h2.GetHashCode());

        (h1 == h2).Should().BeTrue();
    }

    [Fact]
    public void Equals_LowerValue_ShouldBeEqual()
    {
        HashValue h1 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";
        HashValue h2 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD".ToLowerInvariant();

        h1.Equals(h2).Should().BeTrue();
        h1.GetHashCode().Should().Be(h2.GetHashCode());
    }
}