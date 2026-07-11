using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetScrapeRun;

public sealed record Command(Guid Id) : IRequest<ScrapeRunDto>;
