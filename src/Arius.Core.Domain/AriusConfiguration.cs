using System.IO.Abstractions;

namespace Arius.Core.Domain;

public record AriusConfiguration
{
    public required IDirectoryInfo LocalConfigRoot { get; set; }


    private IDirectoryInfo StateDbRoot                                                  => LocalConfigRoot.CreateSubdirectory("StateDbs");
    public  IDirectoryInfo GetLocalStateDbFolderForRepositoryName(string containerName) => StateDbRoot.CreateSubdirectory(containerName);
}