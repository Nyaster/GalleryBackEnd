using System.Security.Cryptography;
using Contracts;
using Entities.Exceptions;
using Entities.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OtpNet;
using Service;
using Shared.DataTransferObjects;

namespace GallerySiteUnitTests;

public sealed class AuthenticatorServiceTests
{
    [Fact]
    public async Task Setup_RequiresPassword_AndDoesNotGrantAccessBeforeConfirmation()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => fixture.Service.SetupAsync(7, new("incorrect")));
        Assert.Null(fixture.User.PendingAuthenticatorSecret);
        var setup = await fixture.Service.SetupAsync(7, new(Fixture.Password));
        Assert.NotEqual(setup.ManualKey, fixture.User.PendingAuthenticatorSecret);
        Assert.Contains("Lilgallery%3Agalleryuser", setup.AuthenticatorUri);
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(10), setup.ExpiresAtUtc);
        Assert.False(UploadAccess.IsAllowed(fixture.User));
        await fixture.Service.ConfirmAsync(7, new(setup.SetupId, fixture.Code(setup.ManualKey)));
        Assert.True(UploadAccess.IsAllowed(fixture.User));
        Assert.Null(fixture.User.PendingAuthenticatorSecret);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("superseded")]
    [InlineData("wrong-code")]
    [InlineData("wrong-user")]
    public async Task Confirm_RejectsInvalidSetup(string reason)
    {
        var fixture = new Fixture();
        var setup = await fixture.Service.SetupAsync(7, new(Fixture.Password));
        if (reason == "expired") fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        if (reason == "superseded") await fixture.Service.SetupAsync(7, new(Fixture.Password));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => fixture.Service.ConfirmAsync(
            reason == "wrong-user" ? 8 : 7,
            new(setup.SetupId, reason == "wrong-code" ? "bad" : fixture.Code(setup.ManualKey))));
        Assert.Null(fixture.User.AuthenticatorEnabledAtUtc);
    }

    [Fact]
    public async Task Replacement_RequiresExistingCode_AndPreservesOldSecretUntilConfirmed()
    {
        var fixture = new Fixture();
        var first = await fixture.EnrollAsync();
        var originalSecret = fixture.User.AuthenticatorSecret;
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.SetupAsync(7, new(Fixture.Password)));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.SetupAsync(7, new("wrong", fixture.Code(first.ManualKey))));
        var replacement = await fixture.Service.SetupAsync(7, new(Fixture.Password, fixture.Code(first.ManualKey)));
        Assert.Equal(originalSecret, fixture.User.AuthenticatorSecret);
        Assert.True(UploadAccess.IsAllowed(fixture.User));
        await fixture.Service.ConfirmAsync(7, new(replacement.SetupId, fixture.Code(replacement.ManualKey)));
        Assert.NotEqual(originalSecret, fixture.User.AuthenticatorSecret);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(replacement.ManualKey),
            Fixture.NewPassword));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_RequiresBothProofs_AndPreservesManualApproval(bool approved)
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        fixture.User.CanUploadImages = approved;
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new("wrong", fixture.Code(setup.ManualKey))));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new(Fixture.Password, "bad")));
        await fixture.Service.RemoveAsync(7, new(Fixture.Password, fixture.Code(setup.ManualKey)));
        Assert.Null(fixture.User.AuthenticatorSecret);
        Assert.Null(fixture.User.AuthenticatorEnabledAtUtc);
        Assert.Null(fixture.User.AuthenticatorSetupId);
        Assert.Equal(approved, UploadAccess.IsAllowed(fixture.User));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), Fixture.NewPassword)));
    }

    [Fact]
    public async Task Recovery_ChangesPasswordRevokesSessionsAndClearsPendingReplacement()
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        await fixture.Service.SetupAsync(7, new(Fixture.Password, fixture.Code(setup.ManualKey)));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.Service.RecoverAsync(new(" GalleryUser ", fixture.Code(setup.ManualKey), Fixture.NewPassword));
        Assert.Equal(PasswordVerificationResult.Success,
            fixture.Hasher.VerifyHashedPassword(fixture.User, fixture.User.PasswordHash, Fixture.NewPassword));
        Assert.Equal(PasswordVerificationResult.Failed,
            fixture.Hasher.VerifyHashedPassword(fixture.User, fixture.User.PasswordHash, Fixture.Password));
        Assert.Equal(1, fixture.User.AuthenticationVersion);
        Assert.NotNull(fixture.User.AuthenticatorEnabledAtUtc);
        Assert.Null(fixture.User.PendingAuthenticatorSecret);
        fixture.Users.Verify(
            users => users.RevokeAllRefreshSessionsAsync(7, fixture.Clock.GetUtcNow(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-2, false)]
    [InlineData(2, false)]
    public async Task Recovery_UsesLimitedClockTolerance(int offset, bool accepted)
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var code = fixture.Code(setup.ManualKey, TimeSpan.FromSeconds(offset * 30));
        var recover = () => fixture.Service.RecoverAsync(new("galleryuser", code, Fixture.NewPassword));
        if (accepted) await recover();
        else await Assert.ThrowsAsync<AppUserUnauthorizedException>(recover);
    }

    [Fact]
    public async Task Codes_CannotBeReusedAcrossConfirmationRecoveryAndRemoval()
    {
        var fixture = new Fixture();
        var setup = await fixture.Service.SetupAsync(7, new(Fixture.Password));
        var code = fixture.Code(setup.ManualKey);
        await fixture.Service.ConfirmAsync(7, new(setup.SetupId, code));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", code, Fixture.NewPassword)));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        code = fixture.Code(setup.ManualKey);
        await fixture.Service.RecoverAsync(new("galleryuser", code, Fixture.NewPassword));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new(Fixture.NewPassword, code)));
    }

    [Fact]
    public async Task FailedAttempts_CommitBeforeThrowing_AndCooldownExpires()
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        fixture.Transaction.Invocations.Clear();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
                fixture.Service.RecoverAsync(new("galleryuser", "bad", Fixture.NewPassword)));
        fixture.Transaction.Verify(transaction => transaction.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(5));
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(15), fixture.User.AuthenticatorLockedUntilUtc);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), Fixture.NewPassword)));
        // Creating another service does not reset the persisted limit.
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.NewService(fixture.Protection).SetupAsync(7, new(Fixture.Password, fixture.Code(setup.ManualKey))));
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), Fixture.NewPassword));
        Assert.Equal(0, fixture.User.AuthenticatorFailedAttempts);
        Assert.Null(fixture.User.AuthenticatorLockedUntilUtc);
    }

    [Fact]
    public async Task Recovery_UsesSameFailureForUnknownAndUnenrolledUsers()
    {
        var fixture = new Fixture();
        var unknown = await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("unknown", "123456", Fixture.NewPassword)));
        var unenrolled = await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", "123456", Fixture.NewPassword)));
        var setup = await fixture.EnrollAsync();
        var invalid = await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", "bad", Fixture.NewPassword)));
        Assert.Equal(unknown.Message, unenrolled.Message);
        Assert.Equal(unknown.Message, invalid.Message);
    }

    [Fact]
    public async Task MissingEncryptionKeys_FailsClosedWithoutClearingEnrollment()
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        var service = fixture.NewService(new EphemeralDataProtectionProvider());
        await Assert.ThrowsAsync<CryptographicException>(() =>
            service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), Fixture.NewPassword)));
        Assert.NotNull(fixture.User.AuthenticatorSecret);
        Assert.NotNull(fixture.User.AuthenticatorEnabledAtUtc);
        Assert.Equal(0, fixture.User.AuthenticationVersion);
    }

    [Fact]
    public async Task Recovery_RejectsWeakPasswordsWithoutConsumingCode()
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        var previousStep = fixture.User.AuthenticatorLastUsedStep;
        await Assert.ThrowsAsync<Base400BadRequestException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), "short")));
        Assert.Equal(previousStep, fixture.User.AuthenticatorLastUsedStep);
    }

    [Fact]
    public async Task Confirmation_IssuesTenUniqueCodes_StoresOnlyHashes_AndReportsOnlyCount()
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        Assert.Equal(10, fixture.BackupCodes.Count);
        Assert.Equal(10, fixture.BackupCodes.Distinct().Count());
        Assert.Equal(10, fixture.User.AuthenticatorBackupCodeHashes.Count);
        foreach (var code in fixture.BackupCodes)
        {
            Assert.Matches("^[A-F0-9]{8}(-[A-F0-9]{8}){3}$", code);
            Assert.DoesNotContain(code, fixture.User.AuthenticatorBackupCodeHashes);
            Assert.DoesNotContain(code.Replace("-", ""), fixture.User.AuthenticatorBackupCodeHashes);
        }

        var status = AppUserDto.FromUser(fixture.User);
        Assert.Equal(10, status.BackupCodesRemaining);
        var json = System.Text.Json.JsonSerializer.Serialize(status);
        Assert.All(fixture.BackupCodes, code => Assert.DoesNotContain(code, json));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LastBackupCodeRecovery_UnlinksAppRevokesAllCodesAndPreservesAdminSettings(bool granted,
        bool blocked)
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        fixture.User.CanUploadImages = granted;
        fixture.User.UploadsBlocked = blocked;
        foreach (var code in fixture.BackupCodes.Take(9))
            await fixture.Service.SetupAsync(7, new(Fixture.Password, code));
        Assert.Single(fixture.User.AuthenticatorBackupCodeHashes);
        await fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[9], Fixture.NewPassword));
        Assert.Null(fixture.User.AuthenticatorSecret);
        Assert.Null(fixture.User.AuthenticatorEnabledAtUtc);
        Assert.Null(fixture.User.AuthenticatorLastUsedStep);
        Assert.Null(fixture.User.PendingAuthenticatorSecret);
        Assert.Empty(fixture.User.AuthenticatorBackupCodeHashes);
        Assert.Equal(1, fixture.User.AuthenticationVersion);
        Assert.Equal(granted, fixture.User.CanUploadImages);
        Assert.Equal(blocked, fixture.User.UploadsBlocked);
        Assert.Equal(granted && !blocked, UploadAccess.IsAllowed(fixture.User));
        fixture.Users.Verify(
            users => users.RevokeAllRefreshSessionsAsync(7, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
        await fixture.Service.SetupAsync(7, new(Fixture.NewPassword));
    }

    [Fact]
    public async Task BackupRecovery_InvalidatesUnusedCodes_AndCannotBeRepeated()
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        await fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[0], Fixture.NewPassword));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[0], Fixture.Password)));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[1], Fixture.Password)));
        Assert.Equal(1, fixture.User.AuthenticationVersion);
    }

    [Fact]
    public async Task WrongPassword_DoesNotConsumeBackupProofForManagement()
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        var code = fixture.BackupCodes[0];
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() => fixture.Service.SetupAsync(7, new("wrong", code)));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new("wrong", code)));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RegenerateBackupCodesAsync(7, new("wrong", code)));
        Assert.Equal(10, fixture.User.AuthenticatorBackupCodeHashes.Count);
        await fixture.Service.SetupAsync(7, new(Fixture.Password, code));
        Assert.Equal(9, fixture.User.AuthenticatorBackupCodeHashes.Count);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RegenerateBackupCodesAsync(7, new(Fixture.Password, code)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Regenerate_RequiresExistingProof_AndInvalidatesOldSetAndPendingSetup(bool useBackup)
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        var originalCodes = fixture.BackupCodes.ToArray();
        await fixture.Service.SetupAsync(7, new(Fixture.Password, originalCodes[0]));
        var result = await fixture.Service.RegenerateBackupCodesAsync(7,
            new(Fixture.Password, useBackup ? originalCodes[1] : fixture.Code(setup.ManualKey)));
        Assert.Equal(10, result.BackupCodes.Count);
        Assert.Equal(10, AppUserDto.FromUser(fixture.User).BackupCodesRemaining);
        Assert.Null(fixture.User.AuthenticatorSetupId);
        Assert.NotNull(fixture.User.AuthenticatorEnabledAtUtc);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new(Fixture.Password, originalCodes[2])));
        await fixture.Service.RemoveAsync(7, new(Fixture.Password, result.BackupCodes[0]));
        Assert.Empty(fixture.User.AuthenticatorBackupCodeHashes);
    }

    [Fact]
    public async Task Replacement_UsingBackupProof_IssuesFreshSetAfterNewAppConfirmation()
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        var originalCodes = fixture.BackupCodes.ToArray();
        var setup = await fixture.Service.SetupAsync(7, new(Fixture.Password, originalCodes[0]));
        var result = await fixture.Service.ConfirmAsync(7, new(setup.SetupId, fixture.Code(setup.ManualKey)));
        Assert.Equal(10, result.BackupCodes.Count);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RemoveAsync(7, new(Fixture.Password, originalCodes[1])));
        await fixture.Service.RemoveAsync(7, new(Fixture.Password, result.BackupCodes[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupCodes_AcceptLowercaseAndOptionalHyphens_WithoutAllowingReuse(bool hyphens)
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        var code = fixture.BackupCodes[0].ToLowerInvariant();
        if (!hyphens) code = code.Replace("-", "");
        await fixture.Service.SetupAsync(7, new(Fixture.Password, code));
        Assert.Equal(9, fixture.User.AuthenticatorBackupCodeHashes.Count);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.SetupAsync(7, new(Fixture.Password, fixture.BackupCodes[0])));
    }

    [Fact]
    public async Task InvalidBackupCodes_ShareAccountCooldownWithAppCodes()
    {
        var fixture = new Fixture();
        var setup = await fixture.EnrollAsync();
        for (var index = 0; index < 5; index++)
            await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
                fixture.Service.RecoverAsync(new("galleryuser", new string('0', 32), Fixture.NewPassword)));
        Assert.Equal(10, fixture.User.AuthenticatorBackupCodeHashes.Count);
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[0], Fixture.NewPassword)));
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RecoverAsync(new("galleryuser", fixture.Code(setup.ManualKey), Fixture.NewPassword)));
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await fixture.Service.RecoverAsync(new("galleryuser", fixture.BackupCodes[0], Fixture.NewPassword));
    }

    [Fact]
    public async Task BackupRecovery_WorksEvenIfAuthenticatorEncryptionKeysAreLost()
    {
        var fixture = new Fixture();
        await fixture.EnrollAsync();
        var service = fixture.NewService(new EphemeralDataProtectionProvider());
        await service.RecoverAsync(new("galleryuser", fixture.BackupCodes[0], Fixture.NewPassword));
        Assert.Null(fixture.User.AuthenticatorSecret);
        await service.SetupAsync(7, new(Fixture.NewPassword));
    }

    [Fact]
    public async Task UnenrolledUsers_CannotGenerateBackupCodes()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<AppUserUnauthorizedException>(() =>
            fixture.Service.RegenerateBackupCodesAsync(7, new(Fixture.Password, "123456")));
        Assert.Empty(fixture.User.AuthenticatorBackupCodeHashes);
    }

    private sealed class Fixture
    {
        public const string Password = "my-original-password";
        public const string NewPassword = "my-new-strong-password";

        public AppUser User { get; } = new()
            { Id = 7, Login = "galleryuser", NormalizedLogin = "GALLERYUSER", PasswordHash = "" };

        public PasswordHasher<AppUser> Hasher { get; } = new();
        public Mock<IAppUserRepository> Users { get; } = new();
        public Mock<IRepositoryManager> Repositories { get; } = new();
        public Mock<IRepositoryTransaction> Transaction { get; } = new();
        public TestClock Clock { get; } = new();
        public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
        public AuthenticatorService Service { get; }
        public IReadOnlyList<string> BackupCodes { get; private set; } = [];

        public Fixture()
        {
            User.PasswordHash = Hasher.HashPassword(User, Password);
            Users.Setup(users => users.LockByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(User);
            Users.Setup(users => users.LockByNormalizedLoginAsync("GALLERYUSER", It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Repositories.SetupGet(repositories => repositories.AppUser).Returns(Users.Object);
            Repositories.Setup(repositories => repositories.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Transaction.Object);
            Service = NewService(Protection);
        }

        public AuthenticatorService NewService(IDataProtectionProvider protection)
            => new(Repositories.Object, Hasher, protection, Clock, NullLogger<AuthenticatorService>.Instance);

        public async Task<AuthenticatorSetupResponse> EnrollAsync()
        {
            var setup = await Service.SetupAsync(7, new(Password));
            BackupCodes = (await Service.ConfirmAsync(7, new(setup.SetupId, Code(setup.ManualKey)))).BackupCodes;
            Clock.Advance(TimeSpan.FromSeconds(30));
            return setup;
        }

        public string Code(string key, TimeSpan offset = default)
            => new Totp(Base32Encoding.ToBytes(key)).ComputeTotp(Clock.GetUtcNow().Add(offset).UtcDateTime);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}