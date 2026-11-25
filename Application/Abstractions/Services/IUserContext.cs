namespace Service.Contracts;

public interface IUserContext
{
    int? UserId { get; }
    string? Login { get; }
    bool HasPermission(string permission);
    void RequireAuthenticated();
}