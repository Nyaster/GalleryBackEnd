using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLikedImagesFeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImageLikes_UserId",
                table: "ImageLikes");

            migrationBuilder.CreateIndex(
                name: "IX_ImageLikes_UserId_CreatedAtUtc_ImageId",
                table: "ImageLikes",
                columns: new[] { "UserId", "CreatedAtUtc", "ImageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImageLikes_UserId_CreatedAtUtc_ImageId",
                table: "ImageLikes");

            migrationBuilder.CreateIndex(
                name: "IX_ImageLikes_UserId",
                table: "ImageLikes",
                column: "UserId");
        }
    }
}
