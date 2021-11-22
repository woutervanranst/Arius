using System.Threading;

namespace Arius.Core.Commands.Archive;
public class ArchiveCommandStatistics
{
    public void AddLocalRepositoryStatistic(int beforeFiles = 0, long beforeSize = 0, int beforePointerFiles = 0,
        int deltaFiles = 0, long deltaSize = 0, int deltaPointerFiles = 0)
    {
        Interlocked.Add(ref this.localBeforeFiles, beforeFiles);
        Interlocked.Add(ref this.localBeforeSize, beforeSize);
        Interlocked.Add(ref this.localBeforePointerFiles, beforePointerFiles);

        Interlocked.Add(ref this.localDeltaFiles, deltaFiles);
        Interlocked.Add(ref this.localDeltaSize, deltaSize);
        Interlocked.Add(ref this.localDeltaPointerFiles, deltaPointerFiles);
    }

    public int localBeforeFiles;
    public long localBeforeSize;
    public int localBeforePointerFiles;

    public int localDeltaFiles;
    public long localDeltaSize;
    public int localDeltaPointerFiles;


    public void AddRemoteRepositoryStatistic(int beforeBinaries = 0, long beforeSize = 0, int beforePointerFileEntries = 0,
        int deltaBinaries = 0, long deltaSize = 0, int deltaPointerFileEntries = 0)
    {
        Interlocked.Add(ref this.remoteBeforeBinaries, beforeBinaries);
        Interlocked.Add(ref this.remoteBeforeSize, beforeSize);
        Interlocked.Add(ref this.remoteBeforePointerFileEntries, beforePointerFileEntries);

        Interlocked.Add(ref this.remoteDeltaBinaries, deltaBinaries);
        Interlocked.Add(ref this.remoteDeltaSize, deltaSize);
        Interlocked.Add(ref this.remoteDeltaPointerFileEntries, deltaPointerFileEntries);
    }

    public int remoteBeforeBinaries;
    public long remoteBeforeSize;
    public int remoteBeforePointerFileEntries;

    public int remoteDeltaBinaries;
    public long remoteDeltaSize;
    public int remoteDeltaPointerFileEntries;
}