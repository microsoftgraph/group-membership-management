using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_allow_empty_destination_column : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowEmptyDestination",
                table: "SyncJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowEmptyDestination",
                table: "SyncJobs");
        }
    }
}
