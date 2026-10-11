using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSiteWorkerHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_site_worker_heartbeats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    worker_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deployment_epoch = table.Column<long>(type: "bigint", nullable: false),
                    readiness_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    diagnostic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_site_worker_heartbeats", x => x.id);
                    table.CheckConstraint("ck_client_site_worker_heartbeat_state", "worker_group = 'client-sites' AND deployment_epoch >= 1 AND readiness_state IN ('READY','DISABLED','BLOCKED_CONFIGURATION')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_site_worker_heartbeat_recent",
                table: "client_site_worker_heartbeats",
                columns: new[] { "firm_id", "worker_group", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ux_client_site_worker_heartbeat_instance",
                table: "client_site_worker_heartbeats",
                columns: new[] { "firm_id", "worker_group", "worker_instance_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_site_worker_heartbeats");
        }
    }
}
