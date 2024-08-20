using Arius.Core.Facade;

namespace Arius.Core.Queries.PointerFilesEntries;

public interface IEntryQueryResult // also implemented by Arius.UI.FileService
{
    public string RelativeName { get; }
}

public interface IPointerFileEntryQueryResult : IEntryQueryResult // properties specific to PointerFileEntry. Public interface is required for type matching
{
    public long           OriginalLength { get; }
    public HydrationState HydrationState { get; }
}