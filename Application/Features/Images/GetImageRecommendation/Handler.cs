using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableRecommendationsDto>
{
    public async Task<PageableRecommendationsDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.Id, false, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        if (image.Embedding is null)
            throw new Base409ConflictException("Recommendations are not ready for this image.");
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var result = await repositories.AppImage.GetRecommendationsAsync(image.Id, page, pageSize, cancellationToken, request.AiUsage);
        return new PageableRecommendationsDto(page, pageSize, result.Total,
            result.Images.Select(recommendation => ImageDtoMapper.ToDto(recommendation, currentUser)).ToList());
    }
}
