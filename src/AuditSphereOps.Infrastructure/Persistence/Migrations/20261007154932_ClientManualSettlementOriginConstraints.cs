using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientManualSettlementOriginConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_manual_settlement_origins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_manual_settlement_origins", x => x.id);
                    table.UniqueConstraint("AK_client_manual_settlement_origins_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_manual_settlement_origin", "source_kind IN ('SALES_RECEIPT','SUPPLIER_PAYMENT') AND amount>0 AND length(trim(reference))>0 AND length(trim(evidence_reference))>0 AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_client_manual_settlement_origins_client_bookkeeping_counter~",
                        columns: x => new { x.firm_id, x.client_id, x.counterparty_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_manual_settlement_origins_client_operational_journal~",
                        columns: x => new { x.firm_id, x.client_id, x.journal_id },
                        principalTable: "client_operational_journals",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_manual_settlement_origins_users_firm_id_created_by_u~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_manual_settlement_origins_firm_id_client_id_counterp~",
                table: "client_manual_settlement_origins",
                columns: new[] { "firm_id", "client_id", "counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_manual_settlement_origins_firm_id_client_id_created_~",
                table: "client_manual_settlement_origins",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_manual_settlement_origins_firm_id_client_id_journal_~",
                table: "client_manual_settlement_origins",
                columns: new[] { "firm_id", "client_id", "journal_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_manual_settlement_origins_firm_id_client_id_source_k~",
                table: "client_manual_settlement_origins",
                columns: new[] { "firm_id", "client_id", "source_kind", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_manual_settlement_origins_firm_id_created_by_user_id",
                table: "client_manual_settlement_origins",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientManualSettlementSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientManualSettlementSql.Down);
            migrationBuilder.DropTable(
                name: "client_manual_settlement_origins");
        }
    }
}
