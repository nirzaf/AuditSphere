using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditSphereOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrialBalanceDigestVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_dataset_digest_version",
                table: "trial_balance_import_batches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "tb-csv-normalized.v2");

            migrationBuilder.AddColumn<string>(
                name: "normalized_dataset_digest_version",
                table: "trial_balance_datasets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_import_batch_digest_version",
                table: "trial_balance_import_batches",
                sql: "normalized_dataset_digest_version IN ('tb-csv-normalized.v2','tb-csv-normalized.v3')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tb_dataset_digest_version",
                table: "trial_balance_datasets",
                sql: "normalized_dataset_digest_version IS NULL OR normalized_dataset_digest_version IN ('tb-csv-normalized.v2','tb-csv-normalized.v3')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Digest version evidence is retained with imported accounting sources and cannot be downgraded away.");
        }
    }
}
