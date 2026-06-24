using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    /// <inheritdoc />
    public partial class add_pendingautoapproval_status : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var pendingAutoApprovalId = Guid.Parse("C3F68017-5BDC-4DCC-911E-231C0880E138");

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { pendingAutoApprovalId, "PendingAutoApproval", 499 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "PendingAutoApproval");
        }
    }
}
