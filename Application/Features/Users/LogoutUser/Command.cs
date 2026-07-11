using MediatR;

namespace Application.Features.Users.LogoutUser;

public sealed record Command(string RefreshToken) : IRequest;
