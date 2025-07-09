using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.RegisterNewUser;

public record Command(CreateUserDto RegistrationRequest) : IRequest<JwtTokenResponse>;