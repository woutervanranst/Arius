using System.Threading;

namespace Arius.Core.Commands.Archive;
public class ArchiveCommandStatistics
{
    public void AddLocalRepositoryStatistic(int pointerFileCount = 0, int binaryFileCount = 0, long binaryFileSize = 0)
    {
        Interlocked.Add(ref this.pointerFileCount, pointerFileCount);
        Interlocked.Add(ref this.binaryFileCount, binaryFileCount);
        Interlocked.Add(ref this.binaryFileSize, binaryFileSize);
    }

    public int PointerFileCount => pointerFileCount;
    private int pointerFileCount;

    public int BinaryFileCount => binaryFileCount;
    private int binaryFileCount;

    public long BinaryFileSize => binaryFileSize;
    private long binaryFileSize;


    public void AddTransactionStatistic(int binaryFileUploaded = 0, long binaryFileSizeUploaded = 0)
    {
        Interlocked.Add(ref this.binaryFileUploaded, binaryFileUploaded);
        Interlocked.Add(ref this.binaryFileSizeUploaded, binaryFileSizeUploaded);
    }

    public int BinaryFileUploaded => binaryFileUploaded;
    private int binaryFileUploaded;

    public long BinaryFileSizeUploaded => binaryFileSizeUploaded;
    private long binaryFileSizeUploaded;
}