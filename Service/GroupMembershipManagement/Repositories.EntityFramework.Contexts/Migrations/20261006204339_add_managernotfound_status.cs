using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_managernotfound_status : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var managerNotFoundId = Guid.Parse("F00B8151-136F-4174-8196-94DE63DC7B4C");

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { managerNotFoundId, "ManagerNotFound", 500 });

            migrationBuilder.InsertData(
                table: "NotificationTypes",
                columns: new[] { "Id", "Disabled", "Name" },
                values: new object[] { 16, false, "ManagerNotExistNotification" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "NotificationTypes",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "ManagerNotFound");
        }
    }
}
