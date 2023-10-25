using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class email : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SyncJobs",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EmailTypes",
                columns: table => new
                {
                    EmailTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailTypeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailContentTemplateName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTypes", x => x.EmailTypeId);
                });

            migrationBuilder.CreateTable(
                name: "Statuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SortPriority = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statuses", x => x.Id);
                    table.UniqueConstraint("AK_Statuses_Name", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "JobEmailStatuses",
                columns: table => new
                {
                    JobEmailStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTypeId = table.Column<int>(type: "int", nullable: false),
                    DisableEmail = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEmailStatuses", x => x.JobEmailStatusId);
                    table.ForeignKey(
                        name: "FK_JobEmailStatuses_EmailTypes_EmailTypeId",
                        column: x => x.EmailTypeId,
                        principalTable: "EmailTypes",
                        principalColumn: "EmailTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobEmailStatuses_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "EmailTypes",
                columns: new[] { "EmailTypeId", "EmailContentTemplateName", "EmailTypeName" },
                values: new object[] { 1, "SyncStartedEmailBody", "OnBoarding" });

            migrationBuilder.InsertData(
                table: "EmailTypes",
                columns: new[] { "EmailTypeId", "EmailContentTemplateName", "EmailTypeName" },
                values: new object[] { 2, "SyncCompletedEmailBody", "OnBoarding" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_Status",
                table: "SyncJobs",
                column: "Status",
                unique: true,
                filter: "[Status] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_JobEmailStatuses_EmailTypeId",
                table: "JobEmailStatuses",
                column: "EmailTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobEmailStatuses_SyncJobId_EmailTypeId",
                table: "JobEmailStatuses",
                columns: new[] { "SyncJobId", "EmailTypeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_Statuses_Status",
                table: "SyncJobs",
                column: "Status",
                principalTable: "Statuses",
                principalColumn: "Name");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_Statuses_Status",
                table: "SyncJobs");

            migrationBuilder.DropTable(
                name: "JobEmailStatuses");

            migrationBuilder.DropTable(
                name: "Statuses");

            migrationBuilder.DropTable(
                name: "EmailTypes");

            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_Status",
                table: "SyncJobs");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SyncJobs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
