namespace GallerySiteBackend.Models.DTOs;

public class PageableImagesDTO
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string OrderBy {get; set; }
    public List<AppImageDTO> Images { get; set; }
}