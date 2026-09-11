using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempt_count_non_negative", "attempt_count >= 0");
                    table.CheckConstraint("ck_outbox_messages_event_type_not_empty", "length(event_type) > 0");
                    table.CheckConstraint("ck_outbox_messages_payload_not_empty", "length(payload::text) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                table: "outbox_messages",
                column: "aggregate_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_published_at",
                table: "outbox_messages",
                column: "published_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
