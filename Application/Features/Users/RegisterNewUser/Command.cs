using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.RegisterNewUser;

public sealed record Command(CreateUserDto Request) : IRequest<AuthCommandResult>;
