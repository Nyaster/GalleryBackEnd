using AutoMapper;
using Entities.Models;
using GallerySiteBackend.Models;
using Shared.DataTransferObjects;

namespace GallerySiteBackend;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<AppImage, AppImageDto>()
            .ForCtorParam(nameof(AppImageDto.Id), opt => opt.MapFrom(src => src.Id))
            .ForCtorParam(nameof(AppImageDto.UploadDate), opt => opt.MapFrom(src => src.UploadedDate))
            .ForCtorParam(nameof(AppImageDto.UploadedBy), opt => opt.MapFrom(src => src.UploadedBy.Login))
            .ForCtorParam(nameof(AppImageDto.UrlToImage), opt => opt.MapFrom(src => $"api/images/{src.Id}"))
            .ForCtorParam(nameof(AppImageDto.Tags),
                opt => opt.MapFrom(src => src.Tags.Select(x => x.Name)));
        CreateMap<AppUser, AppUserDto>().ForCtorParam("Login", opt => opt.MapFrom(src => src.Login));
    }
}