using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class RoleAssignmentApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ConditionalAssignment_ComparesBaselineSetsWithoutAddingUserAndRequiresFreshBaseline()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var targetId = api.MemberId;
        using var updated = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { AccessRole.ModeratorId },
            expectedRoleIds = new[] { AccessRole.UserId }
        });
        await AssertRolesAsync(updated, AccessRole.ModeratorId, AccessRole.UserId);

        using var stale = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { AccessRole.TrustedContributorId },
            expectedRoleIds = new[] { AccessRole.UserId }
        });
        await AssertStaleAsync(stale);

        using var staleWithUnknownDesiredRole = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { int.MaxValue },
            expectedRoleIds = new[] { AccessRole.UserId }
        });
        await AssertStaleAsync(staleWithUnknownDesiredRole);

        using var staleNoOp = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { AccessRole.ModeratorId },
            expectedRoleIds = new[] { AccessRole.UserId }
        });
        await AssertStaleAsync(staleNoOp);

        using var missingBaselineUser = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { AccessRole.TrustedContributorId },
            expectedRoleIds = new[] { AccessRole.ModeratorId }
        });
        await AssertStaleAsync(missingBaselineUser);

        using var cleared = await api.SetRolesAsync(targetId, new
        {
            roleIds = Array.Empty<int>(),
            expectedRoleIds = new[] { AccessRole.UserId, AccessRole.ModeratorId, AccessRole.UserId }
        });
        await AssertRolesAsync(cleared, AccessRole.UserId);

        using var emptyBaseline = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { AccessRole.ModeratorId },
            expectedRoleIds = Array.Empty<int>()
        });
        await AssertStaleAsync(emptyBaseline);

        using var unknown = await api.SetRolesAsync(targetId, new
        {
            roleIds = new[] { int.MaxValue },
            expectedRoleIds = new[] { AccessRole.UserId }
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        await using var database = fixture.CreateContext();
        Assert.Equal(2, await database.AccessAudits.CountAsync(entry =>
            entry.Action == "assignRoles" && entry.TargetUserId == targetId));
        Assert.Equal(AccessRole.UserId, await database.AccountRoles.Where(role => role.UserId == targetId)
            .Select(role => role.RoleId).SingleAsync());
    }

    [Fact]
    public async Task ConditionalAssignment_ConcurrentRequestsCannotBothUseTheSameBaseline()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var baseline = new[] { AccessRole.UserId };
        var requests = await Task.WhenAll(
            api.SetRolesAsync(api.MemberId,
                new { roleIds = new[] { AccessRole.ModeratorId }, expectedRoleIds = baseline }),
            api.SetRolesAsync(api.MemberId,
                new { roleIds = new[] { AccessRole.TrustedContributorId }, expectedRoleIds = baseline }));
        using var first = requests[0];
        using var second = requests[1];

        var success = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.OK);
        var stale = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertStaleAsync(stale);
        var appliedRoleId = ReferenceEquals(first, success) ? AccessRole.ModeratorId : AccessRole.TrustedContributorId;
        await AssertRolesAsync(success, new[] { AccessRole.UserId, appliedRoleId }.Order().ToArray());
        await using var database = fixture.CreateContext();
        Assert.Equal(1, await database.AccessAudits.CountAsync(entry =>
            entry.Action == "assignRoles" && entry.TargetUserId == api.MemberId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalAssignment_OmissionRespectsModeAndExplicitNullIsInvalid(bool strict)
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture, strict);
        var desired = new[] { AccessRole.ModeratorId };
        using var explicitNull =
            await api.SetRolesAsync(api.MemberId, new { roleIds = desired, expectedRoleIds = (int[]?)null });
        Assert.Equal(HttpStatusCode.BadRequest, explicitNull.StatusCode);

        using var omitted = await api.SetRolesAsync(api.MemberId, new { roleIds = desired });
        if (strict)
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        else
            await AssertRolesAsync(omitted, AccessRole.ModeratorId, AccessRole.UserId);

        using var matched = await api.SetRolesAsync(api.MemberId, new
        {
            roleIds = new[] { AccessRole.TrustedContributorId, AccessRole.TrustedContributorId },
            expectedRoleIds = strict ? [AccessRole.UserId] : new[] { AccessRole.ModeratorId, AccessRole.UserId }
        });
        await AssertRolesAsync(matched, AccessRole.UserId, AccessRole.TrustedContributorId);
    }

    [Fact]
    public async Task ConditionalAssignment_RequiresManagerAndDoesNotExposeRolesOnFailures()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        var request = new { roleIds = Array.Empty<int>(), expectedRoleIds = Array.Empty<int>() };
        using var anonymous = await api.Guest.PutAsJsonAsync(RoleAssignmentTestApi.Path(api.ManagerId), request);
        await AssertNoRoleDataAsync(anonymous, HttpStatusCode.Unauthorized);

        using var forbidden = await api.Member.PutAsJsonAsync(RoleAssignmentTestApi.Path(api.ManagerId), request);
        await AssertNoRoleDataAsync(forbidden, HttpStatusCode.Forbidden);

        using var notFound = await api.SetRolesAsync(int.MaxValue, request);
        await AssertNoRoleDataAsync(notFound, HttpStatusCode.NotFound);
    }

    private static async Task AssertStaleAsync(HttpResponseMessage response)
    {
        var problem = await AssertNoRoleDataAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("urn:lilgallery:problem:role-assignments-changed", problem.GetProperty("type").GetString());
    }

    internal static async Task<JsonElement> AssertNoRoleDataAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return default;
        using var document = JsonDocument.Parse(body);
        foreach (var property in new[] { "roles", "roleIds", "currentRoleIds", "expectedRoleIds", "permissions" })
            Assert.False(document.RootElement.TryGetProperty(property, out _), $"Unexpected role data in {property}.");
        return document.RootElement.Clone();
    }

    private static async Task AssertRolesAsync(HttpResponseMessage response, params int[] expected)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<AccessAccountDto>())!.RoleIds);
    }
}

public sealed class LastAdminRoleAssignmentApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MatchingBaseline_LastAdminConflictRetainsExistingDetailAndIsNotStale()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        using var response = await api.SetRolesAsync(api.ManagerId, new
        {
            roleIds = Array.Empty<int>(),
            expectedRoleIds = new[] { AccessRole.AdminId, AccessRole.UserId }
        });

        var problem = await RoleAssignmentApiTests.AssertNoRoleDataAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("At least one account must retain the Admin role.", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("type", out var type) &&
                     type.GetString() == "urn:lilgallery:problem:role-assignments-changed");
        await using var database = fixture.CreateContext();
        Assert.Equal(new[] { AccessRole.AdminId, AccessRole.UserId }, await database.AccountRoles
            .Where(role => role.UserId == api.ManagerId).OrderBy(role => role.RoleId)
            .Select(role => role.RoleId).ToArrayAsync());
        Assert.False(await database.AccessAudits.AnyAsync(entry =>
            entry.Action == "assignRoles" && entry.TargetUserId == api.ManagerId));
    }
}

internal sealed class RoleAssignmentTestApi(
    ApiFactory factory,
    WebApplicationFactory<Program> app,
    HttpClient manager,
    HttpClient member,
    HttpClient guest,
    int managerId,
    int memberId) : IAsyncDisposable
{
    public int ManagerId => managerId;
    public int MemberId => memberId;
    public HttpClient Manager => manager;
    public HttpClient Member => member;
    public HttpClient Guest => guest;

    public static async Task<RoleAssignmentTestApi> StartAsync(PostgresFixture fixture, bool strict = false)
    {
        var clock = new TestClock();
        await using var database = fixture.CreateContext();
        var auth = fixture.Authentication(database, clock);
        var manager = (await auth.RegisterAsync(new($"admin{Guid.NewGuid():N}", "strong-test-password"))).Response;
        var member = (await auth.RegisterAsync(new($"member{Guid.NewGuid():N}", "strong-test-password"))).Response;
        database.AccountRoles.Add(new AccountRole { UserId = manager.User.Id, RoleId = AccessRole.AdminId });
        await database.SaveChangesAsync();

        var factory = new ApiFactory(fixture, clock);
        var app = strict
            ? factory.WithWebHostBuilder(builder =>
                builder.UseSetting("RoleAssignments:RequireExpectedRoleIds", "true"))
            : factory;
        var options = new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://gallery.test"), HandleCookies = false, AllowAutoRedirect = false
        };
        var managerClient = app.CreateClient(options);
        managerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", manager.AccessToken);
        var memberClient = app.CreateClient(options);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        return new RoleAssignmentTestApi(factory, app, managerClient, memberClient, app.CreateClient(options),
            manager.User.Id, member.User.Id);
    }

    public static string Path(int id) => $"/api/admin/access/accounts/{id}/roles";

    public Task<HttpResponseMessage> SetRolesAsync(int id, object body) => manager.PutAsJsonAsync(Path(id), body);

    public async ValueTask DisposeAsync()
    {
        manager.Dispose();
        member.Dispose();
        guest.Dispose();
        if (!ReferenceEquals(app, factory)) await app.DisposeAsync();
        await factory.DisposeAsync();
    }
}