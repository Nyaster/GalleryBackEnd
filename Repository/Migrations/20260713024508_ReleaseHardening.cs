using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CompletedWithErrors",
                table: "ScrapeRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedItems",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "ScrapeRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "RefreshSessions",
                type: "uuid",
                nullable: true);

            // Existing sessions predate families; giving each one its own family preserves their
            // independent logout/expiry semantics instead of putting every old session together.
            migrationBuilder.Sql("UPDATE \"RefreshSessions\" SET \"FamilyId\" = \"Id\" WHERE \"FamilyId\" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "FamilyId",
                table: "RefreshSessions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmbeddingLeaseExpiresAtUtc",
                table: "Images",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeRuns_Status",
                table: "ScrapeRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_FamilyId_ExpiresAtUtc",
                table: "RefreshSessions",
                columns: new[] { "FamilyId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScrapeRuns_Status",
                table: "ScrapeRuns");

            migrationBuilder.DropIndex(
                name: "IX_RefreshSessions_FamilyId_ExpiresAtUtc",
                table: "RefreshSessions");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "CompletedWithErrors",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "FailedItems",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshSessions");

            migrationBuilder.DropColumn(
                name: "EmbeddingLeaseExpiresAtUtc",
                table: "Images");
        }
    }
}
