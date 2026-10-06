using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class rename_securitygroupnotfound_to_sourcegroupnotfound : Migration
    {
        private const string OldName = "SecurityGroupNotFound";
        private const string NewName = "SourceGroupNotFound";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renames the SyncStatus enum member SecurityGroupNotFound -> SourceGroupNotFound. The persisted
            // integer value (8) is unchanged; only the string name stored in the status catalog and in
            // dependent status-name columns is renamed.
            //
            // Statuses.Name is the principal of FK_SyncJobs_Status (SyncJobs.Status -> Statuses.Name) with
            // NoAction on update, so the FK must be dropped before renaming the principal row, then the
            // dependent SyncJobs.Status rows are updated, then the FK is recreated. SyncJobHistory and
            // PurgedSyncJobs hold archived status-name strings (no FK) and are migrated for back-compat so
            // historical rows continue to resolve to the renamed status.
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_Status",
                table: "SyncJobs");

            migrationBuilder.Sql($"UPDATE [Statuses] SET [Name] = '{NewName}' WHERE [Name] = '{OldName}';");
            migrationBuilder.Sql($"UPDATE [SyncJobs] SET [Status] = '{NewName}' WHERE [Status] = '{OldName}';");
            migrationBuilder.Sql($"UPDATE [SyncJobHistory] SET [Status] = '{NewName}' WHERE [Status] = '{OldName}';");
            migrationBuilder.Sql($"UPDATE [PurgedSyncJobs] SET [Status] = '{NewName}' WHERE [Status] = '{OldName}';");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_Status",
                table: "SyncJobs",
                column: "Status",
                principalTable: "Statuses",
                principalColumn: "Name",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncJobs_Status",
                table: "SyncJobs");

            migrationBuilder.Sql($"UPDATE [PurgedSyncJobs] SET [Status] = '{OldName}' WHERE [Status] = '{NewName}';");
            migrationBuilder.Sql($"UPDATE [SyncJobHistory] SET [Status] = '{OldName}' WHERE [Status] = '{NewName}';");
            migrationBuilder.Sql($"UPDATE [SyncJobs] SET [Status] = '{OldName}' WHERE [Status] = '{NewName}';");
            migrationBuilder.Sql($"UPDATE [Statuses] SET [Name] = '{OldName}' WHERE [Name] = '{NewName}';");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncJobs_Status",
                table: "SyncJobs",
                column: "Status",
                principalTable: "Statuses",
                principalColumn: "Name",
                onDelete: ReferentialAction.NoAction);
        }
    }
}
