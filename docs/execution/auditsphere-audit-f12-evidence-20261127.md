# F12 local verification evidence — 2026-11-27

## Source and behavior

Code commit `dd8278e8f31fa6907bc5364ab3fb062bbda6dc5e` follows the verified audit baseline `62059aa2e622bcb6245a211380021f5cf315cd97`. Generic TB/GL self-comparison remains fail-closed. This slice carries the missing-independent-control blocker into the analysis-evidence currentness projection used by the review UI, so a retained one-source ECL cannot appear merely stale without naming the missing independent population. Retained reconciliation and valuation evidence remains unchanged. Native AR/AP and typed bank paths remain separate; positive generic reconciliation is still gated on AC-REC-07–10 owner decisions.

The E2E cases now assert that source-only reconciliation, review and valuation preparation do not publish new receipts or change the retained evidence, while the supported specialist schedule handoff remains usable. During verification, the first E2E attempt exposed an obsolete assertion expecting a blocked reconciliation receipt; that assertion was corrected. The review-page check also confirmed the old generic message concealed the missing-control reason; the query projection was corrected and covered below.

## Changed files

- `src/AuditSphereOps.Application/Accounting/ReconciliationWorkspaceQuery.cs`: one shared internal blocker value for the missing independent control population.
- `src/AuditSphereOps.Application/Accounting/AccountingAnalysisReviewQuery.cs`: carry that precise blocker into `AccountingAnalysisReview.Blockers` when the retained reconciliation is source-only.
- `tests/AuditSphereOps.Domain.Tests/AccountingAnalysisReviewQueryTests.cs`: PostgreSQL regression for the source-only currentness blocker.
- `tests/AuditSphereOps.E2E.Tests/AngularAccountingPreparationCreationJourneyTests.cs`, `AngularEvidenceActionsJourneyTests.cs`, and `AngularValuationPreparationJourneyTests.cs`: real browser journeys assert blocked producer/reviewer actions, unchanged retained rows and the still-supported specialist schedule flow.
- `docs/architecture/auditsphere-architecture-code-map.md`: records the analysis-evidence currentness blocker owner.

## Focused checks

- `git diff --check` — PASS.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false --filter 'FullyQualifiedName~ReconciliationReviewApiTests.GenericSourceOnlyPreparationBlocksTrialBalanceAndSameBatchGeneralLedger|FullyQualifiedName~AccountingEvidenceActionsApiTests.ScopeSourceReplayPeriodAndLegacyRiskFencesNeverPublishAnUnsupportedDecision' --logger 'trx;LogFileName=f12-source-only-api-final.trx' --results-directory /tmp/auditsphere-f12-source-only-api-final` — PASS, 2/2. This run preceded the blocker-projection refinement in `dd8278e8`.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false --filter 'FullyQualifiedName~AccountingAnalysisReviewQueryTests.GenericSourceOnlyEvidenceNamesTheMissingIndependentControlBlocker' --logger 'trx;LogFileName=f12-currentness-blocker.trx' --results-directory /tmp/auditsphere-f12-currentness-blocker` — PASS, 1/1 against the isolated PostgreSQL test database.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false --filter 'FullyQualifiedName~AccountingEvidenceActionsApiTests.ScopeSourceReplayPeriodAndLegacyRiskFencesNeverPublishAnUnsupportedDecision' --logger 'trx;LogFileName=f12-evidence-currentness-api.trx' --results-directory /tmp/auditsphere-f12-evidence-currentness-api` — PASS, 1/1 after the blocker-projection change.
- `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/accounting/reconciliation-create.spec.ts'` — PASS, 1/1.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore -c Release --maxcpucount:1 -p:UseSharedCompilation=false -nodeReuse:false --filter 'FullyQualifiedName~AngularAccountingPreparationCreationJourneyTests.SourceOnlyReconciliationIsBlockedWhileSpecialistPreparationRetainsReviewedReceipt|FullyQualifiedName~AngularEvidenceActionsJourneyTests.SourceOnlyAnalysisRetainsItsLinkButBlocksNewReviewAndClearsAfterRevocation|FullyQualifiedName~AngularValuationPreparationJourneyTests.SourceOnlyReconciliationBlocksNewEclAndInventoryPreparation' --logger 'trx;LogFileName=f12-source-only-e2e-verified.trx' --results-directory /tmp/auditsphere-f12-source-only-e2e-verified` — PASS, 3/3. Real Playwright journeys used the isolated PostgreSQL test database and synthetic local provider-free fixtures.

## Remaining gates

Fast CI run [45968438963](https://github.com/nirzaf/AuditSphere/actions/runs/45968438963) checked out the parent source `dd8278e8f31fa6907bc5364ab3fb062bbda6dc5e` and was `PENDING` at 2026-11-27 08:48 UTC; no current-source full release-verification run is recorded. These checks do not establish production migration or acceptance. AC-REC-07–10 remain `BLOCKED_DECISION` for the qualified owner’s independent-source pairing and adjustment-basis contract. No production, provider, or professional decision was made.

## Exact-source regression follow-up — `80af1003`

The focused regression `ReconciliationApprovalSerializesItemEditsAndUnsupportedApprovedSourceBlocksCloseWithoutMutation` passed 1/1 on exact HEAD `80af10032c3c41f69f9e70802c60dc3081b75049`, using the isolated PostgreSQL database. It verifies the review/edit lock serialization and that a generic approved source-only reconciliation blocks period close with the missing-independent-control gate while leaving the period and retained proof unchanged. A first exact-commit run timed out during fixture `SaveChangesAsync` before reaching the assertion; the database was subsequently observed healthy and idle, and the rerun passed in 3 seconds. No application source or retained financial proof changed in this follow-up; the test assertion was aligned with F12's already-implemented fail-closed behavior in commit `80af1003`.

Fast CI run [45973272824](https://github.com/nirzaf/AuditSphere/actions/runs/45973272824) is queued for `80af1003`; it is not full release verification. AC-REC-07–10 remain `BLOCKED_DECISION`; no production migration, data change or professional/accounting approval was made.

## Exact-source full-run expectation alignment — `2476c7b5`

Full release verification [45975374971](https://github.com/nirzaf/AuditSphere/actions/runs/45975374971) failed on exact source `2476c7b5727e2e54340a3aa35838df025074b175`. The F12 related failures were stale tests that still treated generic TB/GL self-comparison as an independent reconciliation: valuation API tests expected new preparation receipts, the accounting benchmark expected four reconciled proofs without approved openings, and the analysis browser journey expected the retired generic blocker text. No product behavior was changed to satisfy those assertions. The fixtures/assertions were realigned to the adopted fail-closed rule: source-only valuation preparation is refused with no new preparation rows; absent opening evidence yields four `UNRECONCILED` benchmark rows; and the reviewer sees the missing independent-control reason. Existing retained evidence and source totals are asserted unchanged.

Focused checks on the same local source tree:

- Isolated PostgreSQL API: `FullyQualifiedName~ValuationPreparationApiTests` — **3 passed**. The test now covers both valuation methods, the missing-control blocker, refusal of direct execute, no preparation rows and unchanged retained evidence.
- Isolated PostgreSQL Domain: `FullyQualifiedName~AccountingBenchmarkTests.RepresentativeAccountingWorkload_CompletesDurablyAndReportsMeasurements` — **1 passed**. The benchmark retains its durable workload assertions while reporting the four source-only proofs as unreconciled.
- Isolated PostgreSQL Playwright: `FullyQualifiedName~AngularAnalysisReviewJourneyTests.NativeEvidenceNavigationPreservesExactInputsProvenanceAndRevocationFences` — **1 passed** with the visible missing-independent-control blocker and revocation fence.

The full run also failed at unrelated exact expectations recorded in the current-slice handoff. The test updates are uncommitted at this checkpoint; exact-SHA CI must be rerun after the coherent commits. AC-REC-07–10 remain `BLOCKED_DECISION`. No historical proof, production data, external provider or professional decision was changed.
