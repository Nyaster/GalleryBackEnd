using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageBySearch;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<Command, PageableImagesDto>
{
    public async Task<PageableImagesDto> Handle(Command request, CancellationToken cancellationToken)
    {
        var normalized = request.Request with { Page = Math.Max(request.Request.Page, 1), PageSize = Math.Clamp(request.Request.PageSize, 1, 50) };
        if (normalized.Sort == ImageSort.Random)
        {
            var randomSeed = normalized.RandomSeed;
            if (randomSeed is null)
            {
                if (normalized.Page > 1)
                    throw new Base400BadRequestException("randomSeed is required when requesting page greater than 1 with sort=Random.");
                randomSeed = RandomSortSeed.Create();
            }

            if (!RandomSortSeed.TryGetValue(randomSeed, out _))
                throw new Base400BadRequestException("randomSeed must be a valid URL-safe random-sort seed.");

            normalized = normalized with { RandomSeed = randomSeed };
        }
        else
        {
            normalized = normalized with { RandomSeed = null };
        }

        var result = await repositories.AppImage.SearchAsync(normalized, cancellationToken);
        return new PageableImagesDto(normalized.Page, normalized.PageSize, result.Total, normalized.Sort,
            result.Images.Select(image => ImageDtoMapper.ToDto(image, currentUser)).ToArray(), normalized.RandomSeed);
    }
}
