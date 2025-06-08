using MediatR;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands;

internal class RestoreCommandHandler : IRequestHandler<RestoreCommand>
{
    private readonly ILogger<RestoreCommandHandler> logger;

    public RestoreCommandHandler(ILogger<RestoreCommandHandler> logger)
    {
        this.logger = logger;
    }

    public Task Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
