﻿using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

internal static class BlobBaseClientExtensions
{
    //public static async Task SetMetadataTagAsync(this BlobBaseClient c, string tag)
    //{
    //    await c.SetMetadataAsync(new Dictionary<string, string> { { tag, null } });
    //}

    //public static async Task<bool> HasMetadataTagAsync(this BlobBaseClient c, string tag)
    //{
    //    return (await c.GetPropertiesAsync()).Value.HasMetadataTagAsync(tag);
    //}

    //public static bool HasMetadataTagAsync(this BlobProperties c, string tag)
    //{
    //    return c.Metadata.ContainsKey(tag);
    //}

    ///// <summary>
    ///// Get the BlobProperties if the blob exists. Return null if it does not exist.
    ///// </summary>
    ///// <param name="c"></param>
    ///// <returns></returns>
    //public static async Task<BlobProperties> GetPropertiesOrDefaultAsync(this BlobBaseClient c)
    //{
    //    try
    //    {
    //        var p = await c.GetPropertiesAsync();
    //        return p.Value;
    //    }
    //    catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
    //    {
    //        return null;
    //    }
    //}
}
