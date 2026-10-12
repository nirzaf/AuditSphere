using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QatarSundayWeekStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations");

            // The firm week now starts on Sunday (Qatar: Sunday to Thursday working week). A stored Monday-start bucket moves to
            // the Sunday-start week that contains that Monday, which is the day before. Every stored week moves by the same one
            // day, so the unique (firm, engagement, user, week) identity is preserved and no two rows can meet. Committed
            // planning receipts are historical evidence and keep the week they recorded.
            migrationBuilder.Sql("""
                UPDATE staff_allocations SET week_start = week_start - 1 WHERE extract(isodow from week_start) = 1;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations",
                sql: "planned_minutes BETWEEN 1 AND 4800 AND extract(dow from week_start) = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations");

            migrationBuilder.Sql("""
                UPDATE staff_allocations SET week_start = week_start + 1 WHERE extract(dow from week_start) = 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_staff_allocation_values",
                table: "staff_allocations",
                sql: "planned_minutes BETWEEN 1 AND 4800 AND extract(isodow from week_start) = 1");
        }
    }
}
