using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceBot.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPatterns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserTelegramId = table.Column<long>(type: "INTEGER", nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    TransactionType = table.Column<string>(type: "TEXT", nullable: true),
                    UseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsed = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPatterns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPatterns_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserPatterns_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserPatterns_Users_UserTelegramId",
                        column: x => x.UserTelegramId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPatterns_AccountId",
                table: "UserPatterns",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPatterns_CategoryId",
                table: "UserPatterns",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPatterns_UserTelegramId",
                table: "UserPatterns",
                column: "UserTelegramId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPatterns");
        }
    }
}
