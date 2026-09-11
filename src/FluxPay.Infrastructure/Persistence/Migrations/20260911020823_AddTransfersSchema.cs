using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.id);
                    table.CheckConstraint("ck_transfers_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transfers_different_accounts", "source_account_id <> destination_account_id");
                    table.CheckConstraint("ck_transfers_status_consistency", "(status = 'Pending' AND finalized_at IS NULL AND rejection_reason IS NULL) OR (status = 'Completed' AND finalized_at IS NOT NULL AND rejection_reason IS NULL) OR (status = 'Rejected' AND finalized_at IS NOT NULL AND rejection_reason IS NOT NULL AND btrim(rejection_reason) <> '')");
                    table.CheckConstraint("ck_transfers_valid_status", "status IN ('Pending', 'Completed', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_transfers_destination_account",
                        column: x => x.destination_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfers_source_account",
                        column: x => x.source_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_created_at",
                table: "transfers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_destination_account_id",
                table: "transfers",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_source_account_id",
                table: "transfers",
                column: "source_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfers");
        }
    }
}
