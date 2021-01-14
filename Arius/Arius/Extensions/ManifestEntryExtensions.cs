using System.Collections.Generic;
using System.Linq;
using Arius.Repositories;

namespace Arius.Services
{
    internal static class ManifestEntryExtensions
    { 
        /// <summary>
        /// Get the last entries per RelativeName
        /// </summary>
        /// <param name="includeLastDeleted">include deleted items</param>
        /// <returns></returns>
        public static IEnumerable<AzureRepository.PointerFileEntry> GetLastEntries(this AzureRepository.ManifestEntry m, bool includeLastDeleted = false)
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