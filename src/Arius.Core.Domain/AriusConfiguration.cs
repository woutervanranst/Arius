namespace Arius.Core.Domain;

public record AriusConfiguration
{
    public required DirectoryInfo LocalConfigRoot { get; set; }

    private DirectoryInfo StateDbRoot
        => LocalConfigRoot.CreateSubdirectory("StateDbs");

    public DirectoryInfo GetLocalStateDatabaseCacheDirectoryForContainerName(string containerName)
        => StateDbRoot.CreateSubdirectory(containerName);
}