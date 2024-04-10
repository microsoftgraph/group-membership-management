using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_removed_status : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var removedId = Guid.Parse("A262DA5B-52EF-427D-A309-5ED25FFAD891");

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { removedId, "Removed", 1000 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "Removed");
        }
    }
}
