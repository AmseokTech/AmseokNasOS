using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Nas.Infrastructure.Persistence.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class AddBootstrapAdministratorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6"), "2c7339d4-7a32-4cf2-9884-7ac8440dfe74", "admin", "ADMIN" });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "MustChangePassword", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "SecurityVersion", "TwoFactorEnabled", "UserName" },
                values: new object[] { new Guid("9aeb2b37-3d2a-4f6f-9068-44ca9f20a301"), 0, "3e898478-8913-4472-a9cf-5d5e5d8e15f6", new DateTimeOffset(new DateTime(2026, 7, 18, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, true, null, true, null, "ADMIN", "AQAAAAIAAYagAAAAEHd07/lWDHilbuTDL9jBep584Mmn77geD/ljDTSGiuqv2WR1QYUhMAMSx3P9qas4Kg==", null, false, "0bb934c2-07fa-451d-923f-31b113e8fb6a", 0L, false, "admin" });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[] { new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6"), new Guid("9aeb2b37-3d2a-4f6f-9068-44ca9f20a301") });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionCode", "RoleId" },
                values: new object[,]
                {
                    { "backup.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "backup.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "docker.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "docker.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "logs.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "network.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "network.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "raid.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "service.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "service.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "share.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "share.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "storage.format", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "storage.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "storage.write", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "system.reboot", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "system.shutdown", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "user.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") },
                    { "user.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6"), new Guid("9aeb2b37-3d2a-4f6f-9068-44ca9f20a301") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "backup.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "backup.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "docker.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "docker.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "logs.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "network.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "network.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "raid.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "service.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "service.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "share.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "share.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "storage.format", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "storage.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "storage.write", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "system.reboot", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "system.shutdown", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "user.manage", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionCode", "RoleId" },
                keyValues: new object[] { "user.read", new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6") });

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("3791e91b-bcd4-4f80-8f38-e9d7acb998c6"));

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: new Guid("9aeb2b37-3d2a-4f6f-9068-44ca9f20a301"));

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");
        }
    }
}
