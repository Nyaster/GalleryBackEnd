using MediatR;
using Service.Contracts;

namespace Application.Features.Users.LogoutUser;

public sealed class Handler(IAuthenticationService authentication) : IRequestHandler<Command>
{
    public Task Handle(Command request, CancellationToken cancellationToken)
        => authentication.LogoutAsync(request.RefreshToken, cancellationToken);
}
