using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_before_after_sync_user_count_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BeforeSyncUserCount",
                table: "SyncJobHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AfterSyncUserCount",
                table: "SyncJobHistory",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BeforeSyncUserCount",
                table: "SyncJobHistory");

            migrationBuilder.DropColumn(
                name: "AfterSyncUserCount",
                table: "SyncJobHistory");
        }
    }
}
