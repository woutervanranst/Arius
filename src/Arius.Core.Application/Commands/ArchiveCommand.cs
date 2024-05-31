using MediatR;

namespace Arius.Core.Application.Commands;

public record ArchiveCommand : IRequest
{
    public string FilePath { get; init; }
}