using System.Threading;
using Arius.Core.Commands.Archive;

namespace Arius.CliSpectre.Utils;

internal class ArchiveCommandStatistics : IArchiveCommandStatistics
{
    public void AddIndexedFile(int pointerFileCount = 0, int binaryFileCount = 0)
    {
        Interlocked.Add(ref indexedPointerFileCount, pointerFileCount);
        Interlocked.Add(ref indexedBinaryFileCount, binaryFileCount);
    }

    public int IndexedPointerFileCount => indexedPointerFileCount;
    private int indexedPointerFileCount;

    public int IndexedBinaryFileCount => indexedBinaryFileCount;
    private int indexedBinaryFileCount;
}