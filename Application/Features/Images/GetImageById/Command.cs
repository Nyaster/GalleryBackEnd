using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageById;

public record Command(int Id) : IRequest<AppImageDto>;