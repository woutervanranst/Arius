using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        var splitter   = new FileSplitter(inputFilePath, new byte[] { 0, 0 });
        int partNumber = 0;

        foreach (var part in splitter.Split())
        {
            string outputFilePath = Path.Combine(outputDirectory, $"part_{partNumber:D2}.bin");
            File.WriteAllBytes(outputFilePath, part);
            partNumber++;
        }
    }
}