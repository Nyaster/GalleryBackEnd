using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Entities.Models;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class ContentVisibilitySettingsApiTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string PreferencesRoute = "/api/users/me/content-preferences";
    private const string RulesRoute = "/api/admin/content-visibility-rules";
    private const string Password = "Strong-integration-password123!";

    [Fact]
    public async Task PreferencesAreRequiredAndPersistForTheCurrentUserWithTheSameToken()
    {
        var clock = new TestClock();
        await using var app = new ApiFactory(database, clock);
        using var first = app.Client();
        using var second = app.Client();
        using var guest = app.Client();
        Assert.False((await AuthenticateAsync(first, clock, AppUserRole.User)).DisplayAllContent);
        await AuthenticateAsync(second, clock, AppUserRole.User);
        var optIn = new UpdateContentPreferencesDto(true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsJsonAsync(PreferencesRoute, optIn)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PutAsJsonAsync(PreferencesRoute, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await first.PutAsJsonAsync(PreferencesRoute, new UpdateContentPreferencesDto(null))).StatusCode);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await first.PutAsJsonAsync(PreferencesRoute, optIn);
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadFromJsonAsync<AppUserDto>())!.DisplayAllContent);
        }

        Assert.True((await first.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.DisplayAllContent);
        Assert.False((await second.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.DisplayAllContent);
        using var optOut = await first.PutAsJsonAsync(PreferencesRoute, new UpdateContentPreferencesDto(false));
        optOut.EnsureSuccessStatusCode();
        Assert.False((await first.GetFromJsonAsync<AppUserDto>("/api/auth/me"))!.DisplayAllContent);
    }

    [Fact]
    public async Task RulesRequireAdminAndValidateTagGroupsOnCreateAndUpdate()
    {
        var clock = new TestClock();
        await using var app = new ApiFactory(database, clock);
        using var guest = app.Client();
        using var user = app.Client();
        using var moderator = app.Client();
        using var admin = app.Client();
        await AuthenticateAsync(user, clock, AppUserRole.User);
        await AuthenticateAsync(moderator, clock, AppUserRole.Moderator);
        await AuthenticateAsync(admin, clock, AppUserRole.Admin);
        var first = await AddTagAsync(clock);
        var second = await AddTagAsync(clock);
        var pending = await AddTagAsync(clock, TagModerationStatus.Pending);
        var rejected = await AddTagAsync(clock, TagModerationStatus.Rejected);
        var group = new SaveContentVisibilityRuleDto([second, first]);

        foreach (var client in new[] { guest, user, moderator })
        {
            var denied = client == guest ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(denied, (await client.GetAsync(RulesRoute)).StatusCode);
            Assert.Equal(denied, (await client.PostAsJsonAsync(RulesRoute, group)).StatusCode);
            Assert.Equal(denied, (await client.PutAsJsonAsync($"{RulesRoute}/1", group)).StatusCode);
            Assert.Equal(denied, (await client.DeleteAsync($"{RulesRoute}/1")).StatusCode);
        }

        foreach (var invalid in new int[][] { [], [0], [first, first], [int.MaxValue], [pending], [rejected] })
        {
            using var response = await admin.PostAsJsonAsync(RulesRoute, new SaveContentVisibilityRuleDto(invalid));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var create = await admin.PostAsJsonAsync(RulesRoute, group);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var rule = (await create.Content.ReadFromJsonAsync<ContentVisibilityRuleDto>())!;
        Assert.Equal(new[] { first, second }, rule.TagIds);
        var route = $"{RulesRoute}/{rule.Id}";
        Assert.EndsWith(route, create.Headers.Location!.AbsolutePath);
        using var duplicate =
            await admin.PostAsJsonAsync(RulesRoute, new SaveContentVisibilityRuleDto([first, second]));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var invalidUpdate = await admin.PutAsJsonAsync(route, new SaveContentVisibilityRuleDto([pending]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
        Assert.Equal(rule.TagIds, (await admin.GetFromJsonAsync<ContentVisibilityRuleDto>(route))!.TagIds);
        using var update = await admin.PutAsJsonAsync(route, new SaveContentVisibilityRuleDto([second]));
        update.EnsureSuccessStatusCode();
        var list = (await admin.GetFromJsonAsync<List<ContentVisibilityRuleDto>>(RulesRoute))!;
        Assert.Equal(new[] { second }, Assert.Single(list, candidate => candidate.Id == rule.Id).TagIds);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreatesOfTheSameUnorderedGroupReturnOneRule()
    {
        var clock = new TestClock();
        await using var app = new ApiFactory(database, clock);
        using var admin = app.Client();
        await AuthenticateAsync(admin, clock, AppUserRole.Admin);
        var first = await AddTagAsync(clock);
        var second = await AddTagAsync(clock);

        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync(RulesRoute, new SaveContentVisibilityRuleDto([first, second])),
            admin.PostAsJsonAsync(RulesRoute, new SaveContentVisibilityRuleDto([second, first])));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order());
    }

    [Fact]
    public async Task ConcurrentEditsSwappingExistingTagGroupsBothConflict()
    {
        var clock = new TestClock();
        await using var app = new ApiFactory(database, clock);
        using var firstAdmin = app.Client();
        using var secondAdmin = app.Client();
        await AuthenticateAsync(firstAdmin, clock, AppUserRole.Admin);
        await AuthenticateAsync(secondAdmin, clock, AppUserRole.Admin);
        var firstTag = await AddTagAsync(clock);
        var secondTag = await AddTagAsync(clock);
        var firstGroup = new SaveContentVisibilityRuleDto([firstTag, secondTag]);
        var secondGroup = new SaveContentVisibilityRuleDto([firstTag]);
        using var firstCreate = await firstAdmin.PostAsJsonAsync(RulesRoute, firstGroup);
        using var secondCreate = await secondAdmin.PostAsJsonAsync(RulesRoute, secondGroup);
        firstCreate.EnsureSuccessStatusCode();
        secondCreate.EnsureSuccessStatusCode();
        var firstRoute = firstCreate.Headers.Location!;
        var secondRoute = secondCreate.Headers.Location!;

        var responses = await Task.WhenAll(
            firstAdmin.PutAsJsonAsync(firstRoute, secondGroup),
            secondAdmin.PutAsJsonAsync(secondRoute, new SaveContentVisibilityRuleDto([secondTag, firstTag])));
        using var firstEdit = responses[0];
        using var secondEdit = responses[1];
        Assert.Equal(HttpStatusCode.Conflict, firstEdit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondEdit.StatusCode);
        Assert.Equal(firstGroup.TagIds,
            (await firstAdmin.GetFromJsonAsync<ContentVisibilityRuleDto>(firstRoute))!.TagIds);
        Assert.Equal(secondGroup.TagIds,
            (await secondAdmin.GetFromJsonAsync<ContentVisibilityRuleDto>(secondRoute))!.TagIds);
    }

    private async Task<AppUserDto> AuthenticateAsync(HttpClient client, TestClock clock, AppUserRole role)
    {
        await using var context = database.CreateContext();
        var authentication = database.Authentication(context, clock);
        var login = "visibility" + Guid.NewGuid().ToString("N");
        var registration = await authentication.RegisterAsync(new(login, Password));
        if (role != AppUserRole.User)
        {
            context.AccountRoles.Add(new AccountRole
            {
                UserId = registration.Response.User.Id,
                RoleId = role == AppUserRole.Admin ? AccessRole.AdminId : AccessRole.ModeratorId
            });
            await context.SaveChangesAsync();
        }

        var session = await authentication.LoginAsync(new(login, Password));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.Response.AccessToken);
        return registration.Response.User;
    }

    private async Task<int> AddTagAsync(TestClock clock, TagModerationStatus status = TagModerationStatus.Approved)
    {
        await using var context = database.CreateContext();
        var name = Guid.NewGuid().ToString("N");
        var tag = new ImageTag
            { Name = name, NormalizedName = name, CreatedAtUtc = clock.GetUtcNow(), ModerationStatus = status };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag.Id;
    }
}
