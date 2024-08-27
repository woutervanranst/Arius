using Arius.Core.Domain.Storage;

namespace Arius.Core.Domain;

public record AriusConfiguration
{
    public required DirectoryInfo LocalConfigRoot { get; set; }


    private DirectoryInfo StateDbRoot                                                  => LocalConfigRoot.CreateSubdirectory("StateDbs");
    public  DirectoryInfo GetLocalStateDbFolderForRepository(RepositoryOptions repositoryOptions) => StateDbRoot.CreateSubdirectory(repositoryOptions.ContainerName);
}