using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Users;

public sealed record UpdateContentPreferencesCommand(bool DisplayAllContent) : IRequest<AppUserDto>;

public sealed class UpdateContentPreferencesHandler(IRepositoryManager repositories, IUserContext currentUser, AccountAccessDto access)
    : IRequestHandler<UpdateContentPreferencesCommand, AppUserDto>
{
    public async Task<AppUserDto> Handle(UpdateContentPreferencesCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var userId = currentUser.UserId ?? throw new AppUserUnauthorizedException("Authentication is required.");
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await repositories.AppUser.LockByIdAsync(userId, cancellationToken)
            ?? throw new AppUserUnauthorizedException("Authentication is required.");
        user.DisplayAllContent = request.DisplayAllContent;
        await repositories.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AppUserDto.FromUser(user, access);
    }
}
