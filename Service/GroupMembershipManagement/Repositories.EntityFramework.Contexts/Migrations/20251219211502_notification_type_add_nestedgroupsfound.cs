using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class notification_type_add_nestedgroupsfound : Migration
    {
        /// <inheritdoc />       
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "NotificationTypes",
                columns: new[] { "Id", "Disabled", "Name" },
                values: new object[] { 15, false, "NestedGroupsFoundNotification" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "NotificationTypes",
                keyColumn: "Id",
                keyValue: 15);
        }
    }
}