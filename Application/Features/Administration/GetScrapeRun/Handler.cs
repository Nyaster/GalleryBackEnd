using Application.Features.Administration.StartScrape;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetScrapeRun;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, ScrapeRunDto>
{
    public async Task<ScrapeRunDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin))) throw new AppForbiddenException("Administrator access is required.");
        var run = await repositories.GetScrapeRunAsync(request.Id, false, cancellationToken)
            ?? throw new Base404ReturnException("Scrape run not found.");
        return StartScrape.Handler.ToDto(run);
    }
}
