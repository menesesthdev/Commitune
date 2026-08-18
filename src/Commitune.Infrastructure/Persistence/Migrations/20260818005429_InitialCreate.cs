using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commitune.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    GithubLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProtectedGithubToken = table.Column<string>(type: "text", nullable: true),
                    RepositoryOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RepositoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bot_users_TelegramUserId",
                table: "bot_users",
                column: "TelegramUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_users");
        }
    }
}
