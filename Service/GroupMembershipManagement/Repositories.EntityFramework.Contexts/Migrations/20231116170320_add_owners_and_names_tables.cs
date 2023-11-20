using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_owners_and_names_tables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DestinationNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastUpdatedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DestinationNames_SyncJobs_Id",
                        column: x => x.Id,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DestinationOwners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    LastUpdatedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationOwners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DestinationOwnerSyncJob",
                columns: table => new
                {
                    DestinationOwnersId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SyncJobsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationOwnerSyncJob", x => new { x.DestinationOwnersId, x.SyncJobsId });
                    table.ForeignKey(
                        name: "FK_DestinationOwnerSyncJob_DestinationOwners_DestinationOwnersId",
                        column: x => x.DestinationOwnersId,
                        principalTable: "DestinationOwners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationOwnerSyncJob_SyncJobs_SyncJobsId",
                        column: x => x.SyncJobsId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DestinationNames_Name",
                table: "DestinationNames",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationOwners_ObjectId",
                table: "DestinationOwners",
                column: "ObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationOwnerSyncJob_SyncJobsId",
                table: "DestinationOwnerSyncJob",
                column: "SyncJobsId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DestinationNames");

            migrationBuilder.DropTable(
                name: "DestinationOwnerSyncJob");

            migrationBuilder.DropTable(
                name: "DestinationOwners");
        }
    }
}
