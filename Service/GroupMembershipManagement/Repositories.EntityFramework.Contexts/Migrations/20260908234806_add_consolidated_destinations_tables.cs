using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_consolidated_destinations_tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Destinations",
                columns: table => new
                {
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinations", x => x.SyncJobId);
                    table.ForeignKey(
                        name: "FK_Destinations_MembershipTypes_DestinationType",
                        column: x => x.DestinationType,
                        principalTable: "MembershipTypes",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Destinations_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupDestinations",
                columns: table => new
                {
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupDestinations", x => x.SyncJobId);
                    table.ForeignKey(
                        name: "FK_GroupDestinations_Destinations_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "Destinations",
                        principalColumn: "SyncJobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamsChannelDestinations",
                columns: table => new
                {
                    SyncJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChannelName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsChannelDestinations", x => x.SyncJobId);
                    table.ForeignKey(
                        name: "FK_TeamsChannelDestinations_Destinations_SyncJobId",
                        column: x => x.SyncJobId,
                        principalTable: "Destinations",
                        principalColumn: "SyncJobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_DestinationType",
                table: "Destinations",
                column: "DestinationType");

            migrationBuilder.CreateIndex(
                name: "IX_GroupDestinations_GroupId",
                table: "GroupDestinations",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamsChannelDestinations_TeamId",
                table: "TeamsChannelDestinations",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupDestinations");

            migrationBuilder.DropTable(
                name: "TeamsChannelDestinations");

            migrationBuilder.DropTable(
                name: "Destinations");
        }
    }
}
