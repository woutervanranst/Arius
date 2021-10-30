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
using Azure.Data.Tables;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal class EagerCachedConcurrentDataTableRepository<TDto, T> where TDto : class, ITableEntity, new()
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

            var tsc = new TableServiceClient(connectionString);
            table = tsc.GetTableClient(tableName);

            var r = table.CreateIfNotExists();
            if (r is not null)
                logger.LogInformation($"Created {tableName} table");

            //Asynchronously download all PointerFileEntryDtos
            allRowsTask = Task.Run(() =>
            {
                logger.LogDebug($"Getting all rows for {typeof(TDto).Name} in {tableName}...");

                // get all rows - we're getting them anyway as GroupBy is not natively supported
                var ts = table.Query<TDto>()
                    .AsParallel()
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
    private readonly TableClient table;
    private readonly Task<ConcurrentHashSet<T>> allRowsTask;

    public async Task<IReadOnlyCollection<T>> GetAllAsync() => await allRowsTask;

    public async Task Add(T item)
    {
        var allRows = await allRowsTask;
        allRows.Add(item);

        var dto = toDto(item);
        await table.AddEntityAsync(dto);
    }
}