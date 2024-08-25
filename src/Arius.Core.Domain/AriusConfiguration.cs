namespace Arius.Core.Domain;

public record AriusConfiguration
{
    public required DirectoryInfo LocalConfigRoot { get; set; }


    public DirectoryInfo StateDbRoot                              => LocalConfigRoot.CreateSubdirectory("StateDbs");
    public DirectoryInfo GetStateDbForRepositoryName(string name) => StateDbRoot.CreateSubdirectory(name);
}