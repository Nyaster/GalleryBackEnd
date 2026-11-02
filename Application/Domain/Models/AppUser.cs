namespace Entities.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    public required string Login { get; set; }
    public required string NormalizedLogin { get; set; }
    public required string PasswordHash { get; set; }
    public List<AppUserRole> Roles { get; set; } = [AppUserRole.User];
    public bool CanUploadImages { get; set; }
    public bool UploadsBlocked { get; set; }
    public string? AuthenticatorSecret { get; set; }
    public DateTimeOffset? AuthenticatorEnabledAtUtc { get; set; }
    public long? AuthenticatorLastUsedStep { get; set; }
    public List<string> AuthenticatorBackupCodeHashes { get; set; } = [];
    public string? PendingAuthenticatorSecret { get; set; }
    public Guid? AuthenticatorSetupId { get; set; }
    public DateTimeOffset? AuthenticatorSetupExpiresAtUtc { get; set; }
    public int AuthenticatorFailedAttempts { get; set; }
    public DateTimeOffset? AuthenticatorAttemptWindowUtc { get; set; }
    public DateTimeOffset? AuthenticatorLockedUntilUtc { get; set; }
    public int AuthenticationVersion { get; set; }
    public List<AppImage> UploadedImages { get; set; } = [];
    public List<RefreshSession> RefreshSessions { get; set; } = [];
    public List<Comment> Comments { get; set; } = [];
    public List<ImageLike> ImageLikes { get; set; } = [];
    public CommentRestriction? CommentRestriction { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum AppUserRole
{
    Admin,
    Moderator,
    User
}