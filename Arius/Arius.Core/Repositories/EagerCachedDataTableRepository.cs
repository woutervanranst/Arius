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
using Arius.Core.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using Murmur;

namespace Arius.Core.Repositories
{
    internal class EagerCachedDataTableRepository<TDto, T> where TDto : TableEntity, new()
    {
        public EagerCachedDataTableRepository(ILogger logger, 
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
                allRows = Task.Run(() =>
                {
                    logger.LogDebug($"Getting all rows in {tableName}...");


                    // get all rows - we're getting them anyway as GroupBy is not natively supported
                    var r = table
                        .CreateQuery<TDto>()
                        .AsEnumerable()
                        .Select(r => fromDto(r))
                        .ToList();

                    logger.LogDebug($"Getting all rows in {tableName}... Done. Fetched {r.Count} rows");


                    return r;
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Error while initializing {nameof(EagerCachedDataTableRepository<TDto, T>)} for table {tableName}");
                throw;
            }
        }

        private readonly ILogger logger;
        private readonly Func<T, TDto> toDto;
        private readonly Func<TDto, T> fromDto;
        private readonly CloudTable table;
        private readonly Task<List<T>> allRows;

        public async Task<IReadOnlyCollection<T>> GetAllAsync() => await allRows;

        public async Task AddAsync(T item)
        {
            var dto = toDto(item);
            var op = TableOperation.Insert(dto);
            await table.ExecuteAsync(op);

            (await allRows).Add(item);
        }
    }
}
