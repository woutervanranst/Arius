namespace Arius.Core.Commands
{
    internal interface IAzCopyAppSettings
    {
        int BatchCount { get; init; }
        long BatchSize { get; init; }
    }
}