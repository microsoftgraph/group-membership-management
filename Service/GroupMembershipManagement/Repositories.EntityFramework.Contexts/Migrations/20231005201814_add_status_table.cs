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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
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

            var statuses = new Dictionary<Guid, SyncStatus>
            {
                { Guid.Parse("A23604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.All },
                { Guid.Parse("A33604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.CustomerPaused },
                { Guid.Parse("A43604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.DestinationGroupNotFound },
                { Guid.Parse("A53604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.DestinationQueryNotValid },
                { Guid.Parse("A63604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.DeveloperPaused },
                { Guid.Parse("A73604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.Error },
                { Guid.Parse("A83604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.ErroredDueToStuckInProgress },
                { Guid.Parse("A93604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.FileNotFound },
                { Guid.Parse("AA3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.FilePathNotValid },
                { Guid.Parse("AB3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup },
                { Guid.Parse("AC3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.Idle },
                { Guid.Parse("AD3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.InProgress },
                { Guid.Parse("AE3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.MembershipDataNotFound },
                { Guid.Parse("AF3604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.New },
                { Guid.Parse("B03604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.NotOwnerOfDestinationGroup },
                { Guid.Parse("B13604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.QueryNotValid },
                { Guid.Parse("B23604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.SchemaError },
                { Guid.Parse("B33604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.SecurityGroupNotFound },
                { Guid.Parse("B43604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.StandardTeamsChannel },
                { Guid.Parse("B53604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.StuckInProgress },
                { Guid.Parse("B63604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.TeamsChannelError },
                { Guid.Parse("B73604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.TeamsChannelNotDestination },
                { Guid.Parse("B83604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.ThresholdExceeded },
                { Guid.Parse("B93604F9-5869-EE11-9937-6045BDE913DD"), SyncStatus.TransientError }
            };

            foreach (var status in statuses)
            {
                migrationBuilder.InsertData(
                    table: "Statuses",
                    columns: new[] { "Id", "Name", "SortPriority" },
                    columnTypes: new[] { "uniqueidentifier", "nvarchar(255)", "int" },
                    values: new object[,]
                    {
                        { status.Key, status.Value.ToString(), statusWithActionRequired.Contains(status.Value)? 500 : defaultSortPriority }
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
