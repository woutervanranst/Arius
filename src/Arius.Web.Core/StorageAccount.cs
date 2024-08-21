namespace Arius.Web.Core;

public class StorageAccount
{
    public int    Id          { get; set; }
    public string AccountName { get; set; }
    public string AccountKey  { get; set; } // Encrypted

    // Navigation property
    public ICollection<Repository> Repositories { get; set; }
}