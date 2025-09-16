namespace Arius.Core.Features;

public abstract record StorageAccountCommandProperties
{
    public required string AccountName    { get; init; }
    public required string AccountKey     { get; init; } // TODO investigate SecureString
    internal        bool   UseRetryPolicy { get; init; } = true;
}