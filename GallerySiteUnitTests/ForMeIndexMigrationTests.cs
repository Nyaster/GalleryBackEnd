using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Repository.Migrations;

namespace GallerySiteUnitTests;

public sealed class ForMeIndexMigrationTests
{
    [Fact]
    public void Migration_BothDirections_AlterAndConcurrentReindexAreSeparateNontransactionalCommands()
    {
        var migration = new ImproveForMeHnswConstruction();
        AssertCommands(migration.UpOperations, 128);
        AssertCommands(migration.DownOperations, 64);
    }

    private static void AssertCommands(IReadOnlyList<MigrationOperation> operations, int constructionEffort)
    {
        Assert.Equal(2, operations.Count);
        var alter = Assert.IsType<SqlOperation>(operations[0]);
        var rebuild = Assert.IsType<SqlOperation>(operations[1]);
        Assert.True(alter.SuppressTransaction);
        Assert.True(rebuild.SuppressTransaction);
        Assert.Equal($"ALTER INDEX \"IX_Images_Embedding\" SET (m=16, ef_construction={constructionEffort});", alter.Sql);
        Assert.Equal("REINDEX INDEX CONCURRENTLY \"IX_Images_Embedding\";", rebuild.Sql);
    }
}
