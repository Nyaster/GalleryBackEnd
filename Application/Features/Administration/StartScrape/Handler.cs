using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Microsoft.Extensions.Options;
using Service;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.StartScrape;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock, IOptions<ParserSettings> parserSettings)
    : IRequestHandler<Command, ScrapeRunDto>
{
    public async Task<ScrapeRunDto> Handle(Command request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(nameof(AppUserRole.Admin))) throw new AppForbiddenException("Administrator access is required.");
        if (!parserSettings.Value.Enabled) throw new Base400BadRequestException("Scraping is disabled by configuration.");
        var maximumImagesPerRun = parserSettings.Value.MaximumImagesPerRun;
        if (maximumImagesPerRun is < 1 or > ParserSettings.AbsoluteMaximumImagesPerRun)
            throw new InvalidOperationException($"ParserSettings:MaximumImagesPerRun must be between 1 and {ParserSettings.AbsoluteMaximumImagesPerRun}.");
        if (request.MaxImages is int requestedImages && (requestedImages < 1 || requestedImages > maximumImagesPerRun))
            throw new Base400BadRequestException($"maxImages must be between 1 and {maximumImagesPerRun}.");

        var maxImages = request.MaxImages ?? parserSettings.Value.DefaultImagesPerRun;
        if (maxImages < 1 || maxImages > maximumImagesPerRun)
            throw new InvalidOperationException($"ParserSettings:DefaultImagesPerRun must be between 1 and {maximumImagesPerRun}.");

        var run = new ScrapeRun
        {
            Id = Guid.NewGuid(), Mode = request.Mode, Status = BackgroundJobStatus.Queued,
            CreatedAtUtc = clock.GetUtcNow(), MaxImages = maxImages
        };
        await repositories.AddScrapeRunAsync(run, cancellationToken);
        await repositories.SaveAsync(cancellationToken);
        return ToDto(run);
    }

    internal static ScrapeRunDto ToDto(ScrapeRun run) => new(run.Id, run.Mode, run.Status, run.CreatedAtUtc, run.StartedAtUtc,
        run.CompletedAtUtc, run.ImagesDiscovered, run.ImagesImported, run.Error, run.CompletedWithErrors, run.FailedItems,
        run.MaxImages, run.TotalPages, run.ScannedPages, run.EligibleCandidates, run.PlannedDownloads,
        run.ProcessedDownloads, run.LastProgressAtUtc);
}
