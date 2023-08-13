using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        var pfe = new PointerFileEntry()
        {
            BinaryHash       = pf.BinaryHash,
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
        await using var db = GetAriusDbContext();

        pfe = ToPlatformNeutral(pfe);

        var lastVersion = await db.PointerFileEntries
            .Where(pfe0 => pfe.RelativeName.Equals(pfe0.RelativeName))
            .OrderBy(pfe0 => pfe0.VersionUtc)
            .LastOrDefaultAsync();

        var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one
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

    private static readonly PointerFileEntryEqualityComparer equalityComparer = new();


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

        await using var db = GetAriusDbContext();
        var r = await db.PointerFileEntries.AsParallel()
            .GroupBy(pfe => pfe.RelativeName)
            .Select(g => g.Where(pfe => pfe.VersionUtc <= versionUtc))
            .ToAsyncEnumerable() //TODO ParallelEnumerable? //remove this and the dependency on Linq.Async?
            .Where(c => c.Any())
            .Select(z => z.OrderBy(pfe => pfe.VersionUtc).Last())
            .Select(pfe => ToPlatformSpecific(pfe))
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
        await using var db = GetAriusDbContext();
        return await db.PointerFileEntries.CountAsync();
    }



    /// <summary>
    /// Get the version that corresponds to the state of the archive at pointInTime
    /// </summary>
    /// <param name="pointInTimeUtc"></param>
    /// <returns></returns>
    private async Task<DateTime> GetVersionAsync(DateTime pointInTimeUtc)
    {
        var versions = (await GetVersionsAsync()).Reverse();

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
    public async Task<IEnumerable<DateTime>> GetVersionsAsync()
    {
        await using var db = GetAriusDbContext();
        return await db.PointerFileEntries
            .Select(pfe => pfe.VersionUtc)
            .Distinct()
            .OrderBy(version => version)
            .Select(pfe => DateTime.SpecifyKind(pfe, DateTimeKind.Utc))
            .ToArrayAsync();
    }



    private const char PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR = '/';

    private static PointerFileEntry ToPlatformNeutral(PointerFileEntry platformSpecific)
    {
        if (platformSpecific is null)
            return null;

        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformSpecific;

        return platformSpecific with { RelativeName = platformSpecific.RelativeName.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR) };
    }

    private static PointerFileEntry ToPlatformSpecific(PointerFileEntry platformNeutral)
    {
        // TODO UNIT TEST for linux pointers (already done if run in the github runner?

        if (platformNeutral is null)
            return null;

        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformNeutral;

        return platformNeutral with { RelativeName = platformNeutral.RelativeName.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar) };
    }
}