using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public record GetTagsSuggestionQuery(string Tag) : IRequest<List<TagsDto>>;