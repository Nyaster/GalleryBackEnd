using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageViewBatchReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageViewBatchReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImageViewCounters",
                columns: table => new
                {
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    TotalViews = table.Column<long>(type: "bigint", nullable: false),
                    UniqueRegisteredViewers = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageViewCounters", x => x.ImageId);
                    table.ForeignKey(
                        name: "FK_ImageViewCounters_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImageViewFirstSeen",
                columns: table => new
                {
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageViewFirstSeen", x => new { x.ImageId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ImageViewFirstSeen_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageViewBatchReceipts_CreatedAtUtc",
                table: "ImageViewBatchReceipts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImageViewFirstSeen_ImageId_SeenAtUtc",
                table: "ImageViewFirstSeen",
                columns: new[] { "ImageId", "SeenAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageViewBatchReceipts");

            migrationBuilder.DropTable(
                name: "ImageViewCounters");

            migrationBuilder.DropTable(
                name: "ImageViewFirstSeen");
        }
    }
}
