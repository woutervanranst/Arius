using Arius.Core.Models;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.StateRepositories;

internal class StateRepository : IStateRepository
{
    private readonly StateRepositoryDbContextFactory factory;

    public StateRepository(StateRepositoryDbContextFactory factory)
    {
        this.factory = factory;
    }

    public FileInfo StateDatabaseFile => factory.StateDatabaseFile;
    public bool HasChanges => factory.HasChanges;

    public void Vacuum() => factory.Vacuum();
    public void Delete() => factory.Delete();

    // --- BINARYPROPERTIES

    private static readonly Func<StateRepositoryDbContext, Hash, BinaryProperties?> findBinaryProperty = 
        EF.CompileQuery((StateRepositoryDbContext db, Hash h) =>
            db.Set<BinaryProperties>()
                .AsNoTracking()
                .SingleOrDefault(x => x.Hash == h));

    public BinaryProperties? GetBinaryProperty(Hash h)
    {
        using var context = factory.CreateContext();

        return findBinaryProperty(context, h);
    }

    public void AddBinaryProperties(params BinaryProperties[] bps)
    {
        using var context = factory.CreateContext();

        context.BinaryProperties.AddRange(bps);
        context.SaveChanges();
    }

    // --- POINTERFILEENTRIES

    public void UpsertPointerFileEntries(params PointerFileEntry[] pfes)
    {
        using var context = factory.CreateContext();

        context.BulkInsertOrUpdate(pfes);

        factory.SetHasChanges(); // BulkInsertOrUpdate doesn't trigger SaveChanges callback
        // TODO : use the OnChanges callback of BulkInsertOrUpdate when available

        //foreach (var pfe in pfes)
        //{
        //    var existingPfe = context.PointerFileEntries.Find(pfe.Hash, pfe.RelativeName);

        //    if (existingPfe is null)
        //    {
        //        context.PointerFileEntries.Add(pfe);
        //    }
        //    else
        //    {
        //        existingPfe.CreationTimeUtc  = pfe.CreationTimeUtc;
        //        existingPfe.LastWriteTimeUtc = pfe.LastWriteTimeUtc;
        //    }
        //}

        //context.SaveChanges();
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

    public IEnumerable<PointerFileEntry> GetPointerFileEntries(string relativeNamePrefix, bool includeBinaryProperties = false)
    {
        using var context = factory.CreateContext();

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

    //public IEnumerable<PointerFileEntryDto> GetPointerFileEntries()
    //{
    //    using var context = GetContext();
    //    foreach (var pfe in context.PointerFileEntries)
    //        yield return pfe;
    //}

    public void DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted)
    {
        using var context = factory.CreateContext();

        var entriesToDelete = context.PointerFileEntries.Where(shouldBeDeleted).ToArray();
        context.BulkDelete(entriesToDelete);

        if (entriesToDelete.Any())
            factory.SetHasChanges();
    }
}
