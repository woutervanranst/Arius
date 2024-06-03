using System.IO.MemoryMappedFiles;

namespace Arius.Core.Application.Chunkers
{
    public class FileSplitter
    {
        private readonly string _inputFilePath;
        private readonly byte[] _pattern;
        private readonly long _minPartSize;

        public FileSplitter(string inputFilePath, byte[] pattern, long minPartSize = 0)
        {
            _inputFilePath = inputFilePath;
            _pattern = pattern;
            _minPartSize = minPartSize;
        }

        public IEnumerable<byte[]> Split()
        {
            using (var mmf = MemoryMappedFile.CreateFromFile(_inputFilePath, FileMode.Open))
            {
                using (var accessor = mmf.CreateViewAccessor())
                {
                    long length = accessor.Capacity;
                    long currentStart = 0;

                    for (long i = 0; i < length - _pattern.Length + 1; i++)
                    {
                        if (MatchPattern(accessor, i, _pattern))
                        {
                            long partSize = i - currentStart;
                            if (partSize >= _minPartSize)
                            {
                                yield return ReadPart(accessor, currentStart, partSize);
                                currentStart = i + _pattern.Length;
                            }
                            i += _pattern.Length - 1;
                        }
                    }

                    // Yield the last part if any data remains
                    if (currentStart < length)
                    {
                        yield return ReadPart(accessor, currentStart, length - currentStart);
                    }
                }
            }
        }

        private bool MatchPattern(MemoryMappedViewAccessor accessor, long position, byte[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (accessor.ReadByte(position + i) != pattern[i])
                {
                    return false;
                }
            }
            return true;
        }

        private byte[] ReadPart(MemoryMappedViewAccessor accessor, long start, long size)
        {
            byte[] buffer = new byte[size];
            accessor.ReadArray(start, buffer, 0, buffer.Length);
            return buffer;
        }
    }
}
