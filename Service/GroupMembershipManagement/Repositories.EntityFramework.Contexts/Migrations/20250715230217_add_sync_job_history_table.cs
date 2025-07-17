using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_sync_job_history_table : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncJobHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    UsersAdded = table.Column<int>(type: "int", nullable: true),
                    UsersRemoved = table.Column<int>(type: "int", nullable: true),
                    ThresholdViolations = table.Column<int>(type: "int", nullable: true),
                    UpdatedByFunction = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncJobHistory_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobHistory_SyncJobId",
                table: "SyncJobHistory",
                column: "SyncJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobHistory_RunId",
                table: "SyncJobHistory",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobHistory_EndTime",
                table: "SyncJobHistory",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                    name: "IX_SyncJobHistory_CreatedAt",
                    table: "SyncJobHistory",
                    column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobHistory_UpdatedAt",
                table: "SyncJobHistory",
                column: "UpdatedAt");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SyncJobHistory");
        }
    }
}
