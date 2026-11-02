using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddForMeFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForMeFeeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AiUsage = table.Column<int[]>(type: "integer[]", nullable: false),
                    ExcludedTags = table.Column<string[]>(type: "text[]", nullable: false),
                    PageSize = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForMeFeeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForMeFeeds_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ForMeFeedEntries",
                columns: table => new
                {
                    FeedId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    ImageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForMeFeedEntries", x => new { x.FeedId, x.Rank });
                    table.ForeignKey(
                        name: "FK_ForMeFeedEntries_ForMeFeeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "ForMeFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ForMeFeedEntries_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForMeFeedEntries_FeedId_ImageId",
                table: "ForMeFeedEntries",
                columns: new[] { "FeedId", "ImageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForMeFeedEntries_ImageId",
                table: "ForMeFeedEntries",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ForMeFeeds_ExpiresAtUtc",
                table: "ForMeFeeds",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ForMeFeeds_UserId_ExpiresAtUtc",
                table: "ForMeFeeds",
                columns: new[] { "UserId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForMeFeedEntries");

            migrationBuilder.DropTable(
                name: "ForMeFeeds");
        }
    }
}
