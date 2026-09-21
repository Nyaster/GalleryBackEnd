using Entities.Exceptions;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class LastBackupCodeTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_PreservesLastCode_ForRemovalOrRecovery(bool recoverPassword)
    {
        const string password = "original-strong-password";
        const string newPassword = "replacement-strong-password";
        var clock = new TestClock();
        var login = "lastcode" + Guid.NewGuid().ToString("N");
        int id;
        await using (var context = database.CreateContext())
            id = (await database.Authentication(context, clock).RegisterAsync(new(login, password))).Response.User.Id;

        AuthenticatorSetupResponse setup;
        await using (var context = database.CreateContext())
            setup = await database.Authenticator(context, clock).SetupAsync(id, new(password));
        AuthenticatorBackupCodesResponse codes;
        await using (var context = database.CreateContext())
            codes = await database.Authenticator(context, clock).ConfirmAsync(id, new(setup.SetupId, Code(setup)));

        foreach (var code in codes.BackupCodes.Take(9))
        {
            await using var context = database.CreateContext();
            setup = await database.Authenticator(context, clock).SetupAsync(id, new(password, code));
        }
        clock.Advance(TimeSpan.FromMinutes(11));
        var lastCode = codes.BackupCodes[^1].Replace("-", "").ToLowerInvariant();
        await using (var context = database.CreateContext())
            await Assert.ThrowsAsync<Base409ConflictException>(() => database.Authenticator(context, clock)
                .SetupAsync(id, new(password, lastCode)));
        await using (var context = database.CreateContext())
        {
            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == id);
            Assert.Single(user.AuthenticatorBackupCodeHashes);
            Assert.NotNull(user.AuthenticatorEnabledAtUtc);
            Assert.Equal(setup.SetupId, user.AuthenticatorSetupId);
            Assert.Equal(0, user.AuthenticatorFailedAttempts);
        }
        // An invalid proof must still count as a failure, not reveal the last-code conflict.
        await using (var context = database.CreateContext())
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => database.Authenticator(context, clock)
                .SetupAsync(id, new(password, codes.BackupCodes[0])));
        await using (var context = database.CreateContext())
        {
            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == id);
            Assert.Equal(1, user.AuthenticatorFailedAttempts);
            if (recoverPassword)
                await database.Authenticator(context, clock).RecoverAsync(new(login, lastCode, newPassword));
            else
                await database.Authenticator(context, clock).RemoveAsync(id, new(password, lastCode));
        }

        var currentPassword = recoverPassword ? newPassword : password;
        await using (var context = database.CreateContext())
        {
            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == id);
            Assert.Null(user.AuthenticatorEnabledAtUtc);
            Assert.Empty(user.AuthenticatorBackupCodeHashes);
            setup = await database.Authenticator(context, clock).SetupAsync(id, new(currentPassword));
        }
        // Initial enrollment remains restartable even when another setup expires.
        clock.Advance(TimeSpan.FromMinutes(11));
        await using (var context = database.CreateContext())
            setup = await database.Authenticator(context, clock).SetupAsync(id, new(currentPassword));
        await using (var context = database.CreateContext())
        {
            var fresh = await database.Authenticator(context, clock).ConfirmAsync(id, new(setup.SetupId, Code(setup)));
            Assert.Equal(10, fresh.BackupCodes.Count);
        }

        string Code(AuthenticatorSetupResponse value)
            => new Totp(Base32Encoding.ToBytes(value.ManualKey)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
    }
}
