using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_idempotency",
                columns: table => new
                {
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_idempotency", x => x.idempotency_key);
                    table.CheckConstraint("ck_transfer_idempotency_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transfer_idempotency_completion_consistency", "(transfer_id IS NULL AND completed_at IS NULL) OR (transfer_id IS NOT NULL AND completed_at IS NOT NULL)");
                    table.CheckConstraint("ck_transfer_idempotency_different_accounts", "source_account_id <> destination_account_id");
                    table.ForeignKey(
                        name: "fk_transfer_idempotency_transfer",
                        column: x => x.transfer_id,
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_idempotency_created_at",
                table: "transfer_idempotency",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ux_transfer_idempotency_transfer_id",
                table: "transfer_idempotency",
                column: "transfer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_idempotency");
        }
    }
}
