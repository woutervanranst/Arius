using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

internal static class BlobBaseClientExtensions
{
    public static async Task SetMetadataTagAsync(this BlobBaseClient c, string tag)
    {
        await c.SetMetadataAsync(new Dictionary<string, string> { { tag, null } });
    }

    public static async Task<bool> HasMetadataTagAsync(this BlobBaseClient c, string tag)
    {
        return (await c.GetPropertiesAsync()).Value.HasMetadataTagAsync(tag);
    }

    public static bool HasMetadataTagAsync(this BlobProperties c, string tag)
    {
        return c.Metadata.ContainsKey(tag);
    }
}
