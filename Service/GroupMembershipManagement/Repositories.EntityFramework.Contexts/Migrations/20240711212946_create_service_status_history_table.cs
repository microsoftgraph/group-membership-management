// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class create_service_status_history_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tableName = "ServiceStatusHistory";
            migrationBuilder.CreateTable(
            name: tableName,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                ServiceStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceStatusHistory", x => x.Id);
            });

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceStatusHistory_ServiceStatuses",
                table: tableName,
                column: "ServiceStatusId",
                principalTable: "ServiceStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.InsertData(
                   table: tableName,
                   columns: new[] { "ServiceStatusId", "RequestorObjectId" },
                   columnTypes: new[] { "uniqueidentifier", "uniqueidentifier" },
                   values: new object[,]
                   {
                       { Guid.Parse("6B8AB321-D03F-EF11-86C3-6045BDC8336C"), Guid.Empty } // Adds a row with the Running status
                   });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_ServiceStatusHistory_ServiceStatuses", table: "ServiceStatusHistory");
            migrationBuilder.DropTable(name: "ServiceStatusHistory");
        }
    }
}
