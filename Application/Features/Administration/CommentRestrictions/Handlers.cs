using Application.Helpers;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.CommentRestrictions;

public sealed class GetCommentRestrictionHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<GetCommentRestrictionCommand, CommentRestrictionDto>
{
    public async Task<CommentRestrictionDto> Handle(GetCommentRestrictionCommand request, CancellationToken cancellationToken)
    {
        EnsureStaff(currentUser);
        var user = await repositories.AppUser.GetByIdAsync(request.UserId, false, cancellationToken) ?? throw new Base404ReturnException("User not found.");
        var restriction = await repositories.Interactions.GetCommentRestrictionAsync(user.Id, false, cancellationToken);
        var active = restriction is not null && (restriction.RestrictedUntilUtc is null || restriction.RestrictedUntilUtc > clock.GetUtcNow());
        return new CommentRestrictionDto(user.Id, user.Login, active, restriction?.RestrictedUntilUtc);
    }

    internal static void EnsureStaff(IUserContext user) { if (!ImageAuthorization.IsStaff(user)) throw new AppForbiddenException("Staff access is required."); }
}

public sealed class SetCommentRestrictionHandler(IRepositoryManager repositories, IUserContext currentUser, TimeProvider clock) : IRequestHandler<SetCommentRestrictionCommand, CommentRestrictionDto>
{
    public async Task<CommentRestrictionDto> Handle(SetCommentRestrictionCommand request, CancellationToken cancellationToken)
    {
        GetCommentRestrictionHandler.EnsureStaff(currentUser);
        var user = await repositories.AppUser.GetByIdAsync(request.UserId, false, cancellationToken) ?? throw new Base404ReturnException("User not found.");
        var now = clock.GetUtcNow();
        var requestedUntil = request.Request.RestrictedUntilUtc;
        var until = requestedUntil?.ToUniversalTime();
        if (requestedUntil is not null && (requestedUntil.Value.Offset != TimeSpan.Zero || requestedUntil.Value <= now))
            throw new Base400BadRequestException("restrictedUntilUtc must be a future UTC timestamp.");
        var restriction = await repositories.Interactions.GetCommentRestrictionAsync(user.Id, true, cancellationToken);
        if (restriction is null)
        {
            restriction = new CommentRestriction { UserId = user.Id, RestrictedByUserId = currentUser.UserId!.Value, RestrictedUntilUtc = until, CreatedAtUtc = now, UpdatedAtUtc = now };
            await repositories.Interactions.AddCommentRestrictionAsync(restriction, cancellationToken);
        }
        else { restriction.RestrictedUntilUtc = until; restriction.RestrictedByUserId = currentUser.UserId!.Value; restriction.UpdatedAtUtc = now; }
        await repositories.SaveAsync(cancellationToken);
        return new CommentRestrictionDto(user.Id, user.Login, true, restriction.RestrictedUntilUtc);
    }
}

public sealed class DeleteCommentRestrictionHandler(IRepositoryManager repositories, IUserContext currentUser) : IRequestHandler<DeleteCommentRestrictionCommand>
{
    public async Task Handle(DeleteCommentRestrictionCommand request, CancellationToken cancellationToken)
    {
        GetCommentRestrictionHandler.EnsureStaff(currentUser);
        _ = await repositories.AppUser.GetByIdAsync(request.UserId, false, cancellationToken) ?? throw new Base404ReturnException("User not found.");
        var restriction = await repositories.Interactions.GetCommentRestrictionAsync(request.UserId, true, cancellationToken);
        if (restriction is not null) { repositories.Interactions.RemoveCommentRestriction(restriction); await repositories.SaveAsync(cancellationToken); }
    }
}
