using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace Arius.Core.Shared.StateRepositories;

internal class StateRepository : IStateRepository
{
    private readonly StateRepositoryDbContextPool contextPool;

    public StateRepository(StateRepositoryDbContextPool contextPool)
    {
        this.contextPool = contextPool;
    }

    public FileEntry StateDatabaseFile => contextPool.StateDatabaseFile;
    public bool HasChanges => contextPool.HasChanges;

    public void Vacuum() => contextPool.Vacuum();
    public void Delete() => contextPool.Delete();

    // --- BINARYPROPERTIES

    private static readonly Func<StateRepositoryDbContext, Hash, BinaryProperties?> findBinaryProperty = 
        EF.CompileQuery((StateRepositoryDbContext db, Hash h) =>
            db.Set<BinaryProperties>()
                .AsNoTracking()
                .SingleOrDefault(x => x.Hash == h));

    public BinaryProperties? GetBinaryProperty(Hash h)
    {
        using var context = contextPool.CreateContext();

        return findBinaryProperty(context, h);
    }

    public void SetBinaryPropertyArchiveTier(Hash h, StorageTier tier)
    {
        using var context = contextPool.CreateContext();

        var bp = context.BinaryProperties.SingleOrDefault(x => x.Hash == h);
        if (bp != null && bp.StorageTier != tier)
        {
            bp.StorageTier = tier;
            context.SaveChanges();
        }
    }

    public void AddBinaryProperties(params BinaryProperties[] bps)
    {
        using var context = contextPool.CreateContext();

        context.BinaryProperties.AddRange(bps);
        context.SaveChanges();
    }

    // --- POINTERFILEENTRIES

