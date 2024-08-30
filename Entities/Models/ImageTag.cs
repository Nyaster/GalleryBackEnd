using Entities.Models;

namespace GallerySiteBackend.Models;

public class ImageTag
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatDateTime { get; set; }
    public AppUser CreatedBy { get; set; }
    public int CreatedById { get; set; }
    public int AppImageTagCount { get; set; }
    public List<AppImage> AppImages { get; set; } = new List<AppImage>();
}