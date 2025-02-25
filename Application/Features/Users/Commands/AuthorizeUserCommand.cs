using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.Commands;

public record AuthorizeUserCommand(AppLoginDto AppLoginDto) : IRequest<JwtTokenResponse>;