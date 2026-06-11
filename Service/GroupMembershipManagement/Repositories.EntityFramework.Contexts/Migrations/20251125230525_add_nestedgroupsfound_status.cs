using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_nestedgroupsfound_status : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var nestedGroupsFoundId = Guid.Parse("E2F68017-5BDC-4DCC-911E-231C0880E138");

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { nestedGroupsFoundId, "NestedGroupsFound", 500 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "NestedGroupsFound");
        }
    }
}