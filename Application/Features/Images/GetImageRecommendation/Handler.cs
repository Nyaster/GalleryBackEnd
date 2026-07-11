using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, List<AppImageDto>>
{
    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        var image = await repositories.AppImage.GetByIdAsync(request.Id, false, cancellationToken)
            ?? throw new Base404ReturnException("Image not found.");
        ImageAuthorization.EnsureReadable(image, currentUser);
        if (image.Embedding is null)
            throw new Base409ConflictException("Recommendations are not ready for this image.");
        var recommendations = await repositories.AppImage.GetRecommendationsAsync(image.Id, request.Limit, cancellationToken);
        return recommendations.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToList();
    }
}
