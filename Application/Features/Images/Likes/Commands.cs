using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Likes;

public sealed record SetLikeCommand(int ImageId, bool IsLiked) : IRequest<LikeSummaryDto>;
