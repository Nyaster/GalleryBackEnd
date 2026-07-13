using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.StartScrape;

public sealed record Command(ScrapeMode Mode, int? MaxImages = null) : IRequest<ScrapeRunDto>;
