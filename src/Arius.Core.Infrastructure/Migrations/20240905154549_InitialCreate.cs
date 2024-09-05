using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Infrastructure.Migrations
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
                    Hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    OriginalLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ArchivedLength = table.Column<long>(type: "INTEGER", nullable: false),
                    IncrementalLength = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageTier = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinaryProperties", x => x.Hash);
                });

            migrationBuilder.CreateTable(
                name: "PointerFileEntries",
                columns: table => new
                {
                    Hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RelativeName = table.Column<string>(type: "TEXT", nullable: false),
                    CreationTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointerFileEntries", x => new { x.Hash, x.RelativeName });
                    table.ForeignKey(
                        name: "FK_PointerFileEntries_BinaryProperties_Hash",
                        column: x => x.Hash,
                        principalTable: "BinaryProperties",
                        principalColumn: "Hash",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BinaryProperties_Hash",
                table: "BinaryProperties",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_Hash",
                table: "PointerFileEntries",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_PointerFileEntries_RelativeName",
                table: "PointerFileEntries",
                column: "RelativeName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PointerFileEntries");

            migrationBuilder.DropTable(
                name: "BinaryProperties");
        }
    }
}
