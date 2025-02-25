using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public record GetImageByIdQuery(int Id) : IRequest<AppImageDto>;