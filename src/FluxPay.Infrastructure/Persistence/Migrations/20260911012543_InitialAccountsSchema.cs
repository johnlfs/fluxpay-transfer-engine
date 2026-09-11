using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluxPay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "text", nullable: false),
                    owner_name = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_account_number",
                table: "accounts",
                column: "account_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
