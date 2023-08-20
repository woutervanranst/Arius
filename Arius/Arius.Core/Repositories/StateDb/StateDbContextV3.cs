﻿using Arius.Core.Models;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Repositories.StateDb;


internal record PointerFileEntryDto
{
    public byte[] BinaryHash   { get; init; }
    public string RelativeName { get; init; }

    /// <summary>
    /// Version (in Universal Time)
    /// </summary>
    public DateTime VersionUtc { get; init; }
    public bool      IsDeleted        { get; init; }
    public DateTime? CreationTimeUtc  { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }

    public virtual ChunkEntry Chunk { get; init; }
}

internal record ChunkEntry
{
    public byte[]      Hash              { get; init; }
    public long        OriginalLength    { get; init; }
    public long        ArchivedLength    { get; init; }
    public long        IncrementalLength { get; init; }
    public int         ChunkCount        { get; init; }
    public AccessTier? AccessTier        { get; init; } // AcessTier is null for the ChunkEntry of a chunked BinaryFile

    public virtual ICollection<PointerFileEntryDto> PointerFileEntries { get; set; }
}

internal class StateDbContext : DbContext
{
    public virtual DbSet<PointerFileEntryDto> PointerFileEntries { get; set; }
    public virtual DbSet<ChunkEntry>    ChunkEntries   { get; set; }

    private readonly string dbPath;
    private readonly Action<int> hasChanges;

    ///// <summary>
    ///// Required for EF Migrations (potentially also Moq, UnitTests but not sure)
    ///// </summary>
    //public StateDbContext()
    //{
    //}
    internal StateDbContext(string dbPath) : this(dbPath, new Action<int>(_ => { }))
    {
    }
    internal StateDbContext(string dbPath, Action<int> hasChanges)
    {
        this.dbPath = dbPath;
        this.hasChanges = hasChanges;
    }


    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        /* Database is locked -> Cache = shared as per https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
         *  NOTE if it still fails, try 'pragma temp_store=memory'
         */
        options.UseSqlite($"Data Source={dbPath};Cache=Shared",
            sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(60); //set command timeout to 60s to avoid concurrency errors on 'table is locked'
            });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var cmb = modelBuilder.Entity<ChunkEntry>();
        cmb.ToTable("ChunkEntries");
        cmb.HasKey(c => c.Hash);
        cmb.HasIndex(c => c.Hash).IsUnique();

        //builder.Property(c => c.Hash)
        //    .HasColumnName("Hash");
        //.HasConversion(bh => bh.Value, value => new BinaryHash(value));
        //.HasConversion(new MyValueConverter());

        cmb.Property(c => c.AccessTier)
            .HasConversion(v => v.ToString(), v => (AccessTier)v);
            //{
            //    return t.ToString();

            //    //if (t == AccessTier.Hot)
            //    //    return 10;
            //    //if (t == AccessTier.Cool)
            //    //    return 20;
            //    //if (t == AccessTier.Cold)
            //    //    return 30;
            //    //if (t == AccessTier.Archive)
            //    //    return 40;
            //}, t =>
            //{
            //    t switch
            //    {

            //    }
            //});

        var pfemb = modelBuilder.Entity<PointerFileEntryDto>();
        pfemb.ToTable("PointerFileEntries");
        pfemb.HasKey(pfe => new { pfe.BinaryHash, pfe.RelativeName, pfe.VersionUtc });
        pfemb.HasIndex(pfe => pfe.BinaryHash);
        pfemb.HasIndex(pfe => pfe.VersionUtc); //to facilitate Versions.Distinct
        pfemb.HasIndex(pfe => pfe.RelativeName); //to facilitate PointerFileEntries.GroupBy(RelativeName)

        pfemb.Property(pfe => pfe.RelativeName)
            .HasConversion(new RemovePointerFileExtensionConverter());

        // PointerFileEntries * -- 1 Chunk
        pfemb.HasOne(pfe => pfe.Chunk)
            .WithMany(c => c.PointerFileEntries)
            .HasForeignKey(pfe => pfe.BinaryHash);
    }

    public override int SaveChanges()
    {
        var numChanges = base.SaveChanges();
        hasChanges(numChanges);
        return numChanges;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        var numChanges = await base.SaveChangesAsync(cancellationToken);
        hasChanges(numChanges);
        return numChanges;
    }

    private class RemovePointerFileExtensionConverter : ValueConverter<string, string>
    {
        public RemovePointerFileExtensionConverter()
            : base(
                v => v.RemoveSuffix(PointerFile.Extension, StringComparison.InvariantCultureIgnoreCase), // Convert from Model to Provider (code to db)
                v => $"{v}{PointerFile.Extension}") // Convert from Provider to Model (db to code)
        {
        }
    }
}