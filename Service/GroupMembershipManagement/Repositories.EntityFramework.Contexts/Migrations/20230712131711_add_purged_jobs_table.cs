using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_purged_jobs_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurgedSyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Requestor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetOfficeGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowEmptyDestination = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessfulRunTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessfulStartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IgnoreThresholdOnce = table.Column<bool>(type: "bit", nullable: false),
                    ThresholdPercentageForAdditions = table.Column<int>(type: "int", nullable: false),
                    ThresholdPercentageForRemovals = table.Column<int>(type: "int", nullable: false),
                    IsDryRunEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DryRunTimeStamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ThresholdViolations = table.Column<int>(type: "int", nullable: false),
                    PurgedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgedSyncJobs", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurgedSyncJobs");
        }
    }
}
