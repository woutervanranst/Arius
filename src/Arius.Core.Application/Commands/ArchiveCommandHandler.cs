using MediatR;

namespace Arius.Core.Application.Commands;
public class ArchiveCommandHandler : IRequestHandler<ArchiveCommand>
{

    //public Task<Unit> Handle(ArchiveCommand request, CancellationToken cancellationToken)
    //{
    //    // Add your archiving logic here
    //    //Console.WriteLine($"Archiving file: {request.FilePath}");
    //    return Task.FromResult(Unit.Value);
    //}
    public Task Handle(ArchiveCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}