// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Migrations;
using Models;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class create_servicestatuses_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tableName = "ServiceStatuses";
            migrationBuilder.CreateTable(
            name: tableName,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                Name = table.Column<string>(type: "nvarchar(255)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceStatuses", x => x.Id);
            });

            var statuses = new Dictionary<Guid, ServiceStatuses>
            {
                { Guid.Parse("6B8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Running },
                { Guid.Parse("6C8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Stopped },
                { Guid.Parse("6D8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Resetting },
                { Guid.Parse("6E8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Stopping }
            };

            foreach (var status in statuses)
            {
                migrationBuilder.InsertData(
                    table: tableName,
                    columns: new[] { "Id", "Name" },
                    columnTypes: new[] { "uniqueidentifier", "nvarchar(255)" },
                    values: new object[,]
                    {
                        { status.Key, status.Value.ToString() }
                    });
            }

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ServiceStatuses_Name",
                table: tableName,
                column: "Name");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(name: "AK_ServiceStatuses_Name", table: "ServiceStatuses");
            migrationBuilder.DropTable(name: "ServiceStatuses");
        }
    }
}
