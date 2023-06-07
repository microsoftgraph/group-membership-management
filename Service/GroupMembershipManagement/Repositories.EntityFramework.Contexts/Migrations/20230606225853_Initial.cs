using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PartitionKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Requestor = table.Column<string>(maxLength: 255, nullable: false),
                    TargetOfficeGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(maxLength: 255, nullable: true),
                    LastRunTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(1601, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)),
                    LastSuccessfulRunTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(1601, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)),
                    LastSuccessfulStartTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(1601, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)),
                    Period = table.Column<int>(type: "int", nullable: false, defaultValue: 24),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(1601, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)),
                    IgnoreThresholdOnce = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ThresholdPercentageForAdditions = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    ThresholdPercentageForRemovals = table.Column<int>(type: "int", nullable: false, defaultValue: 20),
                    IsDryRunEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DryRunTimeStamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValue: new DateTime(1601, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)),
                    ThresholdViolations = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ETag = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobs", x => x.Id);
                });

            migrationBuilder.AddColumn<DateTime>(
                name: "InitialOnboardingDate",
                table: "SyncJobs",
                defaultValueSql: "getutcdate()");
                
            migrationBuilder.CreateIndex(
                    name: "IDX_SyncJobs_LastRunTime",
                    table: "SyncJobs",
                    column: "LastRunTime");

            migrationBuilder.CreateIndex(
                name: "IDX_SyncJobs_Requestor",
                table: "SyncJobs",
                column: "Requestor");

            migrationBuilder.CreateIndex(
                    name: "IDX_SyncJobs_Status",
                    table: "SyncJobs",
                    column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncJobs");
        }
    }
}
