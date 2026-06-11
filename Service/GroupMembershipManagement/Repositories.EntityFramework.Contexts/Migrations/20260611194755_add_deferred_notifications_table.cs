using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_deferred_notifications_table : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeferredNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    MessageType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DeferredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MessageExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplayedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuppressionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeferredNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeferredNotifications_MessageType_Status",
                table: "DeferredNotifications",
                columns: new[] { "MessageType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeferredNotifications_SequenceNumber",
                table: "DeferredNotifications",
                column: "SequenceNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeferredNotifications");
        }
    }
}
