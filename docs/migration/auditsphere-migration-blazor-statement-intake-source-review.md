# Blazor Statement Drilldown and Trial Balance Intake Source Review

**Status:** PARTIAL
**Source snapshot:** `a75ee4e269ebc1cd3706a8b5bc0d8f83e50c9a22`

## Scope and source identity

Reviewed the legacy statement and trial balance intake pages against their
discovery hashes and traced each route to the native Angular workbench, API
endpoints, and Application-owned queries/services.

| Legacy source | SHA-256 | Angular route and owners |
| --- | --- | --- |
| `src/AuditSphereOps.Web/Components/Pages/StatementDrillDown.razor` | `46baaa347796678dcf0db48c7a1df302ede8617f41ab77e79d751dde17b8ed6d` | `/app/engagements/:id/statements` and `/ui/app/...`; `features/engagements/statements.ts`, `statements.html`, `statement-contracts.ts`; `UiEndpoints.Statements.cs` and the Financial Statement workspace/query services |
| `src/AuditSphereOps.Web/Components/Pages/TrialBalanceIntake.razor` | `9ce8e72f08428d511de9930313692969d75fcfe5cedfb9e641a554f2693cd551` | `/app/engagements/:id/tb-intake` and `/ui/app/...`; `features/engagements/tb-intake.ts`, `tb-upload.ts`, `tb-source.ts`, `currency-review.ts`; trial-balance intake, source, mapping-memory and currency-review API/Application owners |

## Parity observed

- Statement views retain an explicit approved source/mapping/period basis,
  complete statement totals, bounded line and contribution paging, exact
  account contributions, linked audit procedures, exact source inspection,
  and a basis-bound CSV export. Stale basis or revoked access hides prior
  protected results.
- Intake preserves multi-period file preview and reviewed import, persisted
  per-period receipts, background validation, bounded source inspection and
  export, approved-mapping proposals, reviewer-gated mapping drafts, and
  currency/movement review. Upload recovery requires reselecting the file and
  fresh assent; an unknown import result is reconciled without automatic
  resubmission.
- The Angular unit cohort passed **28/28** across the statement, upload,
  source, and currency-review specs. Coverage includes malformed/mismatched
  statement bases, exact values, query bounds, stale/revoked clearing, upload
  checkpoint/assent fences, unknown outcomes, source paging/export guards, and
  currency context/rate validation.
- The PostgreSQL-backed API-host cohort passed **3/3**:
  `AngularTrialBalanceUploadJourneyTests` (2) and
  `FieldworkConnectionsJourneyTests` (1). It exercised rejected unbalanced
  input, reviewed two-period import, worker validation and receipts, exact
  source export, lost-response reconciliation, revocation clearing, and a
  statement-line-to-procedure journey.
- The built-in browser opened both current Angular routes with a guessed
  engagement ID. Statements showed the generic unavailable-in-scope state;
  Trial Balance Intake required a current accounting or engagement
  assignment. No protected financial data was returned and no business
  command was submitted.

## Remaining gaps

Both rows remain `PARTIAL`. The focused journeys do not establish the complete
role/scope, expired-grant, cross-firm, validation and provider-failure matrix
for every action and query. The intake file-format/size/error matrix and all
mapping-memory/currency-review outcomes still need complete source-action
assertion crosswalks. Human screen-reader and wider-locale acceptance also
remain open. Production/canary acceptance, whole-application AS-PAR-002 and
Blazor retirement remain separate open gates.

No full solution regression or EF model-drift check was run for this review.
The latest full-suite checkpoint remains the one recorded in
`docs/execution/status.json`; these focused results do not advance that
checkpoint or authorize retirement of the Blazor rollback host.
