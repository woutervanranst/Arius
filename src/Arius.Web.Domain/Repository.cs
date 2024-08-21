namespace Arius.Web.Domain;

public class Repository
{
    public int         Id               { get; set; }
    public int         StorageAccountId { get; set; } // Foreign key
    public string      LocalPath        { get; set; }
    public string      ContainerName    { get; set; }
    public string      Passphrase       { get; set; } // Encrypted
    public bool        RemoveLocal      { get; set; }
    public StorageTier Tier             { get; set; }
    public bool        Dedup            { get; set; }
    public bool        FastHash         { get; set; }

    // Navigation property
    public StorageAccount StorageAccount { get; set; }
}