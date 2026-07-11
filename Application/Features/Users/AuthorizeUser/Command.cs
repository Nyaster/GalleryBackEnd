using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.AuthorizeUser;

public sealed record Command(AppLoginDto Request) : IRequest<AuthCommandResult>;
