using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repositories.EntityFramework.Contexts.Migrations
{
    public partial class rename_setting_columns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_Key",
                table: "Settings");
            migrationBuilder.RenameColumn(
                name: "Key",
                table: "Settings",
                newName: "SettingKey");
            migrationBuilder.RenameColumn(
                name: "Value",
                table: "Settings",
                newName: "SettingValue");
            migrationBuilder.CreateIndex(
                name: "IX_Settings_SettingKey",
                table: "Settings",
                column: "SettingKey",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_SettingKey",
                table: "Settings");
            migrationBuilder.RenameColumn(
                name: "SettingKey",
                table: "Settings",
                newName: "Key");
            migrationBuilder.RenameColumn(
                name: "SettingValue",
                table: "Settings",
                newName: "Value");
            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key");
        }
    }
}
