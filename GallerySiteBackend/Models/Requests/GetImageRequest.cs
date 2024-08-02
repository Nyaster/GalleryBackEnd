namespace GallerySiteBackend.Models.Requests;

public class GetImageRequest
{
    public string? Tags { get; set; }
    public string? SortBy { get; set; }
    public int Page { get; set; }
}