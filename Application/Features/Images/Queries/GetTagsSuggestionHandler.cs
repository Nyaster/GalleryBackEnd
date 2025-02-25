using Contracts;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public class GetTagsSuggestionHandler(IRepositoryManager repositoryManager)
    : IRequestHandler<GetTagsSuggestionQuery, List<TagsDto>>
{
    public async Task<List<TagsDto>> Handle(GetTagsSuggestionQuery request, CancellationToken cancellationToken)
    {
        var tag = request.Tag;
        var tagsSuggestion = await repositoryManager.AppImage.GetTagsSuggestion(tag);
        var tagsDtos = tagsSuggestion.Select(x => new TagsDto(x.Name)).ToList();
        return tagsDtos;
    }
}