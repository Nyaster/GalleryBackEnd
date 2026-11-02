namespace Service.Contracts;

public interface IUserContext
{
    int? UserId { get; }
    string? Login { get; }
    bool IsInRole(string role);
    void RequireAuthenticated();
}
