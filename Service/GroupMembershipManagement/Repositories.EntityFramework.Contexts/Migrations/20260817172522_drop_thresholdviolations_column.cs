using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class drop_thresholdviolations_column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThresholdViolations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "ThresholdViolations",
                table: "SyncJobHistory");

            migrationBuilder.DropColumn(
                name: "ThresholdViolations",
                table: "PurgedSyncJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThresholdViolations",
                table: "SyncJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ThresholdViolations",
                table: "SyncJobHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThresholdViolations",
                table: "PurgedSyncJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
