using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arius.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    AccountName = table.Column<string>(type: "TEXT", nullable: false),
                    AccountKey = table.Column<string>(type: "TEXT", nullable: false),
                    Passphrase = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: false),
                    RemoveLocal = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tier = table.Column<string>(type: "TEXT", nullable: false),
                    Dedup = table.Column<bool>(type: "INTEGER", nullable: false),
                    FastHash = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupConfigurations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupConfigurations");
        }
    }
}
