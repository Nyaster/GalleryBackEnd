using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.GetUserUploadPermission;

public sealed record Command(int UserId) : IRequest<UploadPermissionDto>;
