using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;

namespace Arius.Models
{
    internal class Manifest //Marked as internal for Unit Testing
    {
        [JsonConstructor]
        public Manifest(IEnumerable<PointerFileEntry> pointerFileEntries, IEnumerable<string> chunkNames, string hash)
        {
            _pointerFileEntries = pointerFileEntries.ToList();
            ChunkNames = chunkNames;
            Hash = hash;
        }

        public Manifest(IEnumerable<string> chunkNames, string hash)
        {
            _pointerFileEntries = new List<PointerFileEntry>();
            ChunkNames = chunkNames;
            Hash = hash;
        }

        // --- PROPERTIES

        [JsonInclude]
        public IEnumerable<PointerFileEntry> PointerFileEntries => _pointerFileEntries;
        private readonly List<PointerFileEntry> _pointerFileEntries;

        [JsonInclude]
        public IEnumerable<string> ChunkNames { get; private set; }

        /// <summary>
        /// Hash of the unencrypted LocalContentFiles
        /// </summary>
        [JsonInclude]
        public string Hash { get; private set; }

        // --- METHODS
        /// <summary>
        /// Get the last entries per RelativeName
        /// </summary>
        /// <param name="includeLastDeleted">include deleted items</param>
        /// <returns></returns>
        internal IEnumerable<PointerFileEntry> GetLastEntries(bool includeLastDeleted = false)
        {
            var r = _pointerFileEntries
                .GroupBy(lcfe => lcfe.RelativeName)
                .Select(g => g
                    .OrderBy(lcfe => lcfe.Version)
                    .Last());

            if (includeLastDeleted)
                return r;
            else
                return r.Where(afpe => !afpe.IsDeleted);
        }

        internal void AddEntries(IEnumerable<PointerFileEntry> entries)
        {
            _pointerFileEntries.AddRange(entries);
        }
        

        // --- RECORD DEFINITION & HELPERS
        internal static List<PointerFileEntry> GetPointerFileEntries(IEnumerable<IPointerFile> pointerFiles)
        {
            return pointerFiles.Select(pf => GetPointerFileEntry(pf)).ToList();
        }
        private static PointerFileEntry GetPointerFileEntry(IPointerFile pointerFile)
        {
            return new PointerFileEntry(pointerFile.RelativeName, 
                DateTime.UtcNow, 
                false, 
                pointerFile.CreationTimeUtc,
                pointerFile.LastWriteTimeUtc);
        }


        public sealed record PointerFileEntry(string RelativeName, DateTime Version, bool IsDeleted, DateTime? CreationTimeUtc, DateTime? LastWriteTimeUtc);
    }
}