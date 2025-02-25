using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.Commands;

public record RegisterUserCommand(CreateUserDto RegistrationRequest) : IRequest<JwtTokenResponse>;