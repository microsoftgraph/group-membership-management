using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.AdfContexts.Migrations
{
    /// <inheritdoc />
    public partial class Initial_AdfRunHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AdfRunHistory",
                schema: "dbo",
                columns: table => new
                {
                    AdfRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdfRunHistory", x => x.AdfRunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdfRunHistory_DateCreated_AdfRunId",
                schema: "dbo",
                table: "AdfRunHistory",
                columns: new[] { "DateCreated", "AdfRunId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdfRunHistory",
                schema: "dbo");
        }
    }
}
