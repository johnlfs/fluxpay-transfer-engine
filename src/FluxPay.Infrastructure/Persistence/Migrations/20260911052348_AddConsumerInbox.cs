using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_inbox_messages",
                columns: table => new
                {
                    consumer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumer_inbox_messages", x => new { x.consumer_name, x.message_id });
                    table.CheckConstraint("ck_consumer_inbox_messages_consumer_name_not_empty", "length(consumer_name) > 0");
                    table.CheckConstraint("ck_consumer_inbox_messages_event_type_not_empty", "length(event_type) > 0");
                    table.CheckConstraint("ck_consumer_inbox_messages_processed_after_received", "processed_at IS NULL\nOR processed_at >= received_at");
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_messages_message_id",
                table: "consumer_inbox_messages",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_consumer_inbox_messages_processed_at",
                table: "consumer_inbox_messages",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_inbox_messages");
        }
    }
}
