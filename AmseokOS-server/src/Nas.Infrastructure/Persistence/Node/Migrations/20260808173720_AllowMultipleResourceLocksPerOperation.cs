using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nas.Infrastructure.Persistence.Node.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleResourceLocksPerOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceLocks_OperationId",
                table: "ResourceLocks");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLocks_OperationId",
                table: "ResourceLocks",
                column: "OperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResourceLocks_OperationId",
                table: "ResourceLocks");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLocks_OperationId",
                table: "ResourceLocks",
                column: "OperationId",
                unique: true);
        }
    }
}
