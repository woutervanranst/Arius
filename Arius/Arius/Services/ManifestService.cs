using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal class ManifestService
    {
        public static IEnumerable<HashValue> GetManifestHashes()
        {
            using var db = new ManifestStore();
            return db.Manifests.Select(m => new HashValue {Value = m.HashValue});
        }

        public static ManifestEntry AddManifest(BinaryFile f)
        {
            using var db = new ManifestStore();

            var me = new ManifestEntry()
            {
                HashValue = f.ManifestHash!.Value.Value,
                Chunks = f.Chunks.Select((cf, i) => 
                    new OrderedChunk()
                    {
                        ManifestHashValue = f.ManifestHash.Value.Value,
                        ChunkHashValue = cf.Hash.Value,
                        Order = i
                    }).ToList()
            };

            db.Manifests.Add(me);
            db.SaveChanges();

            return me;
        }
    }

    internal static class ManifestEntryExtensions
    { 
        /// <summary>
        /// Get the last entries per RelativeName
        /// </summary>
        /// <param name="includeLastDeleted">include deleted items</param>
        /// <returns></returns>
        public static IEnumerable<PointerFileEntry> GetLastEntries(this ManifestEntry m, bool includeLastDeleted = false)
        {
            var r = m.Entries
                .GroupBy(e => e.RelativeName)
                .Select(g => g.OrderBy(e => e.Version).Last());

            if (includeLastDeleted)
                return r;
            else
                return r.Where(e => !e.IsDeleted);
        }
    }
}