using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
        var pfeDto = new PointerFileEntry
        {
            BinaryHash       = pf.BinaryHash,
            RelativeName     = pf.RelativeName,
            VersionUtc       = versionUtc,
            IsDeleted        = false,
            CreationTimeUtc  = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(),
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName).ToUniversalTime(),
        }.ToPointerFileEntryDto();

        return await CreatePointerFileEntryIfNotExistsAsync(pfeDto);
    }

    /// <summary>
    /// Create a PointerFileEntry that is deleted form the given PointerFileEntry
    /// </summary>
    public async Task<CreatePointerFileEntryResult> CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime versionUtc)
    {
        var pfeDto = pfe.ToPointerFileEntryDto() with
        {
            VersionUtc = versionUtc,
            IsDeleted = true,
            CreationTimeUtc = null,
            LastWriteTimeUtc = null
        };

        return await CreatePointerFileEntryIfNotExistsAsync(pfeDto);
    }

    /// <summary>
    /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
    /// </summary>
    private async Task<CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntryDto pfe)
    {
        await using var db = GetStateDbContext();

        var lastVersion = await db.PointerFileEntries
            .Where(pfe0 => pfe.RelativeName.Equals(pfe0.RelativeName))
            .OrderBy(pfe0 => pfe0.VersionUtc)
            .LastOrDefaultAsync();

        var toAdd = !pfeDtoEqualityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one
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

        return CreatePointerFileEntryResult.NoChange;
    }

    public async Task<IEnumerable<PointerFileEntry>> GetCurrentPointerFileEntriesAsync(bool includeDeleted)
    {
        return await GetPointerFileEntriesAsync(DateTime.UtcNow, includeDeleted);
    }

    /// <summary>
    /// Get the PointerFileEntries at the given version.
    /// If no version is specified, the current (most recent) will be returned
    /// </summary>
    public async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntriesAsync(DateTime pointInTimeUtc, bool includeDeleted)
    {
        var pfes = await GetPointerFileEntriesAtPointInTimeAsync(pointInTimeUtc);

        if (includeDeleted)
            return pfes;
        else
            return pfes.Where(pfe => !pfe.IsDeleted);
    }

    private async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntriesAtPointInTimeAsync(DateTime pointInTimeUtc)
    {
        try
        {
            var versionUtc = await GetVersionAsync(pointInTimeUtc);

            var r = await GetPointerFileEntriesAtVersionAsync(versionUtc);

            return r;
        }
        catch (ArgumentException e)
        {
            logger.LogWarning($"{e.Message} Returning empty list of PointerFileEntries.");

            return Array.Empty<PointerFileEntry>();
        }
    }

    private async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntriesAtVersionAsync(DateTime versionUtc)
    {
        //TODO an exception here is swallowed

        await using var db = GetStateDbContext();

        var r = await db.PointerFileEntries.AsParallel()
            .GroupBy(pfe => pfe.RelativeName)
            .Select(g => g.Where(pfe => pfe.VersionUtc <= versionUtc))
            .ToAsyncEnumerable() //TODO ParallelEnumerable? //remove this and the dependency on Linq.Async?
            .Where(c => c.Any())
            .Select(z => z.OrderBy(pfe => pfe.VersionUtc).Last())
            .Select(pfe => pfe.ToPointerFileEntry())
            .ToArrayAsync();

        return r;
    }

    ///// <summary>
    ///// Get All PointerFileEntries
    ///// DO NOT USE?
    ///// TODO REMOVE ME
    ///// </summary>
    ///// <returns></returns>
    //internal async IAsyncEnumerable<PointerFileEntry> GetPointerFileEntriesAsync()
    //{
    //    await using var db = GetAriusDbContext();
    //    await foreach (var pfe in db.PointerFileEntries.AsAsyncEnumerable())
    //        yield return pfe;
    //}

    //internal IAsyncEnumerable<PointerFileEntry> GetPointerFileEntriesAsync(string relativeNamePrefix)
    //{
    //    return GetPointerFileEntriesAsync().Where(pfe => pfe.RelativeName.StartsWith(relativeNamePrefix, StringComparison.InvariantCultureIgnoreCase));
    //}

    internal async Task<int> CountPointerFileEntriesAsync()
    {
        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.CountAsync();
    }



    /// <summary>
    /// Get the version that corresponds to the state of the archive at pointInTime
    /// </summary>
    /// <param name="pointInTimeUtc"></param>
    /// <returns></returns>
    private async Task<DateTime> GetVersionAsync(DateTime pointInTimeUtc)
    {
        var versions = GetVersionsAsync().Reverse().ToEnumerable(); // TODO huh where does the await go?

        // if the pointInTime is a version - return the pointInTime (optimization in case of the GUI dropdown)
        if (versions.Contains(pointInTimeUtc))
            return pointInTimeUtc;

        // else, search for the version that exactly precedes the pointInTime
        DateTime? version = null;
        foreach (var v in versions)
        {
            if (pointInTimeUtc >= v)
            {
                version = v;
                break;
            }
        }

        if (version is null)
            throw new ArgumentException($"{nameof(GetVersionAsync)}: No version found for {nameof(pointInTimeUtc)} {pointInTimeUtc}.");

        return version.Value;
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

    private static readonly PointerFileEntryDtoEqualityComparer pfeDtoEqualityComparer = new();
}