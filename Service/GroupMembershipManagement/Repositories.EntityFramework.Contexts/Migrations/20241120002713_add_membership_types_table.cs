// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Migrations;
using Models;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_membership_types_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tableName = "MembershipTypes";
            migrationBuilder.AddColumn<Guid>(
                name: "MembershipType",
                table: "SyncJobs",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: tableName,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipTypes", x => x.Id);
                });

            var membershipTypes = new Dictionary<Guid, MembershipTypes>
            {
                { Guid.Parse("86321DEF-5B06-4354-8D5D-B15A02010324"), MembershipTypes.GroupMembership },
                { Guid.Parse("7FB4429B-A6C0-4EB5-B6FE-5A31ADE3828D"), MembershipTypes.TeamsChannelMembership }
            };

            foreach (var membershipType in membershipTypes)
            {
                migrationBuilder.InsertData(
                    table: tableName,
                    columns: new[] { "Id", "Name" },
                    columnTypes: new[] { "uniqueidentifier", "nvarchar(255)" },
                    values: new object[,]
                    {
                        { membershipType.Key, membershipType.Value.ToString() }
                    });
            }

           migrationBuilder.AddUniqueConstraint(
                name: "AK_MembershipTypes_Name",
                table: tableName,
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_MembershipType",
                table: "SyncJobs",
                column: "MembershipType");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_MembershipTypes_MembershipType",
                table: "SyncJobs",
                column: "MembershipType",
                principalTable: tableName,
                principalColumn: "Name");

            migrationBuilder.Sql(
            $@"
                UPDATE [dbo].[SyncJobs]
                SET MembershipType = CASE
                    WHEN Destination LIKE '%GroupMembership%' THEN 'GroupMembership'
                    WHEN Destination LIKE '%TeamsChannelMembership%' THEN 'TeamsChannelMembership'
                END
                WHERE Destination LIKE '%GroupMembership%' OR Destination LIKE '%TeamsChannelMembership%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_MembershipTypes_MembershipType",
                table: "SyncJobs");

            migrationBuilder.DropTable(
                name: "MembershipTypes");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_MembershipType",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "MembershipType",
                table: "SyncJobs");
        }
    }
}