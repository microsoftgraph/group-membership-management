using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_scheduleddate_property : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SqlMembershipSources",
                keyColumn: "Id",
                keyValue: new Guid("c7b6628a-54c9-4bdd-a451-1f6d6d1d75e6"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDate",
                table: "SyncJobs",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "SyncJobChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(2024, 4, 8, 19, 7, 33, 472, DateTimeKind.Utc).AddTicks(8183)),
                    ChangedByDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeSource = table.Column<int>(type: "int", nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeDetails = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobChanges", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SqlMembershipSources",
                columns: new[] { "Id", "Attributes", "CustomLabel", "Name" },
                values: new object[] { new Guid("e10dbc23-84d2-4843-b020-d5e2698a0f7a"), null, null, "SqlMembership" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobChanges_ChangedByObjectId",
                table: "SyncJobChanges",
                column: "ChangedByObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobChanges_ChangeSource",
                table: "SyncJobChanges",
                column: "ChangeSource");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobChanges_ChangeTime",
                table: "SyncJobChanges",
                column: "ChangeTime");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobChanges_SyncJobId",
                table: "SyncJobChanges",
                column: "SyncJobId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncJobChanges");

            migrationBuilder.DeleteData(
                table: "SqlMembershipSources",
                keyColumn: "Id",
                keyValue: new Guid("e10dbc23-84d2-4843-b020-d5e2698a0f7a"));

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "SyncJobs");

            migrationBuilder.InsertData(
                table: "SqlMembershipSources",
                columns: new[] { "Id", "Attributes", "CustomLabel", "Name" },
                values: new object[] { new Guid("c7b6628a-54c9-4bdd-a451-1f6d6d1d75e6"), null, null, "SqlMembership" });
        }
    }
}
