using Arius.Core.Application.Chunkers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Arius.Core.Application.Benchmark
{
    [MemoryDiagnoser]
    public class BenchmarkFileSplitter
    {
        private const string InputFilePath = @"C:\Users\WouterVanRanst\Downloads\FileZilla_3.67.0_win64-setup.exe";
        private const string OutputDirectory1 = @"C:\Users\WouterVanRanst\Downloads\OutputBenchmark";
        private const string OutputDirectory2 = @"C:\Users\WouterVanRanst\Downloads\OutputBenchmark2";

        //[GlobalSetup]
        //public void Setup()
        //{
        //}

        [Benchmark]
        public void FileSplitterBenchmark()
        {
            Directory.CreateDirectory(OutputDirectory1);

            var splitter = new FileSplitter(InputFilePath, new byte[] { 0, 0 }, 1024 * 4);
            int partNumber = 0;

            foreach (var part in splitter.Split())
            {
                string outputFilePath = Path.Combine(OutputDirectory1, $"part_{partNumber:D2}.bin");
                File.WriteAllBytes(outputFilePath, part);
                partNumber++;
            }

            Directory.Delete(OutputDirectory1, true);
        }

        [Benchmark]
        public void ByteBoundaryChunkerBenchmark()
        {
            Directory.CreateDirectory(OutputDirectory2);

            var splitter = new ByteBoundaryChunker();
            int partNumber = 0;

            using var stream = File.OpenRead(InputFilePath);

            foreach (var part in splitter.Chunk(stream))
            {
                string outputFilePath = Path.Combine(OutputDirectory2, $"part_{partNumber:D2}.bin");
                File.WriteAllBytes(outputFilePath, part.Bytes);
                partNumber++;
            }

            Directory.Delete(OutputDirectory2, true);
        }
    }

    public static class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<BenchmarkFileSplitter>();
        }
    }
}
