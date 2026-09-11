using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatchable",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "occurred_at", "id" },
                filter: "published_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_next_attempt_state",
                table: "outbox_messages",
                sql: "next_attempt_at IS NULL\nOR (\n    published_at IS NULL\n    AND dead_lettered_at IS NULL\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_terminal_state",
                table: "outbox_messages",
                sql: "NOT (\n    published_at IS NOT NULL\n    AND dead_lettered_at IS NOT NULL\n)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_dispatchable",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_next_attempt_state",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_terminal_state",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");
        }
    }
}
