using MediatR;

namespace Arius.Core.Application.Commands;

public record ArchiveCommand : IRequest<Unit>
{
    public string FilePath { get; init; }
}