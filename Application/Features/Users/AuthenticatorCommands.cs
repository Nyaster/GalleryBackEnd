using Contracts;
using Entities.Exceptions;
using MediatR;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Application.Features.Users;

public sealed record SetupAuthenticatorCommand(AuthenticatorSetupDto Request) : IRequest<AuthenticatorSetupResponse>;

public sealed record ConfirmAuthenticatorCommand(AuthenticatorConfirmDto Request)
    : IRequest<AuthenticatorBackupCodesResponse>;

public sealed record RegenerateBackupCodesCommand(RegenerateBackupCodesDto Request)
    : IRequest<AuthenticatorBackupCodesResponse>;

public sealed record RemoveAuthenticatorCommand(AuthenticatorRemoveDto Request) : IRequest;

public sealed record RecoverAccountCommand(RecoverAccountDto Request) : IRequest;

public sealed record GetCurrentUserQuery : IRequest<AppUserDto>;

public sealed class AuthenticatorCommandHandler(IAuthenticatorService authenticator, IUserContext currentUser)
    : IRequestHandler<SetupAuthenticatorCommand, AuthenticatorSetupResponse>,
        IRequestHandler<ConfirmAuthenticatorCommand, AuthenticatorBackupCodesResponse>,
        IRequestHandler<RegenerateBackupCodesCommand, AuthenticatorBackupCodesResponse>,
        IRequestHandler<RemoveAuthenticatorCommand>,
        IRequestHandler<RecoverAccountCommand>
{
    public Task<AuthenticatorSetupResponse> Handle(SetupAuthenticatorCommand request,
        CancellationToken cancellationToken)
        => authenticator.SetupAsync(RequireUserId(), request.Request, cancellationToken);

    public Task<AuthenticatorBackupCodesResponse> Handle(ConfirmAuthenticatorCommand request,
        CancellationToken cancellationToken)
        => authenticator.ConfirmAsync(RequireUserId(), request.Request, cancellationToken);

    public Task<AuthenticatorBackupCodesResponse> Handle(RegenerateBackupCodesCommand request,
        CancellationToken cancellationToken)
        => authenticator.RegenerateBackupCodesAsync(RequireUserId(), request.Request, cancellationToken);

    public Task Handle(RemoveAuthenticatorCommand request, CancellationToken cancellationToken)
        => authenticator.RemoveAsync(RequireUserId(), request.Request, cancellationToken);

    public Task Handle(RecoverAccountCommand request, CancellationToken cancellationToken)
        => authenticator.RecoverAsync(request.Request, cancellationToken);

    private int RequireUserId()
    {
        currentUser.RequireAuthenticated();
        return currentUser.UserId ?? throw new AppUserUnauthorizedException("Authentication is required.");
    }
}

public sealed class GetCurrentUserHandler(IRepositoryManager repositories, IUserContext currentUser)
    : IRequestHandler<GetCurrentUserQuery, AppUserDto>
{
    public async Task<AppUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireAuthenticated();
        var user = currentUser.UserId is { } id
            ? await repositories.AppUser.GetByIdAsync(id, false, cancellationToken)
            : null;
        return user is not null
            ? AppUserDto.FromUser(user)
            : throw new AppUserUnauthorizedException("Authentication is required.");
    }
}