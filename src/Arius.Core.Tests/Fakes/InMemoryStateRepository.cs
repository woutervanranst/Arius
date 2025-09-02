using Arius.Core.Models;
using Arius.Core.Repositories;
using System.Collections.Concurrent;

namespace Arius.Core.Tests.Fakes;

internal class InMemoryStateRepository : IStateRepository
{
    private readonly ConcurrentDictionary<Hash, BinaryProperties> binaryProperties = new();
    private readonly ConcurrentDictionary<(Hash Hash, string RelativeName), PointerFileEntry> pointerFileEntries = new();
    private int hasChangesFlag;

    public FileInfo StateDatabaseFile => throw new NotImplementedException("InMemoryStateRepository does not use a file");

    public bool HasChanges => Interlocked.CompareExchange(ref hasChangesFlag, 0, 0) == 1;

    public void Vacuum()
    {
        // No-op for in-memory implementation
    }

    public void Delete()
    {
        binaryProperties.Clear();
        pointerFileEntries.Clear();
        SetHasChanges();
    }

    public BinaryProperties? GetBinaryProperty(Hash h)
    {
        binaryProperties.TryGetValue(h, out var result);
        return result;
    }

    public void AddBinaryProperties(params BinaryProperties[] bps)
    {
        foreach (var bp in bps)
        {
            binaryProperties.TryAdd(bp.Hash, bp);
        }
        SetHasChanges();
    }

    public void UpsertPointerFileEntries(params PointerFileEntry[] pfes)
    {
        foreach (var pfe in pfes)
        {
            var key = (pfe.Hash, pfe.RelativeName);
            pointerFileEntries.AddOrUpdate(key, pfe, (_, existing) => existing with 
            { 
                CreationTimeUtc = pfe.CreationTimeUtc,
                LastWriteTimeUtc = pfe.LastWriteTimeUtc
            });
        }
        SetHasChanges();
    }

    public IEnumerable<PointerFileEntry> GetPointerFileEntries(string relativeNamePrefix, bool includeBinaryProperties = false)
    {
        //var dbRelativeNamePrefix = relativeNamePrefix.TrimStart('/');
        
        foreach (var kvp in pointerFileEntries)
        {
            if (kvp.Key.RelativeName.StartsWith(relativeNamePrefix))
            {
                var pfe = kvp.Value;
                if (includeBinaryProperties && binaryProperties.TryGetValue(pfe.Hash, out var bp))
                {
                    yield return pfe with { BinaryProperties = bp };
                }
                else
                {
                    yield return pfe;
                }
            }
        }
    }

    public void DeletePointerFileEntries(Func<PointerFileEntry, bool> shouldBeDeleted)
    {
        var keysToRemove = new List<(Hash Hash, string RelativeName)>();
        
        foreach (var kvp in pointerFileEntries)
        {
            if (shouldBeDeleted(kvp.Value))
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            pointerFileEntries.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            SetHasChanges();
        }
    }

    private void SetHasChanges()
    {
        Interlocked.Exchange(ref hasChangesFlag, 1);
    }
}