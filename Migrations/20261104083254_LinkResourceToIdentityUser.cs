using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstWebAppInDocker.Migrations
{
    /// <inheritdoc />
    public partial class LinkResourceToIdentityUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(
            MigrationBuilder migrationBuilder
        )
        {
            // =========================
            // RESOURCE OWNER
            // =========================
            //
            // Kobler nye ressurser til den permanente
            // Identity-ID-en i AspNetUsers.
            //
            // Nullable fordi eksisterende ressurser
            // ble opprettet før Identity-koblingen.
            //

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Resources",
                type: "varchar(255)",
                nullable: true
            )
            .Annotation(
                "MySql:CharSet",
                "utf8mb4"
            );


            // =========================
            // NEEDS
            // =========================
            //
            // Needs finnes allerede i dagens database.
            //
            // IF NOT EXISTS gjør at:
            //
            // - eksisterende database beholder tabellen
            // - en ny database fortsatt får Needs
            //

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS `Needs`
                (
                    `Id`
                        int NOT NULL AUTO_INCREMENT,

                    `ResourceType`
                        int NOT NULL,

                    `Priority`
                        int NOT NULL,

                    `Quantity`
                        int NOT NULL,

                    `Description`
                        varchar(500)
                        CHARACTER SET utf8mb4
                        NOT NULL,

                    `Latitude`
                        double NOT NULL,

                    `Longitude`
                        double NOT NULL,

                    `OwnerEmail`
                        varchar(150)
                        CHARACTER SET utf8mb4
                        NOT NULL,

                    `CreatedAt`
                        datetime(6)
                        NOT NULL,

                    CONSTRAINT `PK_Needs`
                        PRIMARY KEY (`Id`)
                )
                CHARACTER SET=utf8mb4;
                """
            );


            // =========================
            // INDEX
            // =========================

            migrationBuilder.CreateIndex(
                name:
                    "IX_Resources_OwnerUserId",
                table:
                    "Resources",
                column:
                    "OwnerUserId"
            );


            // =========================
            // FOREIGN KEY
            // =========================
            //
            // Resources.OwnerUserId
            //          ↓
            // AspNetUsers.Id
            //

            migrationBuilder.AddForeignKey(
                name:
                    "FK_Resources_AspNetUsers_OwnerUserId",
                table:
                    "Resources",
                column:
                    "OwnerUserId",
                principalTable:
                    "AspNetUsers",
                principalColumn:
                    "Id"
            );
        }


        /// <inheritdoc />
        protected override void Down(
            MigrationBuilder migrationBuilder
        )
        {
            migrationBuilder.DropForeignKey(
                name:
                    "FK_Resources_AspNetUsers_OwnerUserId",
                table:
                    "Resources"
            );


            migrationBuilder.DropIndex(
                name:
                    "IX_Resources_OwnerUserId",
                table:
                    "Resources"
            );


            migrationBuilder.DropColumn(
                name:
                    "OwnerUserId",
                table:
                    "Resources"
            );


            // Needs slettes med vilje IKKE her.
            //
            // Tabellen eksisterte før denne migrationen
            // i dagens database og kan inneholde
            // registrerte behov.
            //
            // En rollback av Identity-koblingen skal
            // derfor ikke kunne slette behovsdata.
        }
    }
}