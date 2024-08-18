namespace Arius.Web.Core;

public class BackupConfiguration
{
    public int    Id            { get; set; }
    public string Path          { get; set; }
    public string AccountName   { get; set; }
    public string AccountKey    { get; set; } // Encrypted
    public string Passphrase    { get; set; } // Encrypted
    public string ContainerName { get; set; }
    public bool   RemoveLocal   { get; set; }
    public string Tier          { get; set; } // Hot, Cool, Cold, Archive
    public bool   Dedup         { get; set; }
    public bool   FastHash      { get; set; }
}