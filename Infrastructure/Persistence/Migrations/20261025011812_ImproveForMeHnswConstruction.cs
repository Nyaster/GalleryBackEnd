using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations;

public partial class ImproveForMeHnswConstruction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Always rebuild, including retries after ALTER succeeded but REINDEX failed.
        migrationBuilder.Sql("ALTER INDEX \"IX_Images_Embedding\" SET (m=16, ef_construction=128);", suppressTransaction: true);
        migrationBuilder.Sql("REINDEX INDEX CONCURRENTLY \"IX_Images_Embedding\";", suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER INDEX \"IX_Images_Embedding\" SET (m=16, ef_construction=64);", suppressTransaction: true);
        migrationBuilder.Sql("REINDEX INDEX CONCURRENTLY \"IX_Images_Embedding\";", suppressTransaction: true);
    }
}
