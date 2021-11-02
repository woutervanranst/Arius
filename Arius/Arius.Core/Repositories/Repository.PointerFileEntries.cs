﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
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
            BinaryHash = pf.Hash,
            RelativeName = pf.RelativeName,
            VersionUtc = versionUtc,
            IsDeleted = false,
            CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(),
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
        var entries = await pfeRepo.GetEntriesAsync();

        var lastVersion = entries.AsParallel()
            .Where(p => pfe.RelativeName.Equals(p.RelativeName))
            .OrderBy(p => p.VersionUtc)
            .LastOrDefault();

        var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one
        if (toAdd)
        {
            await pfeRepo.AppendAsync(pfe);

            //Ensure the version is in the SORTED master list
            var versions = await versionsTask;
            if (!versions.Contains(pfe.VersionUtc))
                lock (versions)
                    versions.Add(pfe.VersionUtc);

            if (pfe.IsDeleted)
            {
                logger.LogInformation($"Deleted '{pfe.RelativeName}'");
                return CreatePointerFileEntryResult.InsertedDeleted;
            }
            else
            {
                logger.LogInformation($"Added '{pfe.RelativeName}'");
                return CreatePointerFileEntryResult.Inserted;
            }
        }

        return CreatePointerFileEntryResult.NoChange;
    }

    private static readonly PointerFileEntryEqualityComparer equalityComparer = new();



    public async Task<IEnumerable<PointerFileEntry>> GetCurrentEntries(bool includeDeleted)
    {
        return await GetPointerFileEntries(DateTime.UtcNow, includeDeleted);
    }

    /// <summary>
    /// Get the PointerFileEntries at the given version.
    /// If no version is specified, the current (most recent) will be returned
    /// </summary>
    public async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntries(DateTime pointInTimeUtc, bool includeDeleted)
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
        var pfes = await GetPointerFileEntriesAsync();

        //TODO an exception here is swallowed

        var r = pfes
            .GroupBy(pfe => pfe.RelativeName)
            .Select(g => g.Where(pfe => pfe.VersionUtc <= versionUtc)).Where(c => c.Any())
            .Select(z => z.OrderBy(pfe => pfe.VersionUtc).Last())
            .ToArray();

        return r;
    }

    /// <summary>
    /// Get All PointerFileEntries
    /// </summary>
    /// <returns></returns>
    internal async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntriesAsync()
    {
        return await pfeRepo.GetEntriesAsync();
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
    public async Task<IEnumerable<DateTime>> GetVersionsAsync() => await versionsTask;







    public async Task CommitPointerFileVersion()
    {
        await pfeRepo.CommitPointerFileVersion();
    }
}