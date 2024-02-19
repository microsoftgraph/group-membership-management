using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_sql_membership_sources_table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SqlMembershipSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SqlMembershipSources", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SqlMembershipSources",
                columns: new[] { "Id", "Attributes", "CustomLabel", "Name" },
                values: new object[] { new Guid("c7b6628a-54c9-4bdd-a451-1f6d6d1d75e6"), null, null, "SqlMembership" });

            migrationBuilder.CreateIndex(
                name: "IX_SqlMembershipSources_Name",
                table: "SqlMembershipSources",
                column: "Name",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SqlMembershipSources");
        }
    }
}
