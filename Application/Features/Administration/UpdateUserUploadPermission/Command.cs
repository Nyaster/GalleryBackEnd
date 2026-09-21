using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Administration.UpdateUserUploadPermission;

public sealed record Command(int UserId, bool CanUploadImages, bool? UploadsBlocked = null)
    : IRequest<UploadPermissionDto>;