using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Core.Repositories.StateDbMigrations
{
    /// <inheritdoc />
    public partial class ChangeHashToBlob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Hash0",
                table: "BinaryProperties",
                type: "BLOB",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE BinaryProperties SET Hash0 = CAST(hex(BinaryHash) AS BLOB);");

            //migrationBuilder.DropColumn("BinaryHash", table: "BinaryProperties");


            // set nullable false

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
