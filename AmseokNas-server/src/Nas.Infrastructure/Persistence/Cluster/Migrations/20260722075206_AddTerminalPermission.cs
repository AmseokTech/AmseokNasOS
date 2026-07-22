using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nas.Infrastructure.Persistence.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminalPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[] { "terminal.open", "terminal.open" });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionCode", "RoleId" },
                values: new object[] { "terminal.open", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "terminal.open", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "terminal.open");
        }
    }
}
