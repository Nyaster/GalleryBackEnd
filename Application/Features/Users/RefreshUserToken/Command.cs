using MediatR;

namespace Application.Features.Users.RefreshUserToken;

public sealed record Command(string RefreshToken) : IRequest<AuthCommandResult>;
