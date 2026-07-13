using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.CommentRestrictions;

public sealed record GetCommentRestrictionCommand(int UserId) : IRequest<CommentRestrictionDto>;
public sealed record SetCommentRestrictionCommand(int UserId, CommentRestrictionUpdateDto Request) : IRequest<CommentRestrictionDto>;
public sealed record DeleteCommentRestrictionCommand(int UserId) : IRequest;
