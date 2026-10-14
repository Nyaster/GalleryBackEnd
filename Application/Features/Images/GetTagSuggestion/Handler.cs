using Contracts;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetTagSuggestion;

public sealed class Handler(IRepositoryManager repositories, IUserContext currentUser)
    : IRequestHandler<Command, List<TagsDto>>
{
    public async Task<List<TagsDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        var query = request.Query.Trim().ToLowerInvariant();
        if (query.Length is < 1 or > 64) return [];
        var tags = await (currentUser.UserId is null
            ? repositories.AppImage.GetGuestTagSuggestionsAsync(query, request.Limit, cancellationToken)
            : repositories.AppImage.GetTagSuggestionsAsync(query, request.Limit, cancellationToken));
        return tags.Select(tag => new TagsDto(tag.Name)).ToList();
    }
}
