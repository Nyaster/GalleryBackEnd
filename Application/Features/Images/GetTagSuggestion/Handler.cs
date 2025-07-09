using Contracts;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetTagSuggestion;

public class Handler(IRepositoryManager repositoryManager)
    : IRequestHandler<Command, List<TagsDto>>
{
    public async Task<List<TagsDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        var tag = request.Tag;
        var tagsSuggestion = await repositoryManager.AppImage.GetTagsSuggestion(tag);
        var tagsDtos = tagsSuggestion.Select(x => new TagsDto(x.Name)).ToList();
        return tagsDtos;
    }
}