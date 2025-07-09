using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Users.AuthorizeUser;

public record Command(AppLoginDto AppLoginDto) : IRequest<JwtTokenResponse>;