    public void UpsertPointerFileEntries(params PointerFileEntry[] pfes)
    {
        using var context = contextPool.CreateContext();

        if (HasChanges())
        {
            context.BulkInsertOrUpdate(pfes);
            contextPool.SetHasChanges(); // Set changes explicitly as the AnyChangesInterceptor is not triggered
        }

        bool HasChanges()
        {
            // NOTE: BulkInsertOrUpdate does not provide a way to know if any changes were made - we need to do a bit more complex upfront evaulation.
            // This is still twice as fast as the Find > Compare > InsertOrUpdate without BulkExtensions
            // BulkExtensions does not support BulkConfig.CalculateStats on SQLite

            // Create HashSet for O(1) lookups
            var inputKeys = new HashSet<(Hash, string)>(pfes.Select(p => (p.Hash, p.RelativeName)));

            // Use only hash values for initial filter (usually more selective)
            var hashValues = pfes.Select(p => p.Hash).Distinct().ToList();

            // Fetch entries matching any of our hashes
            var existingEntries = context.Set<PointerFileEntry>()
                .Where(e => hashValues.Contains(e.Hash))
                .Select(e => new
                {
                    e.Hash,
                    e.RelativeName,
                    e.CreationTimeUtc,
                    e.LastWriteTimeUtc
                })
                .AsEnumerable()
                .Where(e => inputKeys.Contains((e.Hash, e.RelativeName))) // O(1) lookup
                .ToDictionary(e => (e.Hash, e.RelativeName));

            // Quick check: different counts means we have inserts
            if (pfes.Length != existingEntries.Count)
                return true;

            // Check for updates
            foreach (var pfe in pfes)
            {
                var key = (pfe.Hash, pfe.RelativeName);
                if (!existingEntries.TryGetValue(key, out var existing) ||
                    existing.CreationTimeUtc != pfe.CreationTimeUtc ||
                    existing.LastWriteTimeUtc != pfe.LastWriteTimeUtc)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static readonly Func<StateRepositoryDbContext, string, IEnumerable<PointerFileEntry>> findPointerFileEntries = 
        EF.CompileQuery((StateRepositoryDbContext db, string relativeNamePrefix) =>
            db.PointerFileEntries
                .AsNoTracking()
                .Where(x => x.RelativeName.StartsWith(relativeNamePrefix)));

    private static readonly Func<StateRepositoryDbContext, string, IEnumerable<PointerFileEntry>> findPointerFileEntriesWithBinaryProperties = 
        EF.CompileQuery((StateRepositoryDbContext db, string relativeNamePrefix) =>
            db.PointerFileEntries
                .AsNoTracking()
                .Where(x => x.RelativeName.StartsWith(relativeNamePrefix))
                .Include(x => x.BinaryProperties));

    public IEnumerable<PointerFileEntry> GetPointerFileEntries(string relativeNamePrefix, bool topDirectoryOnly, bool includeBinaryProperties = false)
    {
        if (!relativeNamePrefix.StartsWith('/'))
            throw new ArgumentException("The relativeNamePrefix must start with a '/' character.", nameof(relativeNamePrefix));

        using var context = contextPool.CreateContext();

        // Convert the prefix to match the database format (remove "/" prefix that the RemovePointerFileExtensionConverter removes)
        var dbRelativeNamePrefix = relativeNamePrefix.RemovePrefix('/');
        
        var query = includeBinaryProperties 
            ? findPointerFileEntriesWithBinaryProperties(context, dbRelativeNamePrefix)
            : findPointerFileEntries(context, dbRelativeNamePrefix);

        foreach (var pfe in query)
        {
            yield return pfe;
        }
    }

    public IEnumerable<PointerFileItem> GetPointerFileItems(string prefix)
    {
        if (!prefix.StartsWith('/'))
            throw new ArgumentException("The prefix must start with a '/' character.", nameof(prefix));

        using var context = contextPool.CreateContext();

        // Convert the prefix to match the database format (remove "/" prefix that the RemovePointerFileExtensionConverter removes)
        var dbPrefix = prefix.RemovePrefix('/');

        // Use raw SQL with proper parameterization for both queries
        using var connection = context.Database.GetDbConnection();
        connection.Open();

        // First, get directories at the next level
        var directoryQuery = @"
            SELECT DISTINCT
                SUBSTR(RelativeName, 1, LENGTH(@prefix) + INSTR(SUBSTR(RelativeName, LENGTH(@prefix) + 1), '/')) AS DirectoryPath
            FROM PointerFileEntries
            WHERE RelativeName LIKE @prefix || '%'
              AND LENGTH(RelativeName) > LENGTH(@prefix)
              AND INSTR(SUBSTR(RelativeName, LENGTH(@prefix) + 1), '/') > 0
            ORDER BY DirectoryPath";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = directoryQuery;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@prefix";
            parameter.Value = dbPrefix;
            command.Parameters.Add(parameter);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var directoryPath = (string)reader["DirectoryPath"];
                yield return new PointerFileDirectory { RelativeName = "/" + directoryPath };
            }
        }

        // Then, get files at the current level
        var fileQuery = @"
            SELECT 
                pfe.Hash, pfe.RelativeName, pfe.CreationTimeUtc, pfe.LastWriteTimeUtc,
                bp.Hash as BpHash, bp.ParentHash, bp.OriginalSize, bp.ArchivedSize, bp.StorageTier
            FROM PointerFileEntries pfe
            LEFT JOIN BinaryProperties bp ON pfe.Hash = bp.Hash
            WHERE pfe.RelativeName LIKE @prefix || '%'
              AND (
                LENGTH(pfe.RelativeName) = LENGTH(@prefix) OR 
                INSTR(SUBSTR(pfe.RelativeName, LENGTH(@prefix) + 1), '/') = 0
              )
            ORDER BY pfe.RelativeName";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = fileQuery;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@prefix";
            parameter.Value = dbPrefix;
            command.Parameters.Add(parameter);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hash = Hash.FromBytes((byte[])reader["Hash"]);
                var relativeName = "/" + (string)reader["RelativeName"];
                
                // Handle DateTime fields that are stored as TEXT in SQLite
                DateTime? creationTimeUtc = null;
                if (reader["CreationTimeUtc"] != DBNull.Value)
                {
                    var creationTimeValue = reader["CreationTimeUtc"];
                    if (creationTimeValue is string creationTimeStr)
                        creationTimeUtc = DateTime.Parse(creationTimeStr);
                    else if (creationTimeValue is DateTime creationTimeDate)
                        creationTimeUtc = creationTimeDate;
                }
                
                DateTime? lastWriteTimeUtc = null;
                if (reader["LastWriteTimeUtc"] != DBNull.Value)
                {
                    var lastWriteTimeValue = reader["LastWriteTimeUtc"];
                    if (lastWriteTimeValue is string lastWriteTimeStr)
                        lastWriteTimeUtc = DateTime.Parse(lastWriteTimeStr);
                    else if (lastWriteTimeValue is DateTime lastWriteTimeDate)
                        lastWriteTimeUtc = lastWriteTimeDate;
                }

                BinaryProperties? binaryProperties = null;
                if (reader["BpHash"] != DBNull.Value)
                {
                    var bpHash = Hash.FromBytes((byte[])reader["BpHash"]);
                    var parentHash = reader["ParentHash"] == DBNull.Value ? null : Hash.FromBytes((byte[])reader["ParentHash"]);
                    var originalSize = (long)reader["OriginalSize"];
                    var archivedSize = (long)reader["ArchivedSize"];
                    var storageTier = (StorageTier)(long)reader["StorageTier"];

                    binaryProperties = new BinaryProperties
                    {
                        Hash = bpHash,
                        ParentHash = parentHash,
                        OriginalSize = originalSize,
                        ArchivedSize = archivedSize,
                        StorageTier = storageTier
                    };
                }

                yield return new PointerFileEntry
                {
                    Hash = hash,
                    RelativeName = relativeName,
                    CreationTimeUtc = creationTimeUtc,
                    LastWriteTimeUtc = lastWriteTimeUtc,
                    BinaryProperties = binaryProperties!
                };
            }
        }
    }

    public PointerFileEntry? GetPointerFileEntry(string relativeName, bool includeBinaryProperties = false)
    {
        if (!relativeName.StartsWith('/'))
            throw new ArgumentException("The relativeName must start with a '/' character.", nameof(relativeName));

        using var context = contextPool.CreateContext();

        // TODO convert to compiled queries

        if (includeBinaryProperties)
            return context.PointerFileEntries.AsNoTracking().Include(x => x.BinaryProperties).SingleOrDefault(pfe => pfe.RelativeName == relativeName);
        else
            return context.PointerFileEntries.AsNoTracking().SingleOrDefault(pfe => pfe.RelativeName == relativeName);
    }

    //public IEnumerable<PointerFileEntryDto> GetPointerFileEntries()
    //{
    //    using var context = GetContext();
    //    foreach (var pfe in context.PointerFileEntries)
    //        yield return pfe;
    //}

    public void DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted)
    {
        using var context = contextPool.CreateContext();

        var entriesToDelete = context.PointerFileEntries.Where(shouldBeDeleted).ToArray();
        context.BulkDelete(entriesToDelete);

        if (entriesToDelete.Any())
            contextPool.SetHasChanges();
    }
}
