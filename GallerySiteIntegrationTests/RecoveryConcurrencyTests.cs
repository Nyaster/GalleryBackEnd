using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using Entities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OtpNet;
using Service.Contracts;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class RecoveryConcurrencyTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string Password = "original-strong-password";
    private const string NewPassword = "replacement-strong-password";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentBackupCodeUse_SucceedsOnce_AndPersistsConsumption(bool recoverPassword)
    {
        var clock = new TestClock();
        var (account, setup) = await EnrollAsync(clock);
        AuthenticatorBackupCodesResponse backupCodes;
        await using (var context = database.CreateContext())
            backupCodes = await database.Authenticator(context, clock).RegenerateBackupCodesAsync(
                account.Response.User.Id,
                new(Password, Code(setup, clock)));
        await using (var context = database.CreateContext())
        {
            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == account.Response.User.Id);
            Assert.Equal(10, user.AuthenticatorBackupCodeHashes.Count);
            Assert.All(user.AuthenticatorBackupCodeHashes, hash => Assert.Matches("^[0-9A-F]{64}$", hash));
            Assert.All(backupCodes.BackupCodes,
                code => Assert.DoesNotContain(code, user.AuthenticatorBackupCodeHashes));
        }

        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var context = database.CreateContext();
            var service = database.Authenticator(context, clock);
            return await Record.ExceptionAsync(async () =>
            {
                if (recoverPassword)
                    await service.RecoverAsync(
                        new(account.Response.User.Login, backupCodes.BackupCodes[0], NewPassword));
                else
                    await service.SetupAsync(account.Response.User.Id, new(Password, backupCodes.BackupCodes[0]));
            });
        }));
        Assert.Single(attempts, exception => exception is null);
        Assert.All(attempts.Where(exception => exception is not null),
            exception => Assert.IsType<AppUserUnauthorizedException>(exception));
        await using var check = database.CreateContext();
        var storedUser = await check.AppUsers.SingleAsync(user => user.Id == account.Response.User.Id);
        Assert.Equal(recoverPassword ? 0 : 9, storedUser.AuthenticatorBackupCodeHashes.Count);
        Assert.Equal(recoverPassword ? 1 : 0, storedUser.AuthenticationVersion);
        Assert.Equal(!recoverPassword, storedUser.AuthenticatorEnabledAtUtc is not null);
        if (recoverPassword)
            Assert.False(await check.RefreshSessions.AnyAsync(session =>
                session.UserId == storedUser.Id && session.RevokedAtUtc == null));
    }

    [Fact]
    public async Task ConcurrentRecovery_ConsumesCodeExactlyOnce()
    {
        var clock = new TestClock();
        var (account, setup) = await EnrollAsync(clock);
        var request = new RecoverAccountDto(account.Response.User.Login, Code(setup, clock), NewPassword);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await Record.ExceptionAsync(() => database.Authenticator(context, clock).RecoverAsync(request));
        }));
        Assert.Single(attempts, exception => exception is null);
        Assert.All(attempts.Where(exception => exception is not null),
            exception => Assert.IsType<AppUserUnauthorizedException>(exception));
        await using var check = database.CreateContext();
        var user = await check.AppUsers.SingleAsync(user => user.Id == account.Response.User.Id);
        Assert.Equal(1, user.AuthenticationVersion);
        Assert.All(await check.RefreshSessions.Where(session => session.UserId == user.Id).ToListAsync(),
            session => Assert.NotNull(session.RevokedAtUtc));
    }

    [Theory]
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    [InlineData("login", false)]
    [InlineData("login", true)]
    public async Task RecoveryRacingSessionCreation_LeavesNoUsableOldSession(string operation, bool recoveryFirst)
    {
        var clock = new TestClock();
        var (account, setup) = await EnrollAsync(clock);
        var gate = new UserLockGate();
        await using var firstContext = database.CreateContext(gate);
        await using var secondContext = database.CreateContext();
        await secondContext.Database.OpenConnectionAsync();
        var waitingPid = ((NpgsqlConnection)secondContext.Database.GetDbConnection()).ProcessID;
        AuthenticationResult? concurrentSession = null;

        async Task CreateSessionAsync()
        {
            var context = recoveryFirst ? secondContext : firstContext;
            var service = database.Authentication(context, clock);
            try
            {
                concurrentSession = operation == "refresh"
                    ? await service.RefreshAsync(account.RefreshToken)
                    : await service.LoginAsync(new(account.Response.User.Login, Password));
            }
            catch (AppUserUnauthorizedException)
            {
            }
        }

        async Task RecoverAsync()
        {
            var context = recoveryFirst ? firstContext : secondContext;
            await database.Authenticator(context, clock)
                .RecoverAsync(new(account.Response.User.Login, Code(setup, clock), NewPassword));
        }

        var firstTask = recoveryFirst ? RecoverAsync() : CreateSessionAsync();
        Task secondTask = Task.CompletedTask;
        try
        {
            await gate.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(20));
            secondTask = recoveryFirst ? CreateSessionAsync() : RecoverAsync();
            // Verify actual PostgreSQL lock contention, rather than relying on task scheduling.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var observer = database.CreateContext();
            while (await observer.Database.SqlQuery<int>(
                           $"SELECT cardinality(pg_blocking_pids({waitingPid})) AS \"Value\"")
                       .SingleAsync(timeout.Token) == 0)
                await Task.Delay(20, timeout.Token);
        }
        finally
        {
            gate.Release.TrySetResult();
            await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(20));
        }

        Assert.Equal(!recoveryFirst, concurrentSession is not null);
        await using var check = database.CreateContext();
        Assert.False(await check.RefreshSessions.AnyAsync(session =>
            session.UserId == account.Response.User.Id && session.RevokedAtUtc == null));
        if (concurrentSession is not null)
        {
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
                database.Authentication(check, clock).RefreshAsync(concurrentSession.RefreshToken));
            Assert.Equal("0",
                new JwtSecurityTokenHandler().ReadJwtToken(concurrentSession.Response.AccessToken).Claims
                    .Single(claim => claim.Type == "auth_version").Value);
        }

        var user = await check.AppUsers.AsNoTracking().SingleAsync(user => user.Id == account.Response.User.Id);
        Assert.Equal(1, user.AuthenticationVersion);
        await using var loginContext = database.CreateContext();
        var loggedIn = await database.Authentication(loginContext, clock)
            .LoginAsync(new(account.Response.User.Login, NewPassword));
        Assert.Equal("1",
            new JwtSecurityTokenHandler().ReadJwtToken(loggedIn.Response.AccessToken).Claims
                .Single(claim => claim.Type == "auth_version").Value);
    }

    [Fact]
    public async Task ConcurrentFailures_PersistCooldownAcrossConnections_WithoutBlockingPasswordLogin()
    {
        var clock = new TestClock();
        var (account, setup) = await EnrollAsync(clock);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var context = database.CreateContext();
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => database.Authenticator(context, clock)
                .RecoverAsync(new(account.Response.User.Login, "invalid", NewPassword)));
        }));
        await using (var check = database.CreateContext())
        {
            var user = await check.AppUsers.AsNoTracking().SingleAsync(user => user.Id == account.Response.User.Id);
            Assert.Equal(5, user.AuthenticatorFailedAttempts);
            Assert.NotNull(user.AuthenticatorLockedUntilUtc);
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => database.Authenticator(check, clock)
                .RecoverAsync(new(account.Response.User.Login, Code(setup, clock), NewPassword)));
        }

        await using (var login = database.CreateContext())
            Assert.NotNull(await database.Authentication(login, clock)
                .LoginAsync(new(account.Response.User.Login, Password)));
        clock.Advance(TimeSpan.FromMinutes(15));
        await using var recovery = database.CreateContext();
        await database.Authenticator(recovery, clock)
            .RecoverAsync(new(account.Response.User.Login, Code(setup, clock), NewPassword));
    }

    [Fact]
    public async Task PersistedKeys_AllowRecoveryWithNewProtectionProvider_AndSecretsAreEncrypted()
    {
        var clock = new TestClock();
        var (account, setup) = await EnrollAsync(clock);
        await using var context = database.CreateContext();
        var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == account.Response.User.Id);
        Assert.DoesNotContain(setup.ManualKey, user.AuthenticatorSecret!);
        Assert.NotEmpty(Directory.GetFiles(database.KeyPath, "key-*.xml"));
        // Every helper creates a fresh DataProtectionProvider; only the persisted key ring is shared.
        await database.Authenticator(context, clock)
            .RecoverAsync(new(account.Response.User.Login, Code(setup, clock), NewPassword));
    }

    private async Task<(AuthenticationResult Account, AuthenticatorSetupResponse Setup)> EnrollAsync(TestClock clock)
    {
        AuthenticationResult account;
        await using (var context = database.CreateContext())
            account = await database.Authentication(context, clock)
                .RegisterAsync(new("user" + Guid.NewGuid().ToString("N"), Password));
        AuthenticatorSetupResponse setup;
        await using (var context = database.CreateContext())
            setup = await database.Authenticator(context, clock).SetupAsync(account.Response.User.Id, new(Password));
        await using (var context = database.CreateContext())
            await database.Authenticator(context, clock)
                .ConfirmAsync(account.Response.User.Id, new(setup.SetupId, Code(setup, clock)));
        clock.Advance(TimeSpan.FromSeconds(30));
        return (account, setup);
    }

    private static string Code(AuthenticatorSetupResponse setup, TimeProvider clock)
        => new Totp(Base32Encoding.ToBytes(setup.ManualKey)).ComputeTotp(clock.GetUtcNow().UtcDateTime);

    private sealed class UserLockGate : DbCommandInterceptor
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"AppUsers\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Acquired.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }
}