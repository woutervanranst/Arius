using Arius.Core.Extensions;
using Arius.Core.Facade;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public enum CreatePointerFileEntryResult
    {
        Inserted,
        InsertedDeleted,
        NoChange
    }

    /// <summary>
    /// Create a PointerFileEntry for the given PointerFile and the given version
    /// </summary>
    public async Task<CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFile pf, DateTime versionUtc)
    {
        var pfe = new PointerFileEntry
        {
            BinaryHashValue  = pf.BinaryHash.Value,
            RelativeName     = pf.RelativeName,
            VersionUtc       = versionUtc,
            IsDeleted        = false,
            CreationTimeUtc  = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(),
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName).ToUniversalTime(),
        };

        return await CreatePointerFileEntryIfNotExistsAsync(pfe);
    }

    /// <summary>
    /// Create a PointerFileEntry that is deleted form the given PointerFileEntry
    /// </summary>
    public async Task<CreatePointerFileEntryResult> CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime versionUtc)
    {
        pfe = pfe with
        {
            VersionUtc = versionUtc,
            IsDeleted = true,
            CreationTimeUtc = null,
            LastWriteTimeUtc = null
        };

        return await CreatePointerFileEntryIfNotExistsAsync(pfe);
    }

    /// <summary>
    /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
    /// </summary>
    private async Task<CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
    {
        await using var db = GetStateDbContext();

        var lastVersion = await db.PointerFileEntries
            .Where(pfe0 => pfe.RelativeName == pfe0.RelativeName)
            .OrderBy(pfe0 => pfe0.VersionUtc)
            .LastOrDefaultAsync();

        // TODO here be race condition, see "C:\Users\woute\Documents\GitHub\Arius\src\Arius.Cli\bin\Debug\net7.0\logs\arius-archive-test-2023-08-22T14-02-59.6513082Z.tar.gzip"

        var toAdd = !pfeEqualityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one
        if (toAdd)
        {
            await db.PointerFileEntries.AddAsync(pfe);
            await db.SaveChangesAsync();

            if (pfe.IsDeleted)
            {
                logger.LogInformation($"Deleted Entry '{pfe.RelativeName}'");
                return CreatePointerFileEntryResult.InsertedDeleted;
            }
            else
            {
                logger.LogInformation($"Added Entry '{pfe.RelativeName}'");
                return CreatePointerFileEntryResult.Inserted;
            }
        }
        else
            return CreatePointerFileEntryResult.NoChange;
    }

    public IAsyncEnumerable<PointerFileEntry> GetCurrentPointerFileEntriesAsync(bool includeDeleted)
    {
        return GetPointerFileEntriesAsync(DateTime.UtcNow, includeDeleted);
    }


    /// <summary>
    /// Get the PointerFileEntries at the given version.
    /// If no version is specified, the current (most recent) will be returned
    /// </summary>
    public IAsyncEnumerable<PointerFileEntry> GetPointerFileEntriesAsync(DateTime pointInTimeUtc, bool includeDeleted,
        string? relativeNameEquals = null,
        bool includeChunkEntry = false)
    {
        return GetPointerFileEntriesAtPointInTimeAsync(
                pointInTimeUtc: pointInTimeUtc, 
                relativeNameEquals: relativeNameEquals,
                includeChunkEntry: includeChunkEntry)
            .Where(pfe => includeDeleted || !pfe.IsDeleted);
    }


    private async IAsyncEnumerable<PointerFileEntry> GetPointerFileEntriesAtPointInTimeAsync(DateTime pointInTimeUtc,
        string? relativeNameEquals = null,
        bool includeChunkEntry = false)
    {
        var versionUtc = await GetVersionAsync(pointInTimeUtc);

        if (versionUtc is null)
            yield break;

        await foreach (var entry in GetPointerFileEntriesAtVersionAsync(
                           versionUtc: versionUtc.Value, 
                           relativeNameEquals: relativeNameEquals, 
                           includeChunkEntry: includeChunkEntry))
            yield return entry;
    }

    private async IAsyncEnumerable<PointerFileEntry> GetPointerFileEntriesAtVersionAsync(DateTime versionUtc,
        string? relativeNameEquals = null,
        bool includeChunkEntry = false)
    {
        await using var db = GetStateDbContext();

        // Get all the entries up to the given version
        var entries = includeChunkEntry ?
            (IQueryable<PointerFileEntry>)db.PointerFileEntries.Include(pfe => pfe.Chunk) :
            (IQueryable<PointerFileEntry>)db.PointerFileEntries;

        entries = entries.Where(pfe => pfe.VersionUtc <= versionUtc);

        // Apply the filters from the filter object
        if (relativeNameEquals is not null)
            entries = entries.Where(pfe => pfe.RelativeName == relativeNameEquals);

        // Perform the grouping and ordering within the same query to limit the amount of data pulled into memory
        var groupedAndOrdered = entries
            .GroupBy(pfe => pfe.RelativeName)
            .Select(g => g.OrderByDescending(p => p.VersionUtc).FirstOrDefault());

        await foreach (var entry in groupedAndOrdered.AsAsyncEnumerable())
            if (entry != null)
                yield return entry;

        //private async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntriesAtVersionAsync(DateTime versionUtc)
        //{
        //    //TODO an exception here is swallowed

        //    await using var db = GetStateDbContext();

        //    var r = await db.PointerFileEntries.AsParallel()
        //        .GroupBy(pfe => pfe.RelativeName)
        //        .Select(g => g.Where(pfe => pfe.VersionUtc <= versionUtc))
        //        .ToAsyncEnumerable() //TODO ParallelEnumerable? //remove this and the dependency on Linq.Async?
        //        .Where(c => c.Any())
        //        .Select(z => z.OrderBy(pfe => pfe.VersionUtc).Last())
        //        .Select(pfe => pfe.ToPointerFileEntry())
        //        .ToArrayAsync();

        //    return r;
        //}
    }

    public async IAsyncEnumerable<string> GetPointerFileEntriesSubdirectoriesAsync(string prefix)
    {
        if (!prefix.EndsWith('/'))
            throw new ArgumentException($"{nameof(prefix)} argument must end with '/'");

        await using var db = GetStateDbContext();

        var connectionString = db.Database.GetConnectionString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var pointerFileEntriesTableName = db.Model.FindEntityType(typeof(PointerFileEntry)).GetTableName();
        var relativeNameColumnName = db.Model.FindEntityType(typeof(PointerFileEntry))
            .FindProperty(nameof(PointerFileEntry.RelativeName))
            .GetColumnName();

        var sql = $@"
            WITH PrefixLocations AS (
                SELECT
                    instr({relativeNameColumnName}, @Prefix) + length(@Prefix) - 1 AS PrefixEnd,
                    {relativeNameColumnName}
                FROM
                    {pointerFileEntriesTableName}
                WHERE
                    {relativeNameColumnName} LIKE @Prefix || '%'
            )

            SELECT DISTINCT
                substr({relativeNameColumnName}, PrefixEnd + 1, instr(substr({relativeNameColumnName}, PrefixEnd + 1), '/') - 1) AS Result
            FROM
                PrefixLocations
            WHERE
                instr(substr({relativeNameColumnName}, PrefixEnd + 1), '/') > 0;";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Prefix", prefix);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            yield return reader.GetString(0);
    }

    /// <summary>
    /// Get the count of all PointerFileEntries (deleted and older versions)
    /// </summary>
    /// <returns></returns>
    internal async Task<int> CountPointerFileEntriesAsync()
    {
        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.CountAsync();
    }

    /// <summary>
    /// Get the count of the current existing PointerFileEntries
    /// </summary>
    /// <returns></returns>
    internal async Task<int> CountCurrentPointerFileEntriesAsync()
    {
        await using var db = GetStateDbContext();
        return await GetCurrentPointerFileEntriesAsync(false).CountAsync();
    }



    /// <summary>
    /// Get the version that corresponds to the state of the archive at pointInTime.
    /// If no version is found, returns null
    /// </summary>
    private async Task<DateTime?> GetVersionAsync(DateTime pointInTimeUtc)
    {
        var versions = GetVersionsAsync().Reverse().ToEnumerable(); // TODO huh where does the await go?

        // if the pointInTime is a version - return the pointInTime (optimization in case of the GUI dropdown)
        if (versions.Contains(pointInTimeUtc))
            return pointInTimeUtc;

        // else, search for the version that exactly precedes the pointInTime
        DateTime? version = null;
        foreach (var v in versions)
            if (pointInTimeUtc >= v)
                return v;

        return null;
    }

    /// <summary>
    /// Returns an chronologically ordered list of versions (in universal time) for this repository
    /// </summary>
    /// <returns></returns>
    public async IAsyncEnumerable<DateTime> GetVersionsAsync()
    {
        await using var db = GetStateDbContext();

        foreach (var dt in db.PointerFileEntries
                     .Select(pfe => pfe.VersionUtc)
                     .Distinct()
                     .OrderBy(version => version)
                     .Select(pfe => DateTime.SpecifyKind(pfe, DateTimeKind.Utc)))
        {
            yield return dt;
        }
    }

    private static readonly PointerFileEntryEqualityComparer pfeEqualityComparer = new();
}