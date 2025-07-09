using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetTagSuggestion;

public record Command(string Tag) : IRequest<List<TagsDto>>;