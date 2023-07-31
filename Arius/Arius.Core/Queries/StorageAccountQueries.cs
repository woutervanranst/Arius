﻿using System;
using System.Collections.Generic;
using System.Linq;
using Arius.Core.Facade;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries;

internal class StorageAccountQueries
{
    private readonly ILogger<StorageAccountQueries> logger;
    private readonly IStorageAccountOptions          options;
    private readonly BlobServiceClient              blobServiceClient;

    public StorageAccountQueries(ILogger<StorageAccountQueries> logger, IStorageAccountOptions options)
    {
        this.logger  = logger;
        this.options = options;

        this.blobServiceClient = new BlobServiceClient(new Uri($"https://{options.AccountName}.blob.core.windows.net/"), new StorageSharedKeyCredential(options.AccountName, options.AccountKey));
    }

    public IAsyncEnumerable<string> GetContainerNamesAsync()
    {
        return blobServiceClient.GetBlobContainersAsync().Select(bci => bci.Name);
    }
}