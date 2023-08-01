using Arius.Core.Extensions;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PostSharp.Constraints;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public StateRepository States { get; init; }

    internal class StateRepository
    {
        internal const string StateDbsFolderName = "states";

        private readonly IAriusDbContextFactory dbContextFactory;


        [ComponentInternal(typeof(RepositoryBuilder))]
        public StateRepository(IAriusDbContextFactory dbContextFactory)
        {
            this.dbContextFactory = dbContextFactory;
        }

        internal AriusDbContext GetAriusDbContext() => dbContextFactory.GetContext();

        internal async Task CommitToBlobStorageAsync(DateTime versionUtc)
        {
            await dbContextFactory.SaveAsync(versionUtc);
        }
    }
}