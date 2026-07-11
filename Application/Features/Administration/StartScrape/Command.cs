using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.StartScrape;

public sealed record Command(ScrapeMode Mode) : IRequest<ScrapeRunDto>;
