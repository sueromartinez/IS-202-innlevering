using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstWebAppInDocker.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchingNotificationsAndSami : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Resources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Needs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NeedAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    NeedId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SuggestedByAssistant = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeedAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NeedAssignments_Needs_NeedId",
                        column: x => x.NeedId,
                        principalTable: "Needs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NeedAssignments_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Link = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NeedAssignments_NeedId",
                table: "NeedAssignments",
                column: "NeedId");

            migrationBuilder.CreateIndex(
                name: "IX_NeedAssignments_ResourceId",
                table: "NeedAssignments",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_NeedAssignments_Status",
                table: "NeedAssignments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NeedAssignments");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Needs");
        }
    }
}
