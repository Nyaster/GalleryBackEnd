namespace Entities.Models.Requests;

public class AppUserRegistrationRequest
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}