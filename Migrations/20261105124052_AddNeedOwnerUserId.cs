using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstWebAppInDocker.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedOwnerUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Needs",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Needs_OwnerUserId",
                table: "Needs",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Needs_AspNetUsers_OwnerUserId",
                table: "Needs",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Needs_AspNetUsers_OwnerUserId",
                table: "Needs");

            migrationBuilder.DropIndex(
                name: "IX_Needs_OwnerUserId",
                table: "Needs");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Needs");
        }
    }
}
