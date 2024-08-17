using AutoMapper;
using GallerySiteBackend.Models;
using Shared.DataTransferObjects;

namespace GallerySiteBackend;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<AppImage, AppImageDTO>()
            .ForMember(opt => opt.UploadedBy, opt => opt.MapFrom(src => src.UploadedBy.Login))
            .ForMember(x => x.UrlToImage, x => x.MapFrom(src => $"api/images/{src.UploadedBy.Id}"));
    }
}