using Arius.Core.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal class PointerFileEntryRepository : AppendOnlyRepository<PointerFileEntry>
    {
        public PointerFileEntryRepository(ILogger<PointerFileEntryRepository> logger, IOptions options, BlobContainerClient container)
            : base(logger, options, container)
        {

        }

    }
}
