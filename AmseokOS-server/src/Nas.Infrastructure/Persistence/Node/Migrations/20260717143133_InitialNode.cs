using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nas.Infrastructure.Persistence.Node.Migrations
{
    /// <inheritdoc />
    public partial class InitialNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "NodeStates",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcceptedFencingToken = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeStates", x => x.NodeId);
                });

            migrationBuilder.CreateTable(
                name: "Operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FencingToken = table.Column<long>(type: "INTEGER", nullable: false),
                    Checkpoint = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PublishAttempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "ResourceLocks",
                columns: table => new
                {
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FencingToken = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceLocks", x => x.ResourceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedAt",
                table: "InboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Operations_NodeId_IdempotencyKey",
                table: "Operations",
                columns: new[] { "NodeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operations_Status_CreatedAt",
                table: "Operations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt_OccurredAt",
                table: "OutboxMessages",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLocks_OperationId",
                table: "ResourceLocks",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropTable(
                name: "NodeStates");

            migrationBuilder.DropTable(
                name: "Operations");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ResourceLocks");
        }
    }
}
