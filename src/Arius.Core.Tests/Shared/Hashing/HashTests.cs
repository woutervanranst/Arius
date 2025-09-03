using Arius.Core.Shared.Hashing;
using Shouldly;

namespace Arius.Core.Tests.Shared.Hashing;

public class HashTests
{
    [Fact]
    public void Equals_SameValue_ShouldBeEqual()
    {
        Hash h1 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";
        Hash h2 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";

        h1.Equals(h2).ShouldBeTrue();
        h1.GetHashCode().ShouldBe(h2.GetHashCode());

        (h1 == h2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_LowerValue_ShouldBeEqual()
    {
        Hash h1 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD";
        Hash h2 = "ABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCDABCD".ToLowerInvariant();

        h1.Equals(h2).ShouldBeTrue();
        h1.GetHashCode().ShouldBe(h2.GetHashCode());
    }
}