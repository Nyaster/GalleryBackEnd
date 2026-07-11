using Contracts;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetTagSuggestion;

public sealed class Handler(IRepositoryManager repositories) : IRequestHandler<Command, List<TagsDto>>
{
    public async Task<List<TagsDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        var query = request.Query.Trim().ToLowerInvariant();
        if (query.Length is < 1 or > 64) return [];
        var tags = await repositories.AppImage.GetTagSuggestionsAsync(query, request.Limit, cancellationToken);
        return tags.Select(tag => new TagsDto(tag.Name)).ToList();
    }
}
