using AutoMapper;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using MediatR;
using Shared.DataTransferObjects;

namespace Application.Features.Images.Queries;

public class GetImageByIdHandler(IRepositoryManager repositoryManager, IMapper mapper) : IRequestHandler<GetImageByIdQuery, AppImageDto>
{
    public async Task<AppImageDto> Handle(GetImageByIdQuery request, CancellationToken cancellationToken)
    {
        var id = request.Id;
        var byId = await repositoryManager.AppImage.GetById(id);
        if (byId == null)
        {
            throw new Base404ReturnException($"Image with id:{id} not found");
        }
        var appImageDto = mapper.Map<AppImage, AppImageDto>(byId);
        return appImageDto;
    }
}