using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetTagSuggestion;

public sealed record Command(string Query, int Limit = 20) : IRequest<List<TagsDto>>;
