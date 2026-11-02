using Entities.Models;

namespace Contracts;

public sealed record RefreshSessionRotationResult(AppUser? User, int? UserId, bool FamilyRevoked);
