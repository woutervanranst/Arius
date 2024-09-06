using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.New.Commands.Archive;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Arius.Web;

public sealed class ArchiveCommandNotificationHandler<T> 
    : INotificationHandler<T> where T : ArchiveCommandNotification
{
    private readonly FileProcessingService s;

    public ArchiveCommandNotificationHandler(FileProcessingService s)
    {
        this.s = s;
    }

    public async Task Handle(T notification, CancellationToken cancellationToken)
    {
        var result = notification switch
        {
            FilePairFoundNotification found     => s.HandleFilePairFoundNotification(found, cancellationToken),
            FilePairHashingCompletedNotification hashed   => s.HandleFilePairHashedNotification(hashed, cancellationToken),
            ArchiveCommandDoneNotification done => s.HandleArchiveCommandDoneNotification(done, cancellationToken),
            _                                   => throw new InvalidOperationException("No event handler found for notification type")
        };

        await result;
    }
}

public sealed class FileProcessingHub : Hub<IFileProcessingClient>
{
    private readonly FileProcessingService s;

    public FileProcessingHub(FileProcessingService s)
    {
        this.s = s;
    }

    public async Task SendCurrentProgress()
    {
        // Get the current progress from the FileProcessingService
        var currentFiles = s.GetCurrentFiles();

        // Send the current progress to the caller
        await Clients.Caller.FileFound(currentFiles.Count, currentFiles.Where(f => f.Value != "DONE").Select(f => f.Key.RelativeName).ToArray());

        foreach (var file in currentFiles.Where(f => f.Value == "HASHED"))
        {
            await Clients.Caller.FileProcessed(file.Key.RelativeName);
        }

        // If all files are processed, signal that processing is completed
        if (currentFiles.Values.All(status => status == "DONE"))
        {
            await Clients.Caller.FileProcessingCompleted();
        }
    }
}

public sealed class FileProcessingService
{
    private readonly IHubContext<FileProcessingHub, IFileProcessingClient> hubContext;

    private readonly ConcurrentDictionary<FilePair, string> files = new();

    public FileProcessingService(IHubContext<FileProcessingHub, IFileProcessingClient> hubContext)
    {
        this.hubContext = hubContext;
    }

    public ConcurrentDictionary<FilePair, string> GetCurrentFiles()
    {
        return files;
    }

    public Task HandleFilePairFoundNotification(FilePairFoundNotification notification, CancellationToken cancellationToken)
    {
        files.TryAdd(notification.FilePair, "FOUND");

        hubContext.Clients.All.FileFound(files.Count,  files.Where(kvp => kvp.Value != "DONE").Select(kvp => kvp.Key.RelativeName).ToArray());

        return Task.CompletedTask;
    }

    public Task HandleFilePairHashedNotification(FilePairHashingCompletedNotification notification, CancellationToken cancellationToken)
    {
        files[notification.FilePairWithHash] = "HASHED";
        //files.TryUpdate(notification.FilePairWithHash, "HASHED", "FOUND");

        //hubContext.Clients.All.FileProcessed()

        return Task.CompletedTask;
    }

    public Task HandleArchiveCommandDoneNotification(ArchiveCommandDoneNotification notification, CancellationToken cancellationToken)
    {
        //throw new NotImplementedException();

        hubContext.Clients.All.FileProcessingCompleted();

        return Task.CompletedTask;
    }
}

public interface IFileProcessingClient
{
    Task FileFound(int fileCounter, IReadOnlyCollection<string> recentFiles);
    Task FileProcessed(string fileName);
    Task FileProcessingCompleted();
}

