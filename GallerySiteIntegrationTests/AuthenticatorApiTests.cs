using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Entities.Models;
using GallerySiteBackend.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using Repository;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class AuthenticatorApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string Password = "original-strong-password";
    private const string NewPassword = "replacement-strong-password";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrollRestartRecover_InvalidatesBothTokenTypes_AndAllowsPasswordOnlyLogin(bool useBackupCode)
    {
        var clock = new TestClock();
        JwtTokenResponse registered;
        AuthenticatorSetupResponse setup;
        IReadOnlyList<string> backupCodes;
        string refreshCookie;
        await using (var app = new ApiFactory(database, clock))
        {
            using var client = app.Client();
            var response = await client.PostAsJsonAsync("/api/auth/register",
                new CreateUserDto("user" + Guid.NewGuid().ToString("N"), Password));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            registered = (await response.Content.ReadFromJsonAsync<JwtTokenResponse>())!;
            refreshCookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", registered.AccessToken);
            var bearerOptions = app.Services
                .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
                .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
            Assert.True(bearerOptions.TokenValidationParameters.ValidIssuer == PostgresFixture.Jwt.ValidIssuer,
                "Test host bearer issuer differs from configured test issuer.");
            Assert.True(
                ((SymmetricSecurityKey)bearerOptions.TokenValidationParameters.IssuerSigningKey).Key.SequenceEqual(
                    Encoding.UTF8.GetBytes(PostgresFixture.Jwt.SecretKey)),
                "Test host bearer signing key differs from configured test key.");
            var before = await client.GetFromJsonAsync<AppUserDto>("/api/auth/me");
            Assert.False(before!.CanUploadImages);
            var setupResponse =
                await client.PostAsJsonAsync("/api/auth/authenticator/setup", new AuthenticatorSetupDto(Password));
            Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
            Assert.True(setupResponse.Headers.CacheControl?.NoStore);
            setup = (await setupResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>())!;
            var confirmation = await client.PostAsJsonAsync("/api/auth/authenticator/confirm",
                new AuthenticatorConfirmDto(setup.SetupId, Code(setup, clock)));
            Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
            Assert.True(confirmation.Headers.CacheControl?.NoStore);
            backupCodes = (await confirmation.Content.ReadFromJsonAsync<AuthenticatorBackupCodesResponse>())!
                .BackupCodes;
            Assert.Equal(10, backupCodes.Count);
            Assert.True((await client.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.CanUploadImages);
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new AppLoginDto(registered.User.Login, Password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        clock.Advance(TimeSpan.FromSeconds(30));
        await using var restarted = new ApiFactory(database, clock);
        using var next = restarted.Client();
        var recovery = await next.PostAsJsonAsync("/api/auth/recover",
            new RecoverAccountDto(registered.User.Login, useBackupCode ? backupCodes[0] : Code(setup, clock),
                NewPassword));
        Assert.Equal(HttpStatusCode.NoContent, recovery.StatusCode);
        Assert.Contains(recovery.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith("gallery_refresh=;", StringComparison.Ordinal));
        next.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await next.GetAsync("/api/auth/me")).StatusCode);
        next.DefaultRequestHeaders.Authorization = null;
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await next.SendAsync(refreshRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await next.PostAsJsonAsync("/api/auth/login", new AppLoginDto(registered.User.Login, Password)))
            .StatusCode);
        var loginResponse =
            await next.PostAsJsonAsync("/api/auth/login", new AppLoginDto(registered.User.Login, NewPassword));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loggedIn = (await loginResponse.Content.ReadFromJsonAsync<JwtTokenResponse>())!;
        next.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.AccessToken);
        var after = await next.GetFromJsonAsync<AppUserDto>("/api/auth/me");
        Assert.Equal(!useBackupCode, after!.AuthenticatorEnabled);
        Assert.Equal(!useBackupCode, after.CanUploadImages);
        Assert.Equal(useBackupCode ? 0 : 10, after.BackupCodesRemaining);
        if (useBackupCode)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await next.PostAsJsonAsync("/api/auth/recover",
                new RecoverAccountDto(registered.User.Login, backupCodes[1], Password))).StatusCode);
            await using var reenrollmentApp = new ApiFactory(database, clock);
            using var reenroll = reenrollmentApp.Client();
            reenroll.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loggedIn.AccessToken);
            var setupResponse = await reenroll.PostAsJsonAsync("/api/auth/authenticator/setup",
                new AuthenticatorSetupDto(NewPassword));
            setupResponse.EnsureSuccessStatusCode();
            var newSetup = (await setupResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>())!;
            var confirmed = await reenroll.PostAsJsonAsync("/api/auth/authenticator/confirm",
                new AuthenticatorConfirmDto(newSetup.SetupId, Code(newSetup, clock)));
            confirmed.EnsureSuccessStatusCode();
            Assert.Equal(10,
                (await confirmed.Content.ReadFromJsonAsync<AuthenticatorBackupCodesResponse>())!.BackupCodes.Count);
            Assert.True((await reenroll.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.CanUploadImages);
        }
    }

    [Fact]
    public async Task BackupCodeRegeneration_IsProtectedNotCacheable_AndReplacesOldSet()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        var account = await database.Authentication(context, clock)
            .RegisterAsync(new("backup" + Guid.NewGuid().ToString("N"), Password));
        var service = database.Authenticator(context, clock);
        var setup = await service.SetupAsync(account.Response.User.Id, new(Password));
        var original = await service.ConfirmAsync(account.Response.User.Id, new(setup.SetupId, Code(setup, clock)));
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", account.Response.AccessToken);
        var regenerated = await client.PostAsJsonAsync("/api/auth/authenticator/backup-codes",
            new RegenerateBackupCodesDto(Password, original.BackupCodes[0]));
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        Assert.True(regenerated.Headers.CacheControl?.NoStore);
        var replacement = (await regenerated.Content.ReadFromJsonAsync<AuthenticatorBackupCodesResponse>())!;
        Assert.Equal(10, replacement.BackupCodes.Count);
        var me = await client.GetAsync("/api/auth/me");
        var json = await me.Content.ReadAsStringAsync();
        Assert.All(replacement.BackupCodes, code => Assert.DoesNotContain(code, json));
        Assert.Equal(10, (await me.Content.ReadFromJsonAsync<AppUserDto>())!.BackupCodesRemaining);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/recover",
            new RecoverAccountDto(account.Response.User.Login, original.BackupCodes[1], NewPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/authenticator/remove",
                new AuthenticatorRemoveDto(Password, replacement.BackupCodes[0].Replace("-", "").ToLowerInvariant())))
            .StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.BackupCodesRemaining);
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("confirm")]
    [InlineData("remove")]
    [InlineData("backup-codes")]
    public async Task ManagementEndpoints_RequireAuthentication(string endpoint)
    {
        await using var app = new ApiFactory(database, new TestClock());
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/authenticator/" + endpoint, new { })).StatusCode);
    }

    [Fact]
    public async Task Recovery_ValidatesPayload_AndRateLimitsUnknownAccounts()
    {
        await using var app = new ApiFactory(database, new TestClock());
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/recover", new RecoverAccountDto("unknown", "abcdef", "short")))
            .StatusCode);
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/recover",
                    new RecoverAccountDto("unknown", "123456", NewPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/recover", new RecoverAccountDto("unknown", "123456", NewPassword)))
            .StatusCode);
    }

    [Fact]
    public async Task LegacyTokens_WorkOnlyAtVersionZero_AndDeletedUsersAreRejected()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        var account = await database.Authentication(context, clock)
            .RegisterAsync(new("legacy" + Guid.NewGuid().ToString("N"), Password));
        var jwt = PostgresFixture.Jwt;
        var token = new JwtSecurityToken(jwt.ValidIssuer, jwt.ValidAudience,
            [
                new Claim(ClaimTypes.NameIdentifier, account.Response.User.Id.ToString()),
                new Claim(ClaimTypes.Name, account.Response.User.Login)
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                SecurityAlgorithms.HmacSha256));
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await context.AppUsers.Where(user => user.Id == account.Response.User.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.AuthenticationVersion, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", account.Response.AccessToken);
        await context.AppUsers.Where(user => user.Id == account.Response.User.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task BlockTakesEffectWithExistingToken_AndEnrollmentDoesNotClearIt()
    {
        var clock = new TestClock();
        await using var context = database.CreateContext();
        var account = await database.Authentication(context, clock)
            .RegisterAsync(new("blocked" + Guid.NewGuid().ToString("N"), Password));
        await context.AppUsers.Where(user => user.Id == account.Response.User.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(user => user.UploadsBlocked, true).SetProperty(user => user.CanUploadImages, true));
        await using var app = new ApiFactory(database, clock);
        using var client = app.Client();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", account.Response.AccessToken);
        var setupResponse =
            await client.PostAsJsonAsync("/api/auth/authenticator/setup", new AuthenticatorSetupDto(Password));
        setupResponse.EnsureSuccessStatusCode();
        var setup = (await setupResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>())!;
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/authenticator/confirm",
                new AuthenticatorConfirmDto(setup.SetupId, Code(setup, clock)))).StatusCode);
        var status = await client.GetFromJsonAsync<AppUserDto>("/api/auth/me");
        Assert.True(status!.AuthenticatorEnabled);
        Assert.True(status.UploadsBlocked);
        Assert.False(status.CanUploadImages);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "ImageFile", "test.png");
        form.Add(new StringContent("HumanMade"), "AiUsage");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/images", form)).StatusCode);
        await context.AppUsers.Where(user => user.Id == account.Response.User.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.UploadsBlocked, false));
        Assert.True((await client.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.CanUploadImages);
    }

    private static string Code(AuthenticatorSetupResponse setup, TimeProvider clock)
        => new Totp(Base32Encoding.ToBytes(setup.ManualKey)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
}

internal sealed class ApiFactory(PostgresFixture database, TimeProvider clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["JwtConfig:ValidIssuer"] = PostgresFixture.Jwt.ValidIssuer,
            ["JwtConfig:ValidAudience"] = PostgresFixture.Jwt.ValidAudience,
            ["JwtConfig:SecretKey"] = PostgresFixture.Jwt.SecretKey,
            ["Authenticator:KeyRingPath"] = database.KeyPath,
            ["ImageStorage:RootPath"] = Path.Combine(database.RootPath, "images"),
            ["Observability:LogPath"] = Path.Combine(database.RootPath, "logs"),
            ["Embedding:Enabled"] = "false"
        };
        foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                                                                 && service.ImplementationType?.Namespace ==
                                                                 "Application.BackgroundService").ToArray())
                services.Remove(descriptor);
        });
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://gallery.test"), HandleCookies = false, AllowAutoRedirect = false });
}