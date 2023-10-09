// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Migrations;
using Models;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_status_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Statuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", nullable: true),
                    SortPriority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statuses", x => x.Id);
                });

            var defaultSortPriority = 1000;
            var statusWithActionRequired = new List<SyncStatus>
            {
                SyncStatus.CustomerPaused,
                SyncStatus.DestinationGroupNotFound,
                SyncStatus.MembershipDataNotFound,
                SyncStatus.NotOwnerOfDestinationGroup,
                SyncStatus.SecurityGroupNotFound,
                SyncStatus.ThresholdExceeded
            };

            foreach (SyncStatus status in Enum.GetValues<SyncStatus>().OrderBy(x => x))
            {
                migrationBuilder.InsertData(
                    table: "Statuses",
                    columns: new[] { "Id", "Name", "SortPriority" },
                    columnTypes: new[] { "int", "nvarchar(255)", "int" },
                    values: new object[,]
                    {
                        { (int)status, status.ToString(), statusWithActionRequired.Contains(status)? 500 : defaultSortPriority }
                    });
            }

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Status_Name",
                table: "Statuses",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_Status",
                table: "SyncJobs",
                column: "Status",
                principalTable: "Statuses",
                principalColumn: "Name",
                onDelete: ReferentialAction.NoAction);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_SyncJobs_Status", table: "SyncJobs");
            migrationBuilder.DropUniqueConstraint(name: "AK_Status_Name", table: "Statuses");
            migrationBuilder.DropTable(name: "Statuses");
        }

    }
}
