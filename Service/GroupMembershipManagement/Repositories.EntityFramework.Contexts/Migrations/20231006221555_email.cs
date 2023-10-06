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
                name: "EmailTypes",
                columns: table => new
                {
                    EmailTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmailTypeName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTypes", x => x.EmailTypeId);
                });

            migrationBuilder.CreateTable(
                name: "JobEmailStatuses",
                columns: table => new
                {
                    JobEmailStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTypeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<bool>(type: "bit", nullable: false)
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
                columns: new[] { "EmailTypeId", "EmailTypeName" },
                values: new object[] { 1, "OnBoarding" });

            migrationBuilder.CreateIndex(
                name: "IX_JobEmailStatuses_EmailTypeId",
                table: "JobEmailStatuses",
                column: "EmailTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobEmailStatuses_SyncJobId",
                table: "JobEmailStatuses",
                column: "SyncJobId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobEmailStatuses");

            migrationBuilder.DropTable(
                name: "EmailTypes");
        }
    }
}
