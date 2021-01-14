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
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private class ManifestRepository
        {
            public ManifestRepository(ICommandExecutorOptions options, ILogger<ManifestRepository> logger)
            {
                _logger = logger;

                var o = (AzureRepository.IAzureRepositoryOptions) options;
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

                _csa = CloudStorageAccount.Parse(connectionString);

                Init();
            }

            private readonly ILogger<ManifestRepository> _logger;
            private readonly CloudStorageAccount _csa;

            private void Init()
            {
                //using var db = new ManifestStore();
                //db.Database.EnsureCreated();

                var tableclient = _csa.CreateCloudTableClient(new TableClientConfiguration());
                var x = tableclient.GetTableReference("test");
                x.CreateIfNotExists();


            }

            public ManifestEntry AddManifest(BinaryFile f)
            {
                using var db = new ManifestStore();

                var me = new ManifestEntry()
                {
                    HashValue = f.ManifestHash!.Value.Value,
                    Chunks = f.Chunks.Select((cf, i) => //TO CHECK zitten alle Chunks hierin of enkel de geuploade? to test: delete 1 chunk remote en run opnieuw
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

            public void UpdateManifest(DirectoryInfo root, PointerFile pointerFile, DateTime version)
            {
                // Update the manifest
                using (var db = new ManifestStore())
                {
                    var me = db.Manifests
                        .Include(me => me.Entries)
                        .Single(m => m.HashValue == pointerFile.Hash!.Value);

                    //TODO iets met PointerFileEntryEqualityComparer?

                    var e = new PointerFileEntry
                    {
                        RelativeName = Path.GetRelativePath(root.FullName, pointerFile.FullName),
                        Version = version,
                        CreationTimeUtc = File.GetCreationTimeUtc(pointerFile.FullName), //TODO
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(pointerFile.FullName),
                        IsDeleted = false
                    };

                    var pfeec = new PointerFileEntryEqualityComparer();
                    if (!me.Entries.Contains(e, pfeec))
                        me.Entries.Add(e);

                    _logger.LogInformation($"Added {e.RelativeName}");

                    db.SaveChanges();
                }
            }

            public IEnumerable<ManifestEntry> GetAllEntries()
            {
                using var db = new ManifestStore();
                return db.Manifests.Include(m => m.Entries).ToList();
            }

            public void SetDeleted(ManifestEntry me, PointerFileEntry pfe, DateTime version)
            {
                using var db = new ManifestStore();
                var m = db.Manifests.Single(me2 => me2.HashValue == me.HashValue);

                m.Entries.Add(new PointerFileEntry()
                {
                    RelativeName = pfe.RelativeName,
                    Version = version,
                    IsDeleted = true,
                    CreationTimeUtc = null,
                    LastWriteTimeUtc = null
                });

                db.SaveChanges();

                _logger.LogInformation($"Marked {pfe.RelativeName} as deleted");
            }

            public List<ManifestEntry> GetAllManifestEntriesWithChunksAndPointerFileEntries()
            {
                using var db = new ManifestStore();
                return db.Manifests
                    .Include(a => a.Chunks)
                    .Include(a => a.Entries)
                    .ToList();
            }


            private class ManifestStore : DbContext
            {
                public DbSet<ManifestEntry> Manifests { get; set; }
                //public DbSet<OrderedChunk> Chunks { get; set; }

                protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                    => optionsBuilder.UseSqlite(@"Data Source=c:\arius.db");

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    var me = modelBuilder.Entity<ManifestEntry>();
                    me.HasKey(m => m.HashValue);
                    me.HasMany(m => m.Entries);
                    me.HasMany(m => m.Chunks);

                    var pfee = modelBuilder.Entity<PointerFileEntry>();
                    pfee.HasKey(pfe => new {pfe.RelativeName, pfe.Version});

                    var oce = modelBuilder.Entity<OrderedChunk>();
                    oce.HasKey(oc => new {oc.ManifestHashValue, oc.ChunkHashValue});

                }
            }
        }





        internal class ManifestEntry
        {
            public string HashValue { get; set; }
            public List<PointerFileEntry> Entries { get; init; } = new();
            public List<OrderedChunk> Chunks { get; init; } = new();
        }

        internal class PointerFileEntry
        {
            public string RelativeName { get; set; }
            public DateTime Version { get; set; }
            public bool IsDeleted { get; set; }
            public DateTime? CreationTimeUtc { get; set; }
            public DateTime? LastWriteTimeUtc { get; set; }
        }

        internal class OrderedChunk
        {
            public string ManifestHashValue { get; set; }
            public string ChunkHashValue { get; set; }
            public int Order { get; set; }
        }

    }
}