using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Shared.StateRepositories.Migrations
{
    /// <inheritdoc />
    internal partial class PointerFileEntryKeyIsOnlyRelativeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PointerFileEntries",
                table: "PointerFileEntries");

            migrationBuilder.DropIndex(
                name: "IX_PointerFileEntries_RelativeName",
                table: "PointerFileEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PointerFileEntries",
                table: "PointerFileEntries",
                column: "RelativeName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PointerFileEntries",
                table: "PointerFileEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PointerFileEntries",
                table: "PointerFileEntries",
                columns: new[] { "Hash", "RelativeName" });

            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_RelativeName",
                table: "PointerFileEntries",
                column: "RelativeName");
        }
    }
}
