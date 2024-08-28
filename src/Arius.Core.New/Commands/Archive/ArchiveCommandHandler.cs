using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage.FileSystem;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Commands.Archive;

internal class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{
    private readonly IFileSystem                    fileSystem;
    private readonly IStateDbRepositoryFactory      stateDbRepositoryFactory;
    private readonly ILogger<ArchiveCommandHandler> logger;

    public ArchiveCommandHandler(
        IFileSystem fileSystem,
        IStateDbRepositoryFactory stateDbRepositoryFactory, 
        ILogger<ArchiveCommandHandler> logger)
    {
        this.fileSystem               = fileSystem;
        this.stateDbRepositoryFactory = stateDbRepositoryFactory;
        this.logger                   = logger;
    }

    public async Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        await new ArchiveCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        // Download latest state database
        var stateDbRepository = await stateDbRepositoryFactory.CreateAsync(request.Repository);

        //Index the request.LocalRoot

        //var downloadStateDbCommand = new DownloadStateDbCommand
        //{
        //    Repository = request.Repository,
        //    LocalPath  = request.LocalRoot.FullName
        //};

        //await mediator.Send(downloadStateDbCommand);



        throw new NotImplementedException();

    }

    internal static IEnumerable<(PointerFile? pf, BinaryFile? bf)> IndexFiles(IFileSystem fileSystem, DirectoryInfo root)
    {
        var seenFiles        = new HashSet<string>();
        var currentDirectory = root.FullName;

        foreach (var file in fileSystem.EnumerateFiles(root))
        {
            // Check if the directory has changed, if so clear the seenFiles HashSet
            if (!string.Equals(currentDirectory, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                seenFiles.Clear();
                currentDirectory = file.Path; // Update the current directory to the file's directory
            }

            if (!seenFiles.Add(file.BinaryFileFullName))
                continue;

            if (file.IsPointerFile)
            {
                // this is a PointerFile
                var pf = file.GetPointerFile(root);

                if (pf.GetBinaryFile(root) is { Exists: true } bf)
                {
                    // BinaryFile exists too
                    yield return (pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return (pf, null);
                }
            }
            else
            {
                // this is a BinaryFile
                var bf = file.GetBinaryFile(root);

                if (bf.GetPointerFile(root) is { Exists : true } pf)
                {
                    // PointerFile exists too
                    yield return (pf, bf);
                }
                else
                {
                    // BinaryFile does not exist
                    yield return (null, bf);
                }
            }
        }
    }
}