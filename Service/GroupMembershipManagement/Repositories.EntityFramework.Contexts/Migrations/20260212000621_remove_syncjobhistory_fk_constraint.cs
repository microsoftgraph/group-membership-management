using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class remove_syncjobhistory_fk_constraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobHistory_SyncJobs_SyncJobId",
                table: "SyncJobHistory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobHistory_SyncJobs_SyncJobId",
                table: "SyncJobHistory",
                column: "SyncJobId",
                principalTable: "SyncJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
