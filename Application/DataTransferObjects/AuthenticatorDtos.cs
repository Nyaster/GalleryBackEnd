using System.ComponentModel.DataAnnotations;

namespace Shared.DataTransferObjects;

public static class AuthenticatorProofFormat
{
    public const string Pattern = "^(?:[0-9]{6}|[A-Fa-f0-9]{32}|[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{8}){3})$";
}

public sealed record AuthenticatorSetupDto(
    [param: Required, StringLength(128)] string CurrentPassword,
    [param: RegularExpression(AuthenticatorProofFormat.Pattern)]
    string? CurrentCode = null);

public sealed record AuthenticatorConfirmDto(
    Guid SetupId,
    [param: Required, RegularExpression("^[0-9]{6}$")]
    string Code);

public sealed record AuthenticatorRemoveDto(
    [param: Required, StringLength(128)] string CurrentPassword,
    [param: Required, RegularExpression(AuthenticatorProofFormat.Pattern)]
    string Code);

public sealed record RecoverAccountDto(
    [param: Required, StringLength(64, MinimumLength = 4)]
    string Login,
    [param: Required, RegularExpression(AuthenticatorProofFormat.Pattern)]
    string Code,
    [param: Required, StringLength(128, MinimumLength = 12)]
    string NewPassword);

public sealed record RegenerateBackupCodesDto(
    [param: Required, StringLength(128)] string CurrentPassword,
    [param: Required, RegularExpression(AuthenticatorProofFormat.Pattern)]
    string Code);

public sealed record AuthenticatorBackupCodesResponse(IReadOnlyList<string> BackupCodes);

public sealed record AuthenticatorSetupResponse(
    Guid SetupId,
    string ManualKey,
    string AuthenticatorUri,
    DateTimeOffset ExpiresAtUtc);