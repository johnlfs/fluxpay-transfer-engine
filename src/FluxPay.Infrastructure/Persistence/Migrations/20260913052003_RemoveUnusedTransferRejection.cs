using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedTransferRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_status_consistency",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_valid_status",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "transfers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_status_consistency",
                table: "transfers",
                sql: "(status = 'Pending' AND finalized_at IS NULL) OR (status = 'Completed' AND finalized_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_valid_status",
                table: "transfers",
                sql: "status IN ('Pending', 'Completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_status_consistency",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_valid_status",
                table: "transfers");

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "transfers",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_status_consistency",
                table: "transfers",
                sql: "(status = 'Pending' AND finalized_at IS NULL AND rejection_reason IS NULL) OR (status = 'Completed' AND finalized_at IS NOT NULL AND rejection_reason IS NULL) OR (status = 'Rejected' AND finalized_at IS NOT NULL AND rejection_reason IS NOT NULL AND btrim(rejection_reason) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_valid_status",
                table: "transfers",
                sql: "status IN ('Pending', 'Completed', 'Rejected')");
        }
    }
}
