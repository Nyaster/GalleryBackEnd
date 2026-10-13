using Entities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GallerySiteIntegrationTests;

public sealed class MaterializationCaptureTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task CreateContext_DistinctCollectorsBeyondProviderLimit_ReusesModelAndCapturesEachRead()
    {
        var users = await SeedUsersAsync();
        var models = new List<IModel>();
        for (var i = 0; i < 24; i++)
        {
            var query = new QueryCapture();
            var interaction = new InteractionReadCapture();
            await using var context = database.CreateContext(query, interaction);
            models.Add(context.Model);

            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == users[0]);

            Assert.Same(user, Assert.Single(query.Entities));
            Assert.Same(user, Assert.Single(interaction.Entities));
            Assert.Single(query.Reads);
            Assert.Single(interaction.SelectedColumns);
        }
        Assert.All(models, model => Assert.Same(models[0], model));
    }

    [Fact]
    public async Task CreateContext_ConcurrentCollectors_CapturesOnlyTheirOwnContextEntities()
    {
        var users = await SeedUsersAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = Enumerable.Range(0, 8).Select(async index =>
        {
            var query = new QueryCapture();
            var interaction = new InteractionReadCapture();
            await using var context = database.CreateContext(query, interaction);
            var id = users[index % users.Length];
            await start.Task;

            var user = await context.AppUsers.AsNoTracking().SingleAsync(user => user.Id == id);

            Assert.Equal(id, user.Id);
            Assert.Same(user, Assert.Single(query.Entities));
            Assert.Same(user, Assert.Single(interaction.Entities));
            Assert.Single(query.Reads);
            Assert.Single(interaction.SelectedColumns);
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(reads);
    }

    private async Task<int[]> SeedUsersAsync()
    {
        await using var context = database.CreateContext();
        var prefix = Guid.NewGuid().ToString("N");
        var users = Enumerable.Range(0, 2).Select(index => new AppUser
        {
            Login = $"capture-{prefix}-{index}", NormalizedLogin = $"CAPTURE-{prefix}-{index}",
            PasswordHash = "test-only", CreatedAtUtc = DateTimeOffset.UtcNow
        }).ToArray();
        context.AppUsers.AddRange(users);
        await context.SaveChangesAsync();
        return users.Select(user => user.Id).ToArray();
    }
}
