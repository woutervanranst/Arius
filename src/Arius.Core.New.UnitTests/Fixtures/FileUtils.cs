using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.New.UnitTests.Fixtures;

public class FileUtils
{
    public static void CreateRandomFile(string fullName, long sizeInBytes)
    {
        // https://stackoverflow.com/q/4432178/1582323

        var f = new FileInfo(fullName);
        f.Directory.CreateIfNotExists();

        byte[] data = new byte[sizeInBytes];
        var    rng  = new Random();
        rng.NextBytes(data);
        File.WriteAllBytes(fullName, data);
    }

    public static void CreateZeroFile(string fullName, long sizeInBytes)
    {
        using var fileStream = new FileStream(fullName, FileMode.Create, FileAccess.Write, FileShare.None);
        fileStream.SetLength(sizeInBytes);
        fileStream.Position = 0;

        var buffer = new byte[8192]; // 8 KB buffer

        long remainingBytes = sizeInBytes;
        while (remainingBytes > 0)
        {
            var bytesToWrite = remainingBytes > buffer.Length ? buffer.Length : (int)remainingBytes;
            fileStream.Write(buffer, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }
    }
}