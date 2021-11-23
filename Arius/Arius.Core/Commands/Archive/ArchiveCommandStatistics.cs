using System.Threading;

namespace Arius.Core.Commands.Archive;

public class ArchiveCommandStatistics
{
    public void AddLocalRepositoryStatistic(int beforeFiles = 0, long beforeSize = 0, int beforePointerFiles = 0,
        int deltaFiles = 0, long deltaSize = 0, int deltaPointerFiles = 0
        /*int afterFiles = 0, long afterSize = 0, int afterPointerFiles = 0*/)
    {
        if (beforeFiles != 0) 
            Interlocked.Add(ref this.localBeforeFiles, beforeFiles);
        if (beforeSize != 0)
            Interlocked.Add(ref this.localBeforeSize, beforeSize);
        if (beforePointerFiles != 0)
            Interlocked.Add(ref this.localBeforePointerFiles, beforePointerFiles);

        if (deltaFiles != 0)
            Interlocked.Add(ref this.localDeltaFiles, deltaFiles);
        if (deltaSize != 0)
            Interlocked.Add(ref this.localDeltaSize, deltaSize);
        if (deltaPointerFiles != 0)
            Interlocked.Add(ref this.localDeltaPointerFiles, deltaPointerFiles);

        //if (afterFiles != 0)
        //    Interlocked.Add(ref this.localAfterFiles, afterFiles);
        //if (afterSize != 0)
        //    Interlocked.Add(ref this.localAfterSize, afterSize);
    }

    public int localBeforeFiles;
    public long localBeforeSize;
    public int localBeforePointerFiles;

    public int localDeltaFiles;
    public long localDeltaSize;
    public int localDeltaPointerFiles;

    //public int localAfterFiles;
    //public long localAfterSize;

    public void AddRemoteRepositoryStatistic(int beforeBinaries = 0, long beforeSize = 0, int beforePointerFileEntries = 0,
        int deltaBinaries = 0, long deltaSize = 0, int deltaPointerFileEntries = 0,
        int afterBinaries = 0, long afterSize = 0, int afterPointerFileEntries = 0)
    {
        if (beforeBinaries != 0)
            Interlocked.Add(ref this.remoteBeforeBinaries, beforeBinaries);
        if (beforeSize != 0)
            Interlocked.Add(ref this.remoteBeforeSize, beforeSize);
        if (beforePointerFileEntries != 0) 
            Interlocked.Add(ref this.remoteBeforePointerFileEntries, beforePointerFileEntries);

        if (deltaBinaries != 0) 
            Interlocked.Add(ref this.remoteDeltaBinaries, deltaBinaries);
        if (deltaSize != 0) 
            Interlocked.Add(ref this.remoteDeltaSize, deltaSize);
        if (deltaPointerFileEntries != 0) 
            Interlocked.Add(ref this.remoteDeltaPointerFileEntries, deltaPointerFileEntries);

        if (afterBinaries != 0)
            Interlocked.Add(ref this.remoteAfterBinaries, afterBinaries);
        if (afterSize != 0)
            Interlocked.Add(ref this.remoteAfterSize, afterSize);
        if (afterPointerFileEntries != 0)
            Interlocked.Add(ref this.remoteAfterPointerFileEntries, afterPointerFileEntries);
    }

    public int remoteBeforeBinaries;
    public long remoteBeforeSize;
    public int remoteBeforePointerFileEntries;

    public int remoteDeltaBinaries;
    public long remoteDeltaSize;
    public int remoteDeltaPointerFileEntries;

    public int remoteAfterBinaries;
    public long remoteAfterSize;
    public int remoteAfterPointerFileEntries;
}