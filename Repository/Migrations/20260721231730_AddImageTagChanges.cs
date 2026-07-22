using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddImageTagChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageTagChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    PreviousTags = table.Column<List<string>>(type: "text[]", nullable: false),
                    ProposedTags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EditedByUserId = table.Column<int>(type: "integer", nullable: true),
                    EditedByLogin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedByLogin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RevertedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RevertedByLogin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevertedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageTagChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageTagChanges_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageTagChanges_ImageId_CreatedAtUtc_Id",
                table: "ImageTagChanges",
                columns: new[] { "ImageId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageTagChanges_Status_CreatedAtUtc_Id",
                table: "ImageTagChanges",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ImageTagChanges_PendingImage",
                table: "ImageTagChanges",
                column: "ImageId",
                unique: true,
                filter: "\"Status\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageTagChanges");
        }
    }
}
