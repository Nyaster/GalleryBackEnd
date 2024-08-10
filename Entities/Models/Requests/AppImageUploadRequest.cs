using Microsoft.AspNetCore.Http;

namespace Entities.Models.Requests;

public class AppImageUploadRequest
{
    public IFormFile Image { get; set; }
    public string UploadedBy { get; set; }
    public List<int> TagIds { get; set; }
    public bool IsHidden { get; set; }
}