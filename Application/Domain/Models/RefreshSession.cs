namespace Entities.Models;

public sealed class RefreshSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid FamilyId { get; set; }
    public AppUser User { get; set; } = null!;
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? RevokeReason { get; set; }
}
