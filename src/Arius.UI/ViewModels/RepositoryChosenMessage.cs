using System.IO;
using Arius.Core.Facade;

namespace Arius.UI.ViewModels;

public class RepositoryChosenMessage
{
    public RepositoryChosenMessage(DirectoryInfo root, RepositoryFacade repository)
    {
        LocalDirectory   = root;
        ChosenRepository = repository;
    }

    public DirectoryInfo    LocalDirectory   { get; }
    public RepositoryFacade ChosenRepository { get; }
}