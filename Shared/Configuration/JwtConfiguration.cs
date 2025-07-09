using System.ComponentModel.DataAnnotations;

namespace GallerySiteBackend.Configuration;

public class JwtConfiguration
{
    [Required]
    public string ValidIssuer { get; set; }
    [Required]
    public string ValidAudience { get; set; }
    [Required]
    [MinLength(20)]
    public string SecretKey { get; set; }
}