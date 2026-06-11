// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_groups_and_teams_channels_tables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.SyncJobId);
                    table.ForeignKey(
                        name: "FK_Groups_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamsChannels",
                columns: table => new
                {
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(255)", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsChannels", x => x.SyncJobId);
                    table.ForeignKey(
                        name: "FK_TeamsChannels_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_SyncJobId_GroupId",
                table: "Groups",
                columns: new[] { "SyncJobId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamsChannels_SyncJobId_GroupId",
                table: "TeamsChannels",
                columns: new[] { "SyncJobId", "GroupId" },
                unique: true);

            migrationBuilder.Sql(
            $@"
                INSERT INTO [dbo].[Groups] (SyncJobId, GroupId)
                SELECT Id, TargetOfficeGroupId
                FROM [dbo].[SyncJobs]
                WHERE MembershipType = 'GroupMembership';
            ");

            migrationBuilder.Sql(
            $@"
                INSERT INTO [dbo].[TeamsChannels] (SyncJobId, GroupId, ChannelId)
                SELECT Id, TargetOfficeGroupId, JSON_VALUE(Destination, '$[0].value.channelId')
                FROM [dbo].[SyncJobs]
                WHERE MembershipType = 'TeamsChannelMembership';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "TeamsChannels");
        }
    }
}