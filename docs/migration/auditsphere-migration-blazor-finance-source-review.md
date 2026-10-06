# AuditSphere migration source review — firm finance

**Status:** PARTIAL_REVIEWED
**Reviewed against code/test commit:** `788bc218b095185124cca7ac421f244e415dd381`
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The three legacy page sources below match the SHA-256 values recorded in the
pinned discovery inventory. The Web pages remain rollback/reference sources.
Angular route ownership and focused journeys do not establish complete
behavior parity.

| Legacy source | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Pages/Finance.razor` | `f9af73a49ebc95d600572b77be7f2ddd375487691bec9dc9e0bb231c07f51260` |
| `src/AuditSphereOps.Web/Components/Pages/FirmBooks.razor` | `c398a80d7191bba2a1667861486ede0ab9d12512a46e63c0d5da9ff3f2912c9f` |
| `src/AuditSphereOps.Web/Components/Pages/InvoiceDetail.razor` | `2b0c1cf1718e61b95028b79f98344c0b40c189db9b9b38ddaa668dec6db1a48a` |

## Behavior and ownership mapping

| Legacy behavior | Angular, API and Application owner | Evidence and remaining gap |
|---|---|---|
| Firm ledger: show firm periods, accounts and the 25 most recent postings; allow an authorized FinanceReviewer to close an open period with a reason after confirming. | Angular `features/finance/ledger.ts`; API `UiEndpoints.Finance.cs`; Application `FirmFinanceQuery` and `LedgerService`. | `FirmFinanceQuery` requires an internal firm-wide FinanceManager or FinanceReviewer and filters every projection by firm. The period-close journey verifies a reviewer closes a period only after entering a reason and checks the persisted close decision. An additional browser route/accessibility sweep includes the ledger. Complete role combinations, cross-firm/guessed-ID responses, invalid/stale/revoked close attempts and failure/reconciliation behavior remain open. |
| Firm books: capture a dated operating expense and source document, submit it for independent review, then post the approved journal; calculate a firm trial balance. | Angular `features/finance/books.ts`; API `UiEndpoints.FirmBooks.cs`; Application `FirmBooksWorkspaceQuery`, `FirmExpenseService` and `LedgerService`. | The PostgreSQL API-host Angular journey verifies source-backed capture, submission, hidden self-review controls plus direct API self-review denial, a required reviewer-authored rejection reason persisted with the decision, and a second same-day expense independently approved and posted to a balanced/reconciled trial balance. It checks preparer/reviewer identities and immutable expense/posting state. The projection omits evidence bytes, and the upload endpoint is bounded to 5 MB. Malformed/oversized upload cases, cross-firm/guessed-ID isolation, duplicate post/retry and revoked-session states remain open. The second submission also exposed and fixed a journal-number collision caused by using only the time-ordered GUID prefix; generated expense journal numbers now include the full immutable expense ID. |
| Invoice detail: show the authorized invoice, lines, receipt allocations and outstanding balance; allow the invoice lifecycle actions according to status and finance authority. | Angular `features/finance/invoice.ts`; API invoice handlers in `UiEndpoints.Finance.cs`; Application `BillingInvoiceWorkspaceQuery` and `BillingService`. | PostgreSQL-backed browser journeys verify client-scoped invoice reads, sibling-client and wrong-client invoice denial with stale-content clearing, immediate clearing after grant revocation, bounded 100-item receipt/credit history paging, receipt allocation and credit-note recovery after a lost response. Angular also exposes review, post and send actions. Current browser coverage does not exercise every invoice lifecycle action, all firm-wide/client-scoped role combinations, cross-firm/guessed IDs, invalid cursors, history failures or each uncertain outcome. |

The application services keep firm books separate from client billing. The
firm ledger query rechecks authorization after reading its bounded projection;
the invoice workspace applies invoice/client authorization and rechecks access
before returning the related balance and histories. API endpoints derive the
trusted actor and compose Application services; Angular has no direct EF or
Microsoft Graph access.

## Local verification

The existing focused Release E2E cohort passed **6/6**, with zero failures and
zero skips, against isolated PostgreSQL fixtures. Its Angular/API-host cases
cover invoice billing and scope, firm period close, and the accessibility
sweep. One selected supporting test,
`FirmOperationsJourneyTests.LibraryPublishAnalyticsAndExpenseToTrialBalance`,
still runs through the retained legacy host and is not counted as Angular
evidence.

The dedicated native Angular firm-books journey passed **1/1** at
`788bc218`:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularFirmBooksJourneyTests'
```

It signs in separate FinanceManager and FinanceReviewer identities. The
FinanceManager also holds a reviewer grant for the negative self-review case;
the page hides its review action for its own submitted expense, and a direct
same-identity API rejection attempt returns 403 without changing the submitted
record. The independent reviewer opens the rejection dialog, cannot submit an
empty reason, enters a reason and verifies it is persisted with the rejected
decision. A second expense for the same date then submits successfully, is
approved by the distinct reviewer, posts once, and produces a balanced and
reconciled trial balance. At 390 px viewport width, the Angular document has
no horizontal overflow.

Additional focused evidence at the same source commit:

```bash
npm --prefix src/AuditSphereOps.Ui run test:ci -- --include=src/app/features/finance/books.spec.ts --include=src/app/features/finance/finance.spec.ts
dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~FirmOperationsTests'
```

The Angular finance contracts and rejection-reason tests passed **5/5**;
`FirmOperationsTests` passed **3/3**, including whitespace and overlength
reason refusal with no state mutation, followed by a trimmed persisted reason.
The Angular production build passed with 349.67 kB of initial chunks in this
run. It emitted the existing commercial-settings stylesheet warning (7.53 kB
against a 4 kB component budget). EF reported no pending model changes. The
full solution regression was not rerun; its latest complete checkpoint remains
1001/1001 at `ead85032`.

The built-in browser rendered the Development Firm ledger and Firm books
routes read-only. Its current identity has no firm-wide finance grant; both
routes showed the expected access-denied state without finance records or
actions. No business command was submitted. The invoices were not exercised in
that browser session.

## Open parity and retirement gates

All three source/action rows remain `PARTIAL`. The firm-books path now has a
direct Angular end-to-end journey covering rejection, required reason,
self-review denial, same-date submissions and independent approval. The full
finance-manager/reviewer and scope matrix, firm and guessed-ID isolation,
input/evidence validation, close and post retry/recovery, and invoice
approve/post/send paths still need broader assertions. Assistive-technology and wider-locale review,
production-like rollback/canary, live Microsoft gates and separate owner
acceptance remain open.

The full solution regression was not rerun for this slice. The latest complete PostgreSQL-backed Release regression remains
1001/1001 at `ead85032de2ccc4d4c8043398fa8471d395376a9`. The focused E2E build
compiled a concurrent unstaged change in
`src/AuditSphereOps.Application/Accounting/ConsolidationOverviewQuery.cs`; it
was unrelated to firm finance and excluded from the test commit. The separate
unstaged `docs/execution/angular-source-inventory.json` edit was left
untouched. AS-PAR-002 remains partial and Blazor retirement remains
`NOT_READY`; this review does not authorize removing the rollback host.
