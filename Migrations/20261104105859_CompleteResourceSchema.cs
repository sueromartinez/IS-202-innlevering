using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FirstWebAppInDocker.Migrations
{
    /// <inheritdoc />
    public partial class CompleteResourceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(
            MigrationBuilder migrationBuilder
        )
        {
            // =========================
            // COMPLETE RESOURCE SCHEMA
            // =========================
            //
            // Eldre utviklingsdatabaser kan allerede
            // inneholde disse kolonnene.
            //
            // Nye databaser mangler dem fordi den
            // opprinnelige AddIdentity-migrasjonen
            // opprettet en eldre versjon av Resources.
            //
            // IF NOT EXISTS gjør derfor migrasjonen
            // trygg i begge situasjoner.
            //


            migrationBuilder.Sql(
                """
                ALTER TABLE `Resources`
                ADD COLUMN IF NOT EXISTS `ResourceName`
                    varchar(100)
                    CHARACTER SET utf8mb4
                    NOT NULL
                    DEFAULT '';
                """
            );


            migrationBuilder.Sql(
                """
                ALTER TABLE `Resources`
                ADD COLUMN IF NOT EXISTS `Quantity`
                    int
                    NOT NULL
                    DEFAULT 1;
                """
            );


            migrationBuilder.Sql(
                """
                ALTER TABLE `Resources`
                ADD COLUMN IF NOT EXISTS `Capacity`
                    varchar(200)
                    CHARACTER SET utf8mb4
                    NULL;
                """
            );


            migrationBuilder.Sql(
                """
                ALTER TABLE `Resources`
                ADD COLUMN IF NOT EXISTS `Availability`
                    varchar(200)
                    CHARACTER SET utf8mb4
                    NULL;
                """
            );


            migrationBuilder.Sql(
                """
                ALTER TABLE `Resources`
                ADD COLUMN IF NOT EXISTS `Address`
                    varchar(200)
                    CHARACTER SET utf8mb4
                    NULL;
                """
            );
        }


        /// <inheritdoc />
        protected override void Down(
            MigrationBuilder migrationBuilder
        )
        {
            // Kolonnene slettes med vilje IKKE.
            //
            // Enkelte utviklingsdatabaser hadde disse
            // kolonnene allerede før denne migrasjonen.
            //
            // En rollback skal derfor ikke kunne slette
            // ResourceName, Quantity, Capacity,
            // Availability eller Address og dermed
            // risikere tap av eksisterende ressursdata.
        }
    }
}