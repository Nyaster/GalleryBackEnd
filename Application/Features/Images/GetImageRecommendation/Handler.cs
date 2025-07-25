using AutoMapper;
using Contracts;
using Entities.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace Application.Features.Images.GetImageRecommendation;

public class Handler(IRepositoryManager repositoryManager, IMapper mapper) : IRequestHandler<Command, List<AppImageDto>>
{
    public readonly IRepositoryManager RepositoryManager = repositoryManager;

    public async Task<List<AppImageDto>> Handle(Command request, CancellationToken cancellationToken)
    {
        var requestId = request.id;
        var image = await RepositoryManager.AppImage.GetById(requestId);
        var listAsync = await RepositoryManager.AppImage.FindAll(false)
            .OrderBy(x => x.Embedding!.L2Distance(image!.Embedding)).Take(20).ToListAsync(cancellationToken: cancellationToken);
        return listAsync.Select(mapper.Map<AppImage, AppImageDto>).ToList();
    }
}