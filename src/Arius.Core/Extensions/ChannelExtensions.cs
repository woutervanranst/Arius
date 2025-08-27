using System.Threading.Channels;

namespace Arius.Core.Extensions;

public static class ChannelExtensions
{
    public static Channel<T> CreateBounded<T>(int capacity, bool singleWriter, bool singleReader)
        => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleWriter = singleWriter,
            SingleReader = singleReader
        });
}