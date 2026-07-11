using MediatR;
using Service.Contracts;

namespace Application.Features.Users.RefreshUserToken;

public sealed class Handler(IAuthenticationService authentication) : IRequestHandler<Command, AuthCommandResult>
{
    public async Task<AuthCommandResult> Handle(Command request, CancellationToken cancellationToken)
    {
        var result = await authentication.RefreshAsync(request.RefreshToken, cancellationToken);
        return new AuthCommandResult(result.Response, result.RefreshToken);
    }
}
