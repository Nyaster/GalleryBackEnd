using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.UpdateUserUploadPermission;

public sealed record Command(int UserId, bool CanUploadImages) : IRequest<UploadPermissionDto>;
