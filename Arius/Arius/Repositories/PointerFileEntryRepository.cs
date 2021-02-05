using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        // TODO KARL quid pattern of nested pratial classes
        private partial class PointerFileEntryRepository
        {
            public PointerFileEntryRepository(ICommandExecutorOptions options, ILogger<PointerFileEntryRepository> logger, ILoggerFactory loggerFactory)
            {
                _logger = logger;

                _repo = new CachedEncryptedPointerFileEntryRepository(options, loggerFactory.CreateLogger<CachedEncryptedPointerFileEntryRepository>());
            }

            private readonly ILogger<PointerFileEntryRepository> _logger;

            private readonly CachedEncryptedPointerFileEntryRepository _repo;

            //private static readonly PointerFileEntryEqualityComparer _pfeec = new();


            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pf, DateTime version)
            {
                var pfe = CreatePointerFileEntry(pf, version);

                await CreatePointerFileEntryIfNotExistsAsync(pfe);
            }

            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe, DateTime version, bool isDeleted = false)
            {
                var pfe2 = CreatePointerFileEntry(pfe, version, isDeleted);

                await CreatePointerFileEntryIfNotExistsAsync(pfe2);
            }

            private async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                if (await _repo.CreatePointerFileEntryIfNotExistsAsync(pfe))
                { 
                    if (pfe.IsDeleted)
                        _logger.LogInformation($"Deleted {pfe.RelativeName}");
                    else
                        _logger.LogInformation($"Added {pfe.RelativeName}");
                }


                //var pfes = await _repo.CurrentEntries();

                //if (!pfes.Contains(pfe, _pfeec))
                //{
                //    await _repo.InsertPointerFileEntry(pfe);

                //    if (pfe.IsDeleted)
                //        _logger.LogInformation($"Deleted {pfe.RelativeName}");
                //    else
                //        _logger.LogInformation($"Added {pfe.RelativeName}");
                //}
            }


            private PointerFileEntry CreatePointerFileEntry(PointerFile pf, DateTime version)
            {
                return new()
                {
                    ManifestHash = pf.Hash,
                    RelativeName = pf.RelativeName,
                    Version = version,
                    IsDeleted = false,
                    CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(), //TODO
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName).ToUniversalTime(),
                };
            }

            private PointerFileEntry CreatePointerFileEntry(PointerFileEntry pfe, DateTime version, bool isDeleted)
            {
                if (isDeleted)
                    return pfe with
                    {
                        Version = version,
                        IsDeleted = true,
                        CreationTimeUtc = null,
                        LastWriteTimeUtc = null
                    };
                else
                    throw new NotImplementedException();
            }

            //TODO KARL return values of method see before it returns?



            public async Task<IEnumerable<PointerFileEntry>> GetCurrentEntriesAsync(bool includeDeleted)
            {
                var pfes = await _repo.CurrentEntries();

                if (includeDeleted)
                    return pfes;
                else
                    return pfes.Where(pfe => !pfe.IsDeleted);
            }
        }

        public record PointerFileEntry
        {
            internal HashValue ManifestHash { get; init; }
            public string RelativeName { get; init; }
            public DateTime Version { get; init; }
            public bool IsDeleted { get; init; }
            public DateTime? CreationTimeUtc { get; init; }
            public DateTime? LastWriteTimeUtc { get; init; }
        }
    }
}
