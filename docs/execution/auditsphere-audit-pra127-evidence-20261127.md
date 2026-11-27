# PRA-127 receipt timestamp capture

Disposition: **IMPLEMENTED** for the native invoice receipt and fee-payment flows. Source commit: `469a7cd9`. Verified baseline: `62059aa2e622bcb6245a211380021f5cf315cd97`. The wider assignment remains incomplete; this source commit has no exact-SHA CI result yet.

## Behavior

`POST /api/ui/finance/billing-accounts/{id}/receipts` and the advance/balance fee-payment routes now accept an optional `ReceivedAt`. When supplied, the API accepts only invariant UTC `yyyy-MM-ddTHH:mm:ss.fffZ`, persists the exact instant through the existing service parameter, and rejects malformed or offset-bearing values with the existing route error. Omission still uses the existing server-time fallback.

The invoice form reviews and retains the submitted instant in its existing pending command. Both fee-payment forms expose a UTC-labelled datetime control and include the exact timestamp in reviewed intent. Fee-payment recovery retains amount, reference, milestone, timestamp and firm/user/session-generation/agreement identity in session storage. After an unknown response, the user must refresh, explicitly acknowledge the current state and resubmit the same intent; no automatic payment retry was added. Changed date under a retained invoice request conflicts through the existing request hash; changed date under a fee reference conflicts through the existing fee-payment identity check. Receipt `ReceivedAt` remains distinct from allocation `CreatedAt`; no cutoff or revenue-recognition policy was added.

Files changed: `UiEndpoints.Finance.cs`, `UiEndpoints.FeeAgreement.cs`, Angular `invoice.ts` / `fee-agreement.ts`, their existing specs, `BillingWorkspaceApiTests.cs`, `FirmReceivablesAgingApiTests.cs`, `BillingTests.cs`, `CommercialWorkflowTests.cs`, `FirmReceivablesAgingQueryTests.cs`, generated `contracts/auditsphere-openapi.json`, and the practice billing section of `auditsphere-architecture-code-map.md`.

## Verification

- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Api.Tests/AuditSphereOps.Api.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~BillingWorkspaceApiTests.InvoiceWorkspaceRecordsAllocatesAndCreditsWithCurrentAuthorizationAndAntiforgery|FullyQualifiedName~FirmReceivablesAgingApiTests.TermsRequireIndependentReviewAndReportAndExportUseAuthorizedAsOfState'` — **2 passed**. The test used the approved isolated loopback `auditsphere_tests` PostgreSQL 19 database. An initial invocation without the local role override failed because this cluster has no `postgres` role; a read-only check confirmed `qts` is the local superuser on `auditsphere_tests` (`server_version_num=190000`). No development or production database was used.
- `AUDITSPHERE_TEST_CONNECTION='Host=127.0.0.1;Port=5433;Database=auditsphere_tests;Username=qts' dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore -c Release --maxcpucount:1 --filter 'FullyQualifiedName~BillingTests.BillingWorkflow_SeparatesFinanceRoles_BalancesReceiptAndCredit_AndProtectsPostedHistory|FullyQualifiedName~FirmReceivablesAgingQueryTests.ReportUsesApprovedTermsAndOnlyReceiptsCreditsAndAllocationsEffectiveByUtcAsOfDate|FullyQualifiedName~CommercialWorkflowTests.AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach'` — **5 passed**.
- `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/finance/invoice-read.spec.ts'` — **31 passed**.
- `npm --prefix src/AuditSphereOps.Ui run test:ci -- --include 'src/app/features/commercial/fee-agreement.spec.ts'` — **25 passed**. A first run exposed strict-index-signature errors and two old draft assertions that needed the newly retained timestamp; both were corrected before the passing rerun.
- `npm --prefix src/AuditSphereOps.Ui run build` — **passed**. Existing stylesheet budget warnings remain in `features/commercial/settings.scss` and `app.scss`; neither file changed here.
- `dotnet run --project tools/AuditSphereOps.OpenApiEmitter --configuration Release --no-restore -- contracts/auditsphere-openapi.json` followed by `bash scripts/contracts/verify-openapi.sh` — **passed**; only the two intended optional `receivedAt` request fields were added.
- `git diff --check`, `python3 scripts/docs/validate-markdown-documentation.py`, `python3 scripts/docs/validate-markdown-filenames.py`, and `python3 scripts/docs/validate-narrative-metrics.py` — **passed** (205 Markdown files, 3,086 relative links, no filename or protected-narrative findings).
- Interactive browser verification was **NOT_RUN**. The available in-app browser was on GitHub Actions; no local application and authorized synthetic finance session were running. Focused Angular tests and the real isolated API tests covered the changed interactions and HTTP boundary.

## Exact-source journey follow-up — `80af1003`

`AngularFeeSettlementJourneyTests.ScopedFinancePreparesReviewsAndSettlesAdvanceWithoutCommercialAccess` passed 1/1 on exact HEAD `80af10032c3c41f69f9e70802c60dc3081b75049` using Playwright and the isolated PostgreSQL test database. This verified the supported finance preparation/review/advance-settlement browser journey after updating its locator to the current visible payment-details label. The source behavior and API contracts were unchanged by this test-only follow-up. This is local focused browser evidence, not full E2E-suite or production acceptance.

Fast CI run [45973272824](https://github.com/nirzaf/AuditSphere/actions/runs/45973272824) is queued for `80af1003`; no exact-source full release-verification run is recorded. Historical `469a7cd9` results above remain attributed to that source.

## Exact-source E2E locator alignment — `2476c7b5`

Full release verification [45975374971](https://github.com/nirzaf/AuditSphere/actions/runs/45975374971) reported stale browser locators for the updated receipt-review labels. The test locators in `AngularInvoiceBillingScopeJourneyTests`, `AngularBillingWorkspaceJourneyTests`, `AngularSteCommercialAcceptanceJourneyTests` and `AngularCommercialJourneyTests` now match the visible labels for payment details, received time, fee, milestone and selected action. No UI copy or payment behavior changed in this test-only follow-up.

Against the current local source and freshly built Angular assets, the focused Playwright cohort passed **6/6**: invoice billing scope, invoice receipt allocation/recovery, the three route-bound fee-payment cases, and quotation approval. The test suite remains synthetic and PostgreSQL-isolated. Exact-SHA CI is still required for the eventual candidate; no live finance/provider or accounting acceptance is claimed.

## Remaining gates

- Exact-SHA CI for `469a7cd9` is pending; no full-suite claim is made.
- No database schema changed; no migration was generated or applied.
- Backdating cutoff rules remain a separate `BLOCKED_DECISION`; this work only records the supplied received time. PRA-133/PRA-134/PRA-152 billing-to-ledger policy remains blocked, and this change does not recognize revenue or initiate payment.
- Release/deployment, production data changes, external provider effects and professional/accounting sign-off were not performed or authorized by this slice.
