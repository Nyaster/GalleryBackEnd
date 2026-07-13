using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Comments;

public sealed record GetCommentsCommand(int ImageId, int Page, int PageSize) : IRequest<PageableCommentsDto>;
public sealed record CreateCommentCommand(int ImageId, CreateCommentDto Request) : IRequest<CommentDto>;
public sealed record DeleteCommentCommand(int CommentId, bool IsStaffDeletion) : IRequest;
