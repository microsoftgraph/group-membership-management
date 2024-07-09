using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class threshold_notification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThresholdNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TargetOfficeGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ThresholdPercentageForAdditions = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    ThresholdPercentageForRemovals = table.Column<int>(type: "int", nullable: false, defaultValue: 20),
                    ChangePercentageForAdditions = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    ChangePercentageForRemovals = table.Column<double>(type: "float", nullable: false, defaultValue: 0.0),
                    ChangeQuantityForAdditions = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ChangeQuantityForRemovals = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ResolvedTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ResolvedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LastUpdatedTime = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ResolutionName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CardStateName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThresholdNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThresholdNotifications_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThresholdNotifications_SyncJobId",
                table: "ThresholdNotifications",
                column: "SyncJobId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThresholdNotifications");

        }
    }
}
