using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Repositories.StateDbMigrations
{
    /// <inheritdoc />
    public partial class AddRemovePointerFileExtensionConverter : Migration
    {
        private const string EXTENSION = ".pointer.arius";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the EFMigrationsHistory table
            migrationBuilder.Sql(@"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" TEXT NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY,
                ""ProductVersion"" TEXT NOT NULL);");

            // Remove the suffix
            migrationBuilder.Sql($"UPDATE PointerFileEntries SET RelativeName = SUBSTR(RelativeName, 0, LENGTH(RelativeName) - {EXTENSION.Length - 1});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add suffix to the 'RelativeName' column of the 'PointerFileEntries' table
            migrationBuilder.Sql($"UPDATE PointerFileEntries SET RelativeName = RelativeName || '{EXTENSION}';");
        }
    }
}
