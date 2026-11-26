using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientFiscalCalendarMonthDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_accounting_profile_values",
                table: "client_accounting_profiles");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_accounting_profile_values",
                table: "client_accounting_profiles",
                sql: "length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND CASE WHEN fiscal_year_start_month = 2 THEN 29 WHEN fiscal_year_start_month IN (4, 6, 9, 11) THEN 30 ELSE 31 END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_client_accounting_profile_values",
                table: "client_accounting_profiles");

            migrationBuilder.AddCheckConstraint(
                name: "ck_client_accounting_profile_values",
                table: "client_accounting_profiles",
                sql: "length(trim(jurisdiction)) > 0 AND functional_currency ~ '^[A-Z]{3}$' AND fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31");
        }
    }
}
