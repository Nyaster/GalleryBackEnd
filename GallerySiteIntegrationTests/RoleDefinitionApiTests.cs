using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Shared.DataTransferObjects;

namespace GallerySiteIntegrationTests;

public sealed class RoleDefinitionApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RoleDefinitionUpdatesRequireCurrentVersionAndRejectConcurrentOverwrite()
    {
        await using var api = await RoleAssignmentTestApi.StartAsync(fixture);
        const string path = "/api/admin/access/roles";
        const string name = "Custom publisher";
        using var created = await api.Manager.PostAsJsonAsync(path,
            new ChangeAccessRoleDto(name, [AccessPermission.ImagesReviewQueue]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = (await created.Content.ReadFromJsonAsync<AccessRoleDto>())!;
        Assert.False(string.IsNullOrWhiteSpace(role.Version));
        var rolePath = $"{path}/{role.Id}";

        using var missingVersion = await api.Manager.PutAsJsonAsync(rolePath,
            new ChangeAccessRoleDto(name, [AccessPermission.ImagesPublishAny]));
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        using var blankVersion = await api.Manager.PutAsJsonAsync(rolePath,
            new UpdateAccessRoleDto(name, [AccessPermission.ImagesPublishAny], " "));
        Assert.Equal(HttpStatusCode.BadRequest, blankVersion.StatusCode);

        using var assignment = await api.SetRolesAsync(api.MemberId, new { roleIds = new[] { role.Id } });
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        var listed = Assert.Single((await api.Manager.GetFromJsonAsync<AccessRoleDto[]>(path))!,
            entry => entry.Id == role.Id);
        Assert.Equal(1, listed.MemberCount);
        Assert.Equal(role.Version, listed.Version);

        var requests = await Task.WhenAll(
            api.Manager.PutAsJsonAsync(rolePath,
                new UpdateAccessRoleDto(name, [AccessPermission.ImagesPublishAny], role.Version)),
            api.Manager.PutAsJsonAsync(rolePath,
                new UpdateAccessRoleDto(name, [], role.Version)));
        using var first = requests[0];
        using var second = requests[1];
        var success = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.OK);
        var stale = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.Conflict);
        var updated = (await success.Content.ReadFromJsonAsync<AccessRoleDto>())!;
        Assert.Equal(name, updated.Name);
        Assert.Equal(1, updated.MemberCount);
        Assert.NotEqual(role.Version, updated.Version);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:lilgallery:problem:role-definition-changed", problem.GetProperty("type").GetString());
        Assert.False(problem.TryGetProperty("permissions", out _));

        using var noOp = await api.Manager.PutAsJsonAsync(rolePath,
            new UpdateAccessRoleDto(name, updated.Permissions, updated.Version));
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.Equal(updated.Version, (await noOp.Content.ReadFromJsonAsync<AccessRoleDto>())!.Version);
        listed = Assert.Single((await api.Manager.GetFromJsonAsync<AccessRoleDto[]>(path))!,
            entry => entry.Id == role.Id);
        Assert.Equal(updated.Version, listed.Version);
        Assert.Equal(updated.Permissions, listed.Permissions);

        await using var database = fixture.CreateContext();
        Assert.Equal(1, await database.AccessAudits.CountAsync(entry =>
            entry.RoleId == role.Id && entry.Action == "updateRole"));
    }
}
