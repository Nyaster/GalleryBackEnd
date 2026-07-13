using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class ScrapeRunProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EligibleCandidates",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProgressAtUtc",
                table: "ScrapeRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxImages",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "PlannedDownloads",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedDownloads",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScannedPages",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalPages",
                table: "ScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EligibleCandidates",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "LastProgressAtUtc",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "MaxImages",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "PlannedDownloads",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "ProcessedDownloads",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "ScannedPages",
                table: "ScrapeRuns");

            migrationBuilder.DropColumn(
                name: "TotalPages",
                table: "ScrapeRuns");
        }
    }
}
