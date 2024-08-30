namespace Shared.DataTransferObjects;

public class PageableImagesDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string OrderBy {get; set; }
    public int Total { get; set; }
    public List<AppImageDto> Images { get; set; }
}