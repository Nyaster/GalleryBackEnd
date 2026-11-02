using System.Globalization;
using System.Security.Cryptography;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OtpNet;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace Service;

public sealed class AuthenticatorService(
    IRepositoryManager repositories,
    IPasswordHasher<AppUser> passwordHasher,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger<AuthenticatorService> logger) : IAuthenticatorService
{
    private const string InvalidCredentials = "Invalid account recovery or authenticator credentials.";
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    public async Task<AuthenticatorSetupResponse> SetupAsync(int userId, AuthenticatorSetupDto request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureNotLocked(user);
        if (!PasswordMatches(user, request.CurrentPassword) ||
            (user.AuthenticatorEnabledAtUtc is not null &&
             !AcceptProof(user, request.CurrentCode, preserveLastBackupCode: true)))
            await RejectAsync(user, transaction, cancellationToken);

        var secret = RandomNumberGenerator.GetBytes(20);
        var manualKey = Base32Encoding.ToString(secret);
        CryptographicOperations.ZeroMemory(secret);
        user.PendingAuthenticatorSecret = Protector(user.Id).Protect(manualKey);
        user.AuthenticatorSetupId = Guid.NewGuid();
        user.AuthenticatorSetupExpiresAtUtc = clock.GetUtcNow().AddMinutes(10);
        ResetAttempts(user);
        await SaveAndCommitAsync(transaction, cancellationToken);
        // Never send this URI to a third-party QR service; render it locally in the client.
        var uri =
            $"otpauth://totp/{Uri.EscapeDataString("Lilgallery:" + user.Login)}?secret={manualKey}&issuer=Lilgallery&algorithm=SHA1&digits=6&period=30";
        return new AuthenticatorSetupResponse(user.AuthenticatorSetupId.Value, manualKey, uri,
            user.AuthenticatorSetupExpiresAtUtc.Value);
    }

    public async Task<AuthenticatorBackupCodesResponse> ConfirmAsync(int userId, AuthenticatorConfirmDto request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureNotLocked(user);
        long step = 0;
        if (user.AuthenticatorSetupId != request.SetupId || user.AuthenticatorSetupExpiresAtUtc is null ||
            user.AuthenticatorSetupExpiresAtUtc <= clock.GetUtcNow() || user.PendingAuthenticatorSecret is null ||
            !VerifyCode(user, user.PendingAuthenticatorSecret, request.Code, out step))
            await RejectAsync(user, transaction, cancellationToken);

        user.AuthenticatorSecret = user.PendingAuthenticatorSecret;
        user.AuthenticatorEnabledAtUtc = clock.GetUtcNow();
        user.AuthenticatorLastUsedStep = step;
        var backupCodes = AuthenticatorBackupCodes.Replace(user);
        ClearPending(user);
        ResetAttempts(user);
        await SaveAndCommitAsync(transaction, cancellationToken);
        logger.LogInformation("AuthenticatorConfirmed {UserId}", user.Id);
        return backupCodes;
    }

    public async Task<AuthenticatorBackupCodesResponse> RegenerateBackupCodesAsync(int userId,
        RegenerateBackupCodesDto request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureNotLocked(user);
        if (!PasswordMatches(user, request.CurrentPassword) || !AcceptProof(user, request.Code))
            await RejectAsync(user, transaction, cancellationToken);
        var backupCodes = AuthenticatorBackupCodes.Replace(user);
        // A pending replacement must not outlive a change of recovery credentials.
        ClearPending(user);
        ResetAttempts(user);
        await SaveAndCommitAsync(transaction, cancellationToken);
        logger.LogInformation("AuthenticatorBackupCodesRegenerated {UserId}", user.Id);
        return backupCodes;
    }

    public async Task RemoveAsync(int userId, AuthenticatorRemoveDto request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await LockUserAsync(userId, cancellationToken);
        EnsureNotLocked(user);
        if (!PasswordMatches(user, request.CurrentPassword) || !AcceptProof(user, request.Code))
            await RejectAsync(user, transaction, cancellationToken);

        user.AuthenticatorSecret = null;
        user.AuthenticatorEnabledAtUtc = null;
        user.AuthenticatorLastUsedStep = null;
        user.AuthenticatorBackupCodeHashes.Clear();
        ClearPending(user);
        ResetAttempts(user);
        await SaveAndCommitAsync(transaction, cancellationToken);
        logger.LogInformation("AuthenticatorRemoved {UserId}", user.Id);
    }

    public async Task RecoverAsync(RecoverAccountDto request, CancellationToken cancellationToken = default)
    {
        // Also validate here so non-HTTP callers cannot set a weaker password.
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length is < 12 or > 128)
            throw new Base400BadRequestException("Password must contain between 12 and 128 characters.");
        await using var transaction = await repositories.BeginTransactionAsync(cancellationToken);
        var user = await repositories.AppUser.LockByNormalizedLoginAsync(request.Login.Trim().ToUpperInvariant(),
            cancellationToken);
        if (user is null)
            throw new AppUserUnauthorizedException(InvalidCredentials);
        EnsureNotLocked(user);
        var usedBackupCode = request.Code.Length != 6;
        if (!AcceptProof(user, request.Code))
            await RejectAsync(user, transaction, cancellationToken);

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.AuthenticationVersion = checked(user.AuthenticationVersion + 1);
        if (usedBackupCode)
        {
            // Recover even with the last backup code: the lost app must not prevent re-enrollment.
            user.AuthenticatorSecret = null;
            user.AuthenticatorEnabledAtUtc = null;
            user.AuthenticatorLastUsedStep = null;
            user.AuthenticatorBackupCodeHashes.Clear();
        }

        ClearPending(user);
        ResetAttempts(user);
        await repositories.AppUser.RevokeAllRefreshSessionsAsync(user.Id, clock.GetUtcNow(), cancellationToken);
        await SaveAndCommitAsync(transaction, cancellationToken);
        logger.LogInformation("AccountRecovered {UserId}", user.Id);
    }

    private async Task<AppUser> LockUserAsync(int userId, CancellationToken cancellationToken)
        => await repositories.AppUser.LockByIdAsync(userId, cancellationToken)
           ?? throw new AppUserUnauthorizedException(InvalidCredentials);

    private IDataProtector Protector(int userId)
        => protection.CreateProtector("Lilgallery.Authenticator.v1", userId.ToString(CultureInfo.InvariantCulture));

    private bool PasswordMatches(AppUser user, string password)
        => passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    private bool VerifyCode(AppUser user, string protectedSecret, string? code, out long step)
    {
        step = 0;
        if (code is null || code.Length != 6 || code.Any(character => character is < '0' or > '9'))
            return false;
        // Decryption failures fail closed; never silently reset an enrollment when keys are missing.
        var secret = Base32Encoding.ToBytes(Protector(user.Id).Unprotect(protectedSecret));
        try
        {
            return new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6)
                .VerifyTotp(clock.GetUtcNow().UtcDateTime, code, out step, new VerificationWindow(1, 1));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private bool AcceptActiveCode(AppUser user, string? code)
    {
        if (user.AuthenticatorEnabledAtUtc is null || user.AuthenticatorSecret is null ||
            !VerifyCode(user, user.AuthenticatorSecret, code, out var step) ||
            (user.AuthenticatorLastUsedStep is { } lastStep && step <= lastStep))
            return false;
        user.AuthenticatorLastUsedStep = step;
        return true;
    }

    private bool AcceptProof(AppUser user, string? code, bool preserveLastBackupCode = false)
        => code?.Length == 6
            ? AcceptActiveCode(user, code)
            : AuthenticatorBackupCodes.TryConsume(user, code, preserveLastBackupCode);

    private void EnsureNotLocked(AppUser user)
    {
        if (user.AuthenticatorLockedUntilUtc > clock.GetUtcNow())
            throw new AppUserUnauthorizedException(InvalidCredentials);
    }

    private async Task RejectAsync(AppUser user, IRepositoryTransaction transaction,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (user.AuthenticatorAttemptWindowUtc is null || now - user.AuthenticatorAttemptWindowUtc >= AttemptWindow)
        {
            user.AuthenticatorAttemptWindowUtc = now;
            user.AuthenticatorFailedAttempts = 0;
        }

        user.AuthenticatorFailedAttempts++;
        if (user.AuthenticatorFailedAttempts >= 5)
            user.AuthenticatorLockedUntilUtc = now.Add(AttemptWindow);
        // Persist failures before throwing; disposing a failed transaction must not erase the limiter.
        await SaveAndCommitAsync(transaction, cancellationToken);
        logger.LogWarning("AuthenticatorAttemptRejected {UserId}", user.Id);
        throw new AppUserUnauthorizedException(InvalidCredentials);
    }

    private async Task SaveAndCommitAsync(IRepositoryTransaction transaction, CancellationToken cancellationToken)
    {
        await repositories.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ResetAttempts(AppUser user)
    {
        user.AuthenticatorFailedAttempts = 0;
        user.AuthenticatorAttemptWindowUtc = null;
        user.AuthenticatorLockedUntilUtc = null;
    }

    private static void ClearPending(AppUser user)
    {
        user.PendingAuthenticatorSecret = null;
        user.AuthenticatorSetupId = null;
        user.AuthenticatorSetupExpiresAtUtc = null;
    }
}