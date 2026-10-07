using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261007180000_ClientOperationalPostingSnapshotGuard")]
public sealed class ClientOperationalPostingSnapshotGuard : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder) =>
    migrationBuilder.Sql(ClientOperationalPostingSnapshotGuardSql.Up);

  protected override void Down(MigrationBuilder migrationBuilder) =>
    migrationBuilder.Sql(ClientOperationalPostingSnapshotGuardSql.Down);
}
