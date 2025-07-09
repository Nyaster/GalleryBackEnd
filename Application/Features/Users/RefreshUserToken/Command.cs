using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.RefreshUserToken;

public record Command(AppRefreshhTokenResetDto RefreshRequest, string Token) : IRequest<JwtTokenResponse>;