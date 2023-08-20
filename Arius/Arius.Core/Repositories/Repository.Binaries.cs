using System.Linq;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    /// <summary>
    /// Get the count of (distinct) BinaryHashes
    /// </summary>
    /// <returns></returns>
    public async Task<int> CountBinariesAsync()
    {
        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.Select(pfe => pfe.BinaryHash).Distinct().CountAsync();
    }

    public async Task<bool> BinaryExistsAsync(BinaryHash bh)
    {

        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.AnyAsync(pfe => pfe.BinaryHash == bh.Value);
    }
}