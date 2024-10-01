namespace Arius.Core.Domain;

public record AriusConfiguration
{
    public required DirectoryInfo LocalConfigRoot { get; set; }

    private DirectoryInfo StateDbRoot
        => LocalConfigRoot.CreateSubdirectory("StateDbs");

    public DirectoryInfo GetLocalStateDatabaseFolderForContainerName(string containerName)
        => StateDbRoot.CreateSubdirectory(containerName);
}