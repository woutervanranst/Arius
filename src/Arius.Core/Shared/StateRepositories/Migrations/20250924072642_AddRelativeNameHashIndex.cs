using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Shared.StateRepositories.Migrations
{
    /// <inheritdoc />
    internal partial class AddRelativeNameHashIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_RelativeName_Hash",
                table: "PointerFileEntries",
                columns: new[] { "RelativeName", "Hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PointerFileEntries_RelativeName_Hash",
                table: "PointerFileEntries");
        }
    }
}
