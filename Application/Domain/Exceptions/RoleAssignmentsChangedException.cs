namespace Entities.Exceptions;

public sealed class RoleAssignmentsChangedException()
    : Base409ConflictException("The account's role assignments have changed. Reload them before saving.");
