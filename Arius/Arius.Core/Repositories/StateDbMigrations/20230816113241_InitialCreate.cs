using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Repositories.StateDbMigrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BinaryProperties",
                columns: table => new
                {
                    BinaryHash = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchivedLength = table.Column<long>(type: "INTEGER", nullable: false),
                    IncrementalLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinaryProperties", x => x.BinaryHash);
                });

            migrationBuilder.CreateTable(
                name: "PointerFileEntries",
                columns: table => new
                {
                    BinaryHash = table.Column<string>(type: "TEXT", nullable: false),
                    RelativeName = table.Column<string>(type: "TEXT", nullable: false),
                    VersionUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointerFileEntries", x => new { x.BinaryHash, x.RelativeName, x.VersionUtc });
                });

            migrationBuilder.CreateIndex(
                name: "IX_BinaryProperties_BinaryHash",
                table: "BinaryProperties",
                column: "BinaryHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_RelativeName",
                table: "PointerFileEntries",
                column: "RelativeName");

            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_VersionUtc",
                table: "PointerFileEntries",
                column: "VersionUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BinaryProperties");

            migrationBuilder.DropTable(
                name: "PointerFileEntries");
        }
    }
}
