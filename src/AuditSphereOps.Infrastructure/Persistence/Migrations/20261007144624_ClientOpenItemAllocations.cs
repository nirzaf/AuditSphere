using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientOpenItemAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_open_item_allocation_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    counterparty_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source_amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_open_item_allocation_submissions", x => x.id);
                    table.UniqueConstraint("AK_client_open_item_allocation_submissions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_open_item_allocation_submission", "source_kind IN ('SALES_CREDIT','PURCHASE_CREDIT','SALES_RECEIPT','SUPPLIER_PAYMENT') AND disposition IN ('ALLOCATE','UNALLOCATE') AND source_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND counterparty_id<>'00000000-0000-0000-0000-000000000000'::uuid AND currency ~ '^[A-Z]{3}$' AND source_amount>0 AND source_hash ~ '^[a-f0-9]{64}$' AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND manifest_hash ~ '^[a-f0-9]{64}$' AND length(trim(reference))>0 AND length(trim(reason))>0 AND length(manifest_json)>0");
                    table.ForeignKey(
                        name: "FK_client_open_item_allocation_submissions_client_bookkeeping_~",
                        columns: x => new { x.firm_id, x.client_id, x.counterparty_id },
                        principalTable: "client_bookkeeping_counterparties",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_open_item_allocation_submissions_users_firm_id_creat~",
                        columns: x => new { x.firm_id, x.created_by_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_open_item_allocation_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    decision = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    preview_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    review_context_json = table.Column<string>(type: "character varying(500000)", maxLength: 500000, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_open_item_allocation_decisions", x => x.id);
                    table.UniqueConstraint("AK_client_open_item_allocation_decisions_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_open_item_allocation_decision", "decision IN ('APPROVE','RETURN') AND command_id<>'00000000-0000-0000-0000-000000000000'::uuid AND intent_hash ~ '^[a-f0-9]{64}$' AND preview_digest ~ '^[a-f0-9]{64}$' AND length(trim(reason))>0 AND length(review_context_json)>0");
                    table.ForeignKey(
                        name: "FK_client_open_item_allocation_decisions_client_open_item_allo~",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_open_item_allocation_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_client_open_item_allocation_decisions_users_firm_id_actor_u~",
                        columns: x => new { x.firm_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "firm_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_open_item_allocation_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_open_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    reverses_allocation_line_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_open_item_allocation_lines", x => x.id);
                    table.UniqueConstraint("AK_client_open_item_allocation_lines_firm_id_client_id_id", x => new { x.firm_id, x.client_id, x.id });
                    table.CheckConstraint("ck_client_open_item_allocation_line", "line_number>0 AND target_kind IN ('SALES_INVOICE','PURCHASE_INVOICE') AND target_open_item_id<>'00000000-0000-0000-0000-000000000000'::uuid AND amount>0");
                    table.ForeignKey(
                        name: "fk_client_open_item_allocation_line_reversal",
                        columns: x => new { x.firm_id, x.client_id, x.reverses_allocation_line_id },
                        principalTable: "client_open_item_allocation_lines",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_open_item_allocation_line_submission",
                        columns: x => new { x.firm_id, x.client_id, x.submission_id },
                        principalTable: "client_open_item_allocation_submissions",
                        principalColumns: new[] { "firm_id", "client_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_decisions_firm_id_actor_user_id",
                table: "client_open_item_allocation_decisions",
                columns: new[] { "firm_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_decisions_firm_id_client_id_act~",
                table: "client_open_item_allocation_decisions",
                columns: new[] { "firm_id", "client_id", "actor_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_decisions_firm_id_client_id_sub~",
                table: "client_open_item_allocation_decisions",
                columns: new[] { "firm_id", "client_id", "submission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_lines_firm_id_client_id_reverse~",
                table: "client_open_item_allocation_lines",
                columns: new[] { "firm_id", "client_id", "reverses_allocation_line_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_lines_firm_id_client_id_submiss~",
                table: "client_open_item_allocation_lines",
                columns: new[] { "firm_id", "client_id", "submission_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_submissions_firm_id_client_id_~1",
                table: "client_open_item_allocation_submissions",
                columns: new[] { "firm_id", "client_id", "created_by_user_id", "command_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_submissions_firm_id_client_id_c~",
                table: "client_open_item_allocation_submissions",
                columns: new[] { "firm_id", "client_id", "counterparty_id" });

            migrationBuilder.CreateIndex(
                name: "IX_client_open_item_allocation_submissions_firm_id_created_by_~",
                table: "client_open_item_allocation_submissions",
                columns: new[] { "firm_id", "created_by_user_id" });

            migrationBuilder.Sql(ClientOpenItemAllocationSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ClientOpenItemAllocationSql.Down);
            migrationBuilder.DropTable(
                name: "client_open_item_allocation_decisions");

            migrationBuilder.DropTable(
                name: "client_open_item_allocation_lines");

            migrationBuilder.DropTable(
                name: "client_open_item_allocation_submissions");
        }
    }
}
