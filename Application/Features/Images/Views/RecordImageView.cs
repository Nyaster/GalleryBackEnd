using Application.Helpers;
using Contracts;
using MediatR;
using Service.Contracts;

namespace Application.Features.Images.Views;

public sealed record RecordImageView(int ImageId) : IRequest;

public sealed class RecordImageViewHandler(IRepositoryManager repositories, IUserContext currentUser,
    IImageViewRecorder recorder) : IRequestHandler<RecordImageView>
{
    public async Task Handle(RecordImageView request, CancellationToken cancellationToken)
    {
        var image = await ImageAuthorization.GetReadableMetadataAsync(repositories.AppImage, request.ImageId,
            currentUser, cancellationToken);
        if (!ImageAuthorization.IsOwner(image.UploadedById, currentUser))
            recorder.Record(image.Id, currentUser.UserId);
    }
}
