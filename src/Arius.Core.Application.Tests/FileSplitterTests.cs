using Arius.Core.Application.Chunkers;

namespace Arius.Core.Application.Tests;

public class FileSplitterTests
{
    [Fact]
    public void TestFileSplitter()
    {
        string inputFilePath   = @"C:\Users\WouterVanRanst\Downloads\FileZilla_3.67.0_win64-setup.exe";
        string outputDirectory = @"C:\Users\WouterVanRanst\Downloads\Output";

        Directory.CreateDirectory(outputDirectory);

        var splitter   = new FileSplitter(inputFilePath, new byte[] { 0, 0 }, 1024 * 4);
        int partNumber = 0;

        foreach (var part in splitter.Split())
        {
            string outputFilePath = Path.Combine(outputDirectory, $"part_{partNumber:D2}.bin");
            File.WriteAllBytes(outputFilePath, part);
            partNumber++;
        }

        Directory.Delete(outputDirectory, true);
    }

    [Fact]
    public void TestByteBoundaryCHunker()
    {
        string inputFilePath   = @"C:\Users\WouterVanRanst\Downloads\FileZilla_3.67.0_win64-setup.exe";
        string outputDirectory = @"C:\Users\WouterVanRanst\Downloads\Output2";

        Directory.CreateDirectory(outputDirectory);

        var splitter   = new ByteBoundaryChunker();
        int partNumber = 0;

        using var stream = File.OpenRead(inputFilePath);

        foreach (var part in splitter.Chunk(stream))
        {
            string outputFilePath = Path.Combine(outputDirectory, $"part_{partNumber:D2}.bin");
            File.WriteAllBytes(outputFilePath, part.Bytes);
            partNumber++;
        }


        Directory.Delete(outputDirectory, true);
    }


}