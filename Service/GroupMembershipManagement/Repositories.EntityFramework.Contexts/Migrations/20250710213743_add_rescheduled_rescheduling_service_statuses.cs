
using Microsoft.EntityFrameworkCore.Migrations;
using Models;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class add_rescheduled_rescheduling_service_statuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var tableName = "ServiceStatuses";

            var newStatuses = new Dictionary<Guid, ServiceStatuses>
            {
                { Guid.Parse("7B8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Rescheduled },
                { Guid.Parse("7C8AB321-D03F-EF11-86C3-6045BDC8336C"), ServiceStatuses.Rescheduling }
            };

            foreach (var status in newStatuses)
            {
                migrationBuilder.InsertData(
                    table: tableName,
                    columns: new[] { "Id", "Name" },
                    columnTypes: new[] { "uniqueidentifier", "nvarchar(255)" },
                    values: new object[,]
                    {
                        { status.Key, status.Value.ToString() }
                    });
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ServiceStatuses",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    Guid.Parse("7B8AB321-D03F-EF11-86C3-6045BDC8336C"),
                    Guid.Parse("7C8AB321-D03F-EF11-86C3-6045BDC8336C")
                });
        }
    }
}