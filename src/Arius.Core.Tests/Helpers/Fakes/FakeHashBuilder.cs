using System.Security.Cryptography;
using Arius.Core.Shared.Hashing;

namespace Arius.Core.Tests.Helpers.Fakes;

internal static class FakeHashBuilder
{
    public static Hash GenerateValidHash(int seed)
    {
        using var sha256    = SHA256.Create();
        byte[]    seedBytes = BitConverter.GetBytes(seed);
        return sha256.ComputeHash(seedBytes);
    }
}