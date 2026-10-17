using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AuditSphereDbContext))]
[Migration("20261017100000_PortalCommentCommandReceipts")]
public sealed class PortalCommentCommandReceipts : Migration
{
  protected override void Up(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.AddColumn<Guid>("request_id", "client_deliverable_comments", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<string>("request_sha256", "client_deliverable_comments", type: "character varying(64)", maxLength: 64, nullable: true);
    migrationBuilder.AddCheckConstraint("ck_client_deliverable_comment_values", "client_deliverable_comments",
      "length(body) > 0 AND ((resolved_at IS NULL) = (resolution IS NULL)) AND ((request_id IS NULL) = (request_sha256 IS NULL))");
    migrationBuilder.CreateIndex("ix_client_deliverable_comments_firm_id_author_user_id_request_id",
      "client_deliverable_comments", new[] { "firm_id", "author_user_id", "request_id" }, unique: true,
      filter: "request_id IS NOT NULL");
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
    migrationBuilder.DropIndex("ix_client_deliverable_comments_firm_id_author_user_id_request_id", "client_deliverable_comments");
    migrationBuilder.DropCheckConstraint("ck_client_deliverable_comment_values", "client_deliverable_comments");
    migrationBuilder.AddCheckConstraint("ck_client_deliverable_comment_values", "client_deliverable_comments",
      "length(body) > 0 AND ((resolved_at IS NULL) = (resolution IS NULL))");
    migrationBuilder.DropColumn("request_id", "client_deliverable_comments");
    migrationBuilder.DropColumn("request_sha256", "client_deliverable_comments");
  }
}
