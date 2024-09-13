using System.Text.Json.Serialization;

namespace Arius.Core.New.UnitTests.Fixtures;

public record TestCloudRepositoryOptions
{
    public string AccountName   { get; init; }
    
    public string AccountKey    { get; init; }
    
    [JsonIgnore]
    public string ContainerName { get; set; }

    public string Passphrase { get; init; }
}