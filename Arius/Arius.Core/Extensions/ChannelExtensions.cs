using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Extensions
{
    internal static class ChannelExtensions
    {
        // Adaptation from https://github.com/n-ski/ParallelExtensionsExtras.NetFxStandard/blob/master/src/Extensions/BlockingCollectionExtensions.cs

        public static async Task AddFromEnumerable<T>(this ChannelWriter<T> target, IEnumerable<T> source, bool completeAddingWhenDone)
        {
            try { foreach (var item in source) await target.WriteAsync(item); }
            finally { if (completeAddingWhenDone) target.Complete(); }
        }
    }
}
