using MediatR;
using Microsoft.AspNetCore.Identity.Data;
using Shared.DataTransferObjects;

namespace Application.Features.Users.Commands;

public record RefreshTokenCommand(AppRefreshhTokenResetDto RefreshRequest, string Token) : IRequest<JwtTokenResponse>;