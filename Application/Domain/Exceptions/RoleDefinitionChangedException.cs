namespace Entities.Exceptions;

public sealed class RoleDefinitionChangedException()
    : Base409ConflictException("This role has changed. Reload it before saving.");
