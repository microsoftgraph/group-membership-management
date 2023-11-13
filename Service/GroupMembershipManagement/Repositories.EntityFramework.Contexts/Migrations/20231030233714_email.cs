using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class email : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "NotificationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Disabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationTypeID = table.Column<int>(type: "int", nullable: false),
                    Disabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_jobNotifications_NotificationTypes_NotificationTypeID",
                        column: x => x.NotificationTypeID,
                        principalTable: "NotificationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_jobNotifications_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "NotificationTypes",
                columns: new[] { "Id", "Name", "Disabled" },
                values: new object[] { 1, "SyncStartedEmailBody", false });

            migrationBuilder.InsertData(
                table: "NotificationTypes",
                columns: new[] { "Id", "Name", "Disabled" },
                values: new object[] { 2, "SyncCompletedEmailBody", false });

            migrationBuilder.CreateIndex(
                name: "IX_jobNotifications_NotificationTypeID",
                table: "JobNotifications",
                column: "NotificationTypeID");

            migrationBuilder.CreateIndex(
                name: "IX_jobNotifications_SyncJobId_NotificationTypeID",
                table: "JobNotifications",
                columns: new[] { "SyncJobId", "NotificationTypeID" },
                unique: true);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropForeignKey(
                name: "FK_jobNotifications_SyncJobs_SyncJobId",
                table: "JobNotifications");
            migrationBuilder.DropForeignKey(
                name: "FK_jobNotifications_NotificationTypes_NotificationTypeID",
                table: "JobNotifications");

            migrationBuilder.DropTable(
                name: "JobNotifications");

            migrationBuilder.DropTable(
                name: "NotificationTypes");
        }
    }
}
