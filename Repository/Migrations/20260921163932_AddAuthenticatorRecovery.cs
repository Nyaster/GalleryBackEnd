using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticatorRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthenticationVersion",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthenticatorAttemptWindowUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthenticatorEnabledAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthenticatorFailedAttempts",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "AuthenticatorLastUsedStep",
                table: "AppUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthenticatorLockedUntilUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthenticatorSecret",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthenticatorSetupExpiresAtUtc",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AuthenticatorSetupId",
                table: "AppUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingAuthenticatorSecret",
                table: "AppUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UploadsBlocked",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthenticationVersion",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorAttemptWindowUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorEnabledAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorFailedAttempts",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorLastUsedStep",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorLockedUntilUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorSecret",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorSetupExpiresAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "AuthenticatorSetupId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PendingAuthenticatorSecret",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "UploadsBlocked",
                table: "AppUsers");
        }
    }
}
