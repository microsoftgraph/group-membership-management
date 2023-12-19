using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class rename_setting_columns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Key",
                table: "Settings",
                newName: "SettingKey");
            migrationBuilder.RenameColumn(
                name: "Value",
                table: "Settings",
                newName: "SettingValue");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SettingKey",
                table: "Settings",
                newName: "Key");
            migrationBuilder.RenameColumn(
                name: "SettingValue",
                table: "Settings",
                newName: "Value");
        }
    }
}
