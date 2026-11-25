using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetUserId = table.Column<int>(type: "integer", nullable: true),
                    RoleId = table.Column<int>(type: "integer", nullable: true),
                    Before = table.Column<string>(type: "text", nullable: true),
                    After = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsProtected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRolePermissions", x => new { x.RoleId, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_AccessRolePermissions_AccessRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AccessRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AccountRoles_AccessRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AccessRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountRoles_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoles_NormalizedName",
                table: "AccessRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountRoles_RoleId",
                table: "AccountRoles",
                column: "RoleId");

            migrationBuilder.Sql("""
                INSERT INTO "AccessRoles" ("Id", "Name", "NormalizedName", "IsProtected")
                VALUES (1, 'Admin', 'ADMIN', TRUE),
                       (2, 'Moderator', 'MODERATOR', FALSE),
                       (3, 'User', 'USER', TRUE),
                       (4, 'Trusted Contributor', 'TRUSTED CONTRIBUTOR', FALSE);
                SELECT setval(pg_get_serial_sequence('"AccessRoles"', 'Id'), 4);

                INSERT INTO "AccessRolePermissions" ("RoleId", "PermissionKey")
                SELECT roles.id, permissions.key
                FROM (VALUES (1), (2)) AS roles(id)
                CROSS JOIN (VALUES ('images.reviewQueue'), ('images.readRestricted'),
                    ('images.moderate'), ('images.classifyAi'), ('images.hideAny'),
                    ('images.restoreAny'), ('images.publishAny'), ('tags.moderate'),
                    ('imageTags.review'), ('imageTags.editAny'), ('comments.deleteAny'),
                    ('comments.manageRestrictions')) AS permissions(key);

                INSERT INTO "AccessRolePermissions" ("RoleId", "PermissionKey")
                VALUES (1, 'announcements.manage'), (1, 'feedback.read'), (1, 'visibilityRules.manage'),
                       (1, 'scraping.run'), (1, 'uploads.manageAccess'), (1, 'uploads.bypassRestrictions'),
                       (1, 'roles.manage'), (4, 'uploads.bypassReview'), (4, 'imageTags.bypassReviewOnOwnImages');

                CREATE FUNCTION gallery_assign_default_role() RETURNS trigger
                    LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO "AccountRoles" ("UserId", "RoleId") VALUES (NEW."Id", 3);
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_AppUsers_AssignDefaultRole"
                    AFTER INSERT ON "AppUsers"
                    FOR EACH ROW EXECUTE FUNCTION gallery_assign_default_role();

                DO $role_migration$
                DECLARE
                    empty_accounts bigint;
                    unknown_accounts bigint;
                BEGIN
                    SELECT count(*) FILTER (WHERE COALESCE(cardinality("roles"), 0) = 0),
                           count(*) FILTER (WHERE EXISTS (
                               SELECT 1 FROM unnest("roles") AS legacy(value)
                               WHERE legacy.value IS NULL OR legacy.value NOT IN (0, 1, 2)))
                    INTO empty_accounts, unknown_accounts
                    FROM "AppUsers";

                    IF empty_accounts > 0 THEN
                        RAISE WARNING 'Role migration assigned baseline User to % accounts with empty legacy roles.',
                            empty_accounts;
                    END IF;
                    IF unknown_accounts > 0 THEN
                        RAISE WARNING 'Role migration ignored unknown legacy role values in % accounts.',
                            unknown_accounts;
                    END IF;
                END;
                $role_migration$;

                INSERT INTO "AccountRoles" ("UserId", "RoleId")
                SELECT "Id", 3 FROM "AppUsers"
                UNION ALL
                SELECT "Id", 1 FROM "AppUsers" WHERE 0 = ANY("roles")
                UNION ALL
                SELECT "Id", 2 FROM "AppUsers" WHERE 1 = ANY("roles");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_AppUsers_AssignDefaultRole" ON "AppUsers";
                DROP FUNCTION gallery_assign_default_role();
                """);

            migrationBuilder.DropTable(
                name: "AccessAudits");

            migrationBuilder.DropTable(
                name: "AccessRolePermissions");

            migrationBuilder.DropTable(
                name: "AccountRoles");

            migrationBuilder.DropTable(
                name: "AccessRoles");
        }
    }
}
