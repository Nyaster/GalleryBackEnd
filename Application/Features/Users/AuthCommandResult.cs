using Shared.DataTransferObjects;

namespace Application.Features.Users;

public sealed record AuthCommandResult(JwtTokenResponse Response, string RefreshToken);
