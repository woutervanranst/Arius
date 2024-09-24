using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncrementalLength",
                table: "BinaryProperties");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IncrementalLength",
                table: "BinaryProperties",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
