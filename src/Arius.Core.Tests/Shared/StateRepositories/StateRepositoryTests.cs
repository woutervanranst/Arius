using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Shared.StateRepositories;

public class StateRepositoryTests : IDisposable
{
    private readonly FixtureWithFileSystem fixture;
    private readonly StateRepositoryDbContextPool contextPool;
    private readonly StateRepository stateRepository;
    private readonly FileEntry stateFile;

    public StateRepositoryTests()
    {
        fixture = new FixtureWithFileSystem();
        
        // Create state database file path in test folder using proper filesystem
        var stateFileName = $"test-state-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.db";
        stateFile = new FileEntry(fixture.FileSystem, $"/{stateFileName}");
        
        contextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: true, NullLogger<StateRepositoryDbContextPool>.Instance);
        stateRepository = new StateRepository(contextPool);
    }

    public void Dispose()
    {
        stateRepository.Delete();
        fixture?.Dispose();
    }

    [Fact]
    public void StateRepository_Should_Create_Database_File()
    {
        stateFile.Exists.ShouldBeTrue();
        stateRepository.StateDatabaseFile.ShouldBe(stateFile);
    }

    [Fact]
    public void HasChanges_Should_Start_False()
    {
        stateRepository.HasChanges.ShouldBeFalse();
        contextPool.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void AddBinaryProperties_Should_Insert_Records_And_Set_HasChanges()
    {
        // Arrange
        var hash1 = CreateTestHash(1);
        var hash2 = CreateTestHash(2);
        
        var bp1 = new BinaryProperties
        {
            Hash = hash1,
            OriginalSize = 100,
            ArchivedSize = 80,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        
        var bp2 = new BinaryProperties
        {
            Hash = hash2,
            OriginalSize = 200,
            ParentHash = hash1,
            StorageTier = StorageTier.Cold,
            PointerFileEntries = new List<PointerFileEntry>()
        };

        // Act
        stateRepository.AddBinaryProperties(bp1, bp2);

        // Assert
        stateRepository.HasChanges.ShouldBeTrue();
        
        var retrieved1 = stateRepository.GetBinaryProperty(hash1);
        retrieved1.ShouldNotBeNull();
        retrieved1.Hash.ShouldBe(hash1);
        retrieved1.OriginalSize.ShouldBe(100);
        retrieved1.ArchivedSize.ShouldBe(80);
        retrieved1.StorageTier.ShouldBe(StorageTier.Hot);
        retrieved1.ParentHash.ShouldBeNull();

        var retrieved2 = stateRepository.GetBinaryProperty(hash2);
        retrieved2.ShouldNotBeNull();
        retrieved2.Hash.ShouldBe(hash2);
        retrieved2.OriginalSize.ShouldBe(200);
        retrieved2.ArchivedSize.ShouldBeNull();
        retrieved2.StorageTier.ShouldBe(StorageTier.Cold);
        retrieved2.ParentHash.ShouldBe(hash1);
    }

    [Fact]
    public void GetBinaryProperty_Should_Return_Null_For_NonExistent_Hash()
    {
        // Arrange
        var nonExistentHash = CreateTestHash(999);

        // Act
        var result = stateRepository.GetBinaryProperty(nonExistentHash);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void UpsertPointerFileEntries_Should_Insert_New_Records_And_Set_HasChanges()
    {
        // Arrange
        var hash = CreateTestHash(1);
        var creationTime = DateTime.UtcNow.AddDays(-1);
        var writeTime = DateTime.UtcNow;
        
        // First create the BinaryProperties that the PointerFileEntry will reference
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        stateRepository.AddBinaryProperties(bp);
        
        var pfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = creationTime,
            LastWriteTimeUtc = writeTime,
            BinaryProperties = null!
        };

        // Act
        stateRepository.UpsertPointerFileEntries(pfe);

        // Assert
        stateRepository.HasChanges.ShouldBeTrue();
        
        var entries = stateRepository.GetPointerFileEntries("/test/").ToList();
        entries.ShouldHaveSingleItem();
        
        var retrieved = entries[0];
        retrieved.Hash.ShouldBe(hash);
        retrieved.RelativeName.ShouldBe("/test/file.txt.pointer.arius");
        retrieved.CreationTimeUtc.ShouldBe(creationTime);
        retrieved.LastWriteTimeUtc.ShouldBe(writeTime);
    }

    [Fact]
    public void UpsertPointerFileEntries_Should_Update_Existing_Records_And_Set_HasChanges()
    {
        // Arrange - Insert initial record
        var hash = CreateTestHash(1);
        var initialCreationTime = DateTime.UtcNow.AddDays(-2);
        var initialWriteTime = DateTime.UtcNow.AddDays(-1);
        
        // First create the BinaryProperties that the PointerFileEntry will reference
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        stateRepository.AddBinaryProperties(bp);
        
        var initialPfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = initialCreationTime,
            LastWriteTimeUtc = initialWriteTime,
            BinaryProperties = null!
        };
        
        stateRepository.UpsertPointerFileEntries(initialPfe);
        
        // Reset HasChanges to test update
        var initialHasChanges = stateRepository.HasChanges;
        initialHasChanges.ShouldBeTrue();
        
        // Create new instance to reset HasChanges
        var newContextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: false, NullLogger<StateRepositoryDbContextPool>.Instance);
        var newStateRepository = new StateRepository(newContextPool);
        newStateRepository.HasChanges.ShouldBeFalse();
        
        // Arrange - Update with new timestamps
        var newCreationTime = DateTime.UtcNow.AddHours(-1);
        var newWriteTime = DateTime.UtcNow;
        
        var updatedPfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = newCreationTime,
            LastWriteTimeUtc = newWriteTime,
            BinaryProperties = null!
        };

        // Act
        newStateRepository.UpsertPointerFileEntries(updatedPfe);

        // Assert
        newStateRepository.HasChanges.ShouldBeTrue();
        
        var entries = newStateRepository.GetPointerFileEntries("/test/").ToList();
        entries.ShouldHaveSingleItem();
        
        var retrieved = entries[0];
        retrieved.Hash.ShouldBe(hash);
        retrieved.RelativeName.ShouldBe("/test/file.txt.pointer.arius");
        retrieved.CreationTimeUtc.ShouldBe(newCreationTime);
        retrieved.LastWriteTimeUtc.ShouldBe(newWriteTime);
    }

    [Fact]
    public void UpsertPointerFileEntries_Should_Not_Set_HasChanges_When_No_Changes()
    {
        // Arrange - Insert initial record
        var hash = CreateTestHash(1);
        var creationTime = DateTime.UtcNow.AddDays(-1);
        var writeTime = DateTime.UtcNow;
        
        // First create the BinaryProperties that the PointerFileEntry will reference
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        stateRepository.AddBinaryProperties(bp);
        
        var pfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = creationTime,
            LastWriteTimeUtc = writeTime,
            BinaryProperties = null!
        };
        
        stateRepository.UpsertPointerFileEntries(pfe);
        stateRepository.HasChanges.ShouldBeTrue();
        
        // Create new instance to reset HasChanges
        var newContextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: false, NullLogger<StateRepositoryDbContextPool>.Instance);
        var newStateRepository = new StateRepository(newContextPool);
        newStateRepository.HasChanges.ShouldBeFalse();

        // Act - Upsert same data (no changes)
        newStateRepository.UpsertPointerFileEntries(pfe);

        // Assert
        newStateRepository.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void GetPointerFileEntries_Should_Filter_By_Prefix()
    {
        // Arrange
        var hash1 = CreateTestHash(1);
        var hash2 = CreateTestHash(2);
        var hash3 = CreateTestHash(3);
        
        // First create the BinaryProperties that the PointerFileEntries will reference
        var bp1 = new BinaryProperties { Hash = hash1, OriginalSize = 100, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        var bp2 = new BinaryProperties { Hash = hash2, OriginalSize = 200, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        var bp3 = new BinaryProperties { Hash = hash3, OriginalSize = 300, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        stateRepository.AddBinaryProperties(bp1, bp2, bp3);
        
        var pfe1 = new PointerFileEntry
        {
            Hash = hash1,
            RelativeName = "/folder1/file1.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        var pfe2 = new PointerFileEntry
        {
            Hash = hash2,
            RelativeName = "/folder1/file2.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        var pfe3 = new PointerFileEntry
        {
            Hash = hash3,
            RelativeName = "/folder2/file3.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        stateRepository.UpsertPointerFileEntries(pfe1, pfe2, pfe3);

        // Act
        var folder1Entries = stateRepository.GetPointerFileEntries("/folder1/").ToList();
        var folder2Entries = stateRepository.GetPointerFileEntries("/folder2/").ToList();
        var allEntries = stateRepository.GetPointerFileEntries("/").ToList();

        // Assert
        folder1Entries.Count.ShouldBe(2);
        folder1Entries.ShouldContain(e => e.Hash == hash1);
        folder1Entries.ShouldContain(e => e.Hash == hash2);
        
        folder2Entries.ShouldHaveSingleItem();
        folder2Entries[0].Hash.ShouldBe(hash3);
        
        allEntries.Count.ShouldBe(3);
    }

    [Fact]
    public void GetPointerFileEntries_With_IncludeBinaryProperties_Should_Load_Related_Data()
    {
        // Arrange
        var hash = CreateTestHash(1);
        
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        
        var pfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        stateRepository.AddBinaryProperties(bp);
        stateRepository.UpsertPointerFileEntries(pfe);

        // Act
        var entries = stateRepository.GetPointerFileEntries("/test/", includeBinaryProperties: true).ToList();

        // Assert
        entries.ShouldHaveSingleItem();
        var entry = entries[0];
        entry.BinaryProperties.ShouldNotBeNull();
        entry.BinaryProperties.Hash.ShouldBe(hash);
        entry.BinaryProperties.OriginalSize.ShouldBe(100);
        entry.BinaryProperties.StorageTier.ShouldBe(StorageTier.Hot);
    }

    [Fact]
    public void DeletePointerFileEntries_Should_Remove_Records_And_Set_HasChanges()
    {
        // Arrange
        var hash1 = CreateTestHash(1);
        var hash2 = CreateTestHash(2);
        
        // First create the BinaryProperties that the PointerFileEntries will reference
        var bp1 = new BinaryProperties { Hash = hash1, OriginalSize = 100, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        var bp2 = new BinaryProperties { Hash = hash2, OriginalSize = 200, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        stateRepository.AddBinaryProperties(bp1, bp2);
        
        var pfe1 = new PointerFileEntry
        {
            Hash = hash1,
            RelativeName = "/test/file1.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        var pfe2 = new PointerFileEntry
        {
            Hash = hash2,
            RelativeName = "/test/file2.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        stateRepository.UpsertPointerFileEntries(pfe1, pfe2);
        
        // Reset HasChanges to test delete
        var newContextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: false, NullLogger<StateRepositoryDbContextPool>.Instance);
        var newStateRepository = new StateRepository(newContextPool);
        newStateRepository.HasChanges.ShouldBeFalse();

        // Act - Delete entries with hash1
        newStateRepository.DeletePointerFileEntries(entry => entry.Hash == hash1);

        // Assert
        newStateRepository.HasChanges.ShouldBeTrue();
        
        var remainingEntries = newStateRepository.GetPointerFileEntries("/test/").ToList();
        remainingEntries.ShouldHaveSingleItem();
        remainingEntries[0].Hash.ShouldBe(hash2);
    }

    [Fact]
    public void DeletePointerFileEntries_Should_Not_Set_HasChanges_When_No_Matching_Records()
    {
        // Arrange
        var hash = CreateTestHash(1);
        
        // First create the BinaryProperties that the PointerFileEntry will reference
        var bp = new BinaryProperties { Hash = hash, OriginalSize = 100, StorageTier = StorageTier.Hot, PointerFileEntries = new List<PointerFileEntry>() };
        stateRepository.AddBinaryProperties(bp);
        
        var pfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/test/file.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };
        
        stateRepository.UpsertPointerFileEntries(pfe);
        
        // Reset HasChanges
        var newContextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: false, NullLogger<StateRepositoryDbContextPool>.Instance);
        var newStateRepository = new StateRepository(newContextPool);
        newStateRepository.HasChanges.ShouldBeFalse();

        // Act - Try to delete non-matching entries
        var nonExistentHash = CreateTestHash(999);
        newStateRepository.DeletePointerFileEntries(entry => entry.Hash == nonExistentHash);

        // Assert
        newStateRepository.HasChanges.ShouldBeFalse();
        
        var allEntries = newStateRepository.GetPointerFileEntries("/").ToList();
        allEntries.ShouldHaveSingleItem();
        allEntries[0].Hash.ShouldBe(hash);
    }

    [Fact]
    public void Vacuum_Should_Work_Without_Error()
    {
        // Arrange
        var hash = CreateTestHash(1);
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        
        stateRepository.AddBinaryProperties(bp);

        // Act & Assert - Should not throw
        stateRepository.Vacuum();
        
        // Verify data still exists
        var retrieved = stateRepository.GetBinaryProperty(hash);
        retrieved.ShouldNotBeNull();
    }

    [Fact]
    public void Data_Should_Persist_Across_Context_Recreations()
    {
        // Arrange
        var hash = CreateTestHash(1);
        var bp = new BinaryProperties
        {
            Hash = hash,
            OriginalSize = 100,
            StorageTier = StorageTier.Hot,
            PointerFileEntries = new List<PointerFileEntry>()
        };
        
        var pfe = new PointerFileEntry
        {
            Hash = hash,
            RelativeName = "/persistent/file.txt.pointer.arius",
            CreationTimeUtc = DateTime.UtcNow,
            LastWriteTimeUtc = DateTime.UtcNow,
            BinaryProperties = null!
        };

        // Act - Insert data with original repository
        stateRepository.AddBinaryProperties(bp);
        stateRepository.UpsertPointerFileEntries(pfe);

        // Create new repository instance pointing to same database
        var newContextPool = new StateRepositoryDbContextPool(stateFile, ensureCreated: false, NullLogger<StateRepositoryDbContextPool>.Instance);
        var newStateRepository = new StateRepository(newContextPool);

        // Assert - Data should persist
        var retrievedBp = newStateRepository.GetBinaryProperty(hash);
        retrievedBp.ShouldNotBeNull();
        retrievedBp.Hash.ShouldBe(hash);
        retrievedBp.OriginalSize.ShouldBe(100);
        
        var retrievedPfes = newStateRepository.GetPointerFileEntries("/persistent/").ToList();
        retrievedPfes.ShouldHaveSingleItem();
        retrievedPfes[0].Hash.ShouldBe(hash);
        retrievedPfes[0].RelativeName.ShouldBe("/persistent/file.txt.pointer.arius");
    }

    private static Hash CreateTestHash(int seed)
    {
        var bytes = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            bytes[i] = (byte)(seed + i);
        }
        return Hash.FromBytes(bytes);
    }
}