using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal class EagerCachedConcurrentDataTableRepository<TDto, T> where TDto : TableEntity, new()
    {
        public EagerCachedConcurrentDataTableRepository(ILogger logger, 
            string accountName, string accountKey, string tableName,
            Func<T, TDto> toDto,
            Func<TDto, T> fromDto)
        {
            this.logger = logger;
            this.toDto = toDto;
            this.fromDto = fromDto;

            try
            {
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
                var csa = CloudStorageAccount.Parse(connectionString);
                var tc = csa.CreateCloudTableClient();
                table = tc.GetTableReference(tableName);

                var r = table.CreateIfNotExists();
                if (r)
                    logger.LogInformation($"Created {tableName} table");

                //Asynchronously download all PointerFileEntryDtos
                allRowsTask = Task.Run(() =>
                {
                    logger.LogDebug($"Getting all rows for {typeof(TDto).Name} in {tableName}...");

                    // get all rows - we're getting them anyway as GroupBy is not natively supported
                    var ts = table
                        .CreateQuery<TDto>()
                        .AsEnumerable()
                        .Select(r => fromDto(r));

                    var r = new ConcurrentHashSet<T>(ts);

                    logger.LogDebug($"Getting all rows in {tableName}... Done. Fetched {r.Count} rows");

                    return r;
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error while initializing {nameof(EagerCachedConcurrentDataTableRepository<TDto, T>)} for table {tableName}");
                throw;
            }
        }

        private readonly ILogger logger;
        private readonly Func<T, TDto> toDto;
        private readonly Func<TDto, T> fromDto;
        private readonly CloudTable table;
        private readonly Task<ConcurrentHashSet<T>> allRowsTask;

        public async Task<IReadOnlyCollection<T>> GetAllAsync() => (IReadOnlyCollection<T>)(await allRowsTask).Values;

        public async Task Add(T item)
        {
            var allRows = await allRowsTask;
            allRows.Add(item);

            var dto = toDto(item);
            var op = TableOperation.Insert(dto);
            await table.ExecuteAsync(op);
        }
    }
}
