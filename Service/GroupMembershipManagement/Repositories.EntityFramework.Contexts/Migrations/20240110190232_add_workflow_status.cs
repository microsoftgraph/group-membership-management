using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_workflow_status : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var pendingReviewId = Guid.Parse("C0F68017-5BDC-4DCC-911E-231C0880E138");
            var submissionRejectedId = Guid.Parse("C1F68017-5BDC-4DCC-911E-231C0880E138");

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { pendingReviewId, "PendingReview", 499 });

            migrationBuilder.InsertData(
                table: "Statuses",
                columns: new[] { "Id", "Name", "SortPriority" },
                values: new object[] { submissionRejectedId, "SubmissionRejected", 501 });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "PendingReview");

            migrationBuilder.DeleteData(
                table: "Statuses",
                keyColumn: "Name",
                keyValue: "SubmissionRejected");
        }
    }
}
