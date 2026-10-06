# AuditSphere migration source review — firm finance

**Status:** PARTIAL_REVIEWED
**Reviewed against code/test commit:** `e33010354e331acfbb87ecb2b5879e89ac2a6602`
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
| Firm ledger: show firm periods, accounts and the 25 most recent postings; allow an authorized FinanceReviewer to close an open period with a reason after confirming. | Angular `features/finance/ledger.ts`; API `UiEndpoints.Finance.cs`; Application `FirmFinanceQuery` and `LedgerService`. | `FirmFinanceQuery` requires an internal firm-wide FinanceManager or FinanceReviewer and filters every projection by firm. The period-close journey verifies a reviewer closes an open period only after entering a reason and persists the close decision. A boundary journey verifies a client-scoped FinanceManager is denied both the Angular read and direct API, a reviewer sees only the local period, foreign and random guessed period IDs return indistinguishable 403 close responses without mutation, and revoking the reviewer grant clears visible ledger state. A later validation journey verifies whitespace-only input disables the Angular confirmation, direct API submission returns safe `ledger.invalid`, and the local period stays open without a close decision. An additional browser route/accessibility sweep includes the ledger. Other role combinations and state-conflict/provider failure and retry/recovery behavior remain open. |
| Firm books: capture a dated operating expense and source document, submit it for independent review, then post the approved journal; calculate a firm trial balance. | Angular `features/finance/books.ts`; API `UiEndpoints.FirmBooks.cs`; Application `FirmBooksWorkspaceQuery`, `FirmExpenseService` and `LedgerService`. | The PostgreSQL API-host Angular journeys verify source-backed capture, independent review and rejection, reason persistence, same-day journal identity, approval/posting and a balanced/reconciled trial balance. A boundary journey verifies malformed and 5 MiB + 1 uploads fail safely, foreign and random guessed IDs return indistinguishable 403 responses for submit/post, and foreign expense state is unchanged. It increments the authorized user's session epoch and verifies that the rendered page clears to “Access unavailable”. The post journey repeats the same post command twice, receives identical successful responses, and verifies one persisted posting. Evidence bytes are omitted from projections; upload size remains bounded to 5 MB. |
| Invoice detail: show the authorized invoice, lines, receipt allocations and outstanding balance; allow the invoice lifecycle actions according to status and finance authority. | Angular `features/finance/invoice.ts`; API invoice handlers in `UiEndpoints.Finance.cs`; Application `BillingInvoiceWorkspaceQuery` and `BillingService`. | PostgreSQL-backed browser journeys verify client-scoped invoice reads, sibling-client and wrong-client invoice denial with stale-content clearing, immediate clearing after grant revocation, bounded 100-item receipt/credit history paging, receipt allocation and credit-note recovery after a lost response. The lifecycle journey proves role-specific action visibility and backend enforcement: FinanceManager cannot approve, FinanceReviewer approves independently but cannot post, and an authorized FinanceManager posts and sends. Posting also requires an approved finance profile matching invoice currency; repeated post commands return identical success responses. A boundary journey compares a real foreign-firm invoice ID with a random ID for detail read and approve/post/send; every pair returns the same 403, and the foreign invoice remains unchanged. Invalid cursor shape/empty-ID cases return the same safe 400; transient receipt and credit-history failures preserve loaded rows and succeed on retry. Held history responses are discarded after route change and after session revocation. Broader role/scope combinations and remaining uncertain outcomes remain open. |

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

The firm-ledger authorization journey passed **1/1** at `dc43a898`:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularFirmLedgerBoundaryJourneyTests'
```

On isolated PostgreSQL state, a client-scoped FinanceManager receives an
access-unavailable screen and HTTP 403 from the ledger endpoint without the
foreign period marker. A firm-wide reviewer sees the local period only;
closing an actual foreign-firm period ID and a random unknown ID returns
identical HTTP 403 bodies. The foreign period remains open at its original
revision with no close decision. Revoking the reviewer grant then clears the
page to the Access unavailable state and removes the local period content.
The journey passed **1/1** with no page errors.

At `cfbeed2d`, the same isolated PostgreSQL/API-host browser journey also
submits a whitespace-only close reason through the UI and confirms the
confirmation remains disabled. A direct API request returns HTTP 400 with the
safe `ledger.invalid` response; PostgreSQL confirms the local period remains
`OPEN` at revision 1 with no close decision. The expanded journey passed
**1/1** in 37 seconds. The foreign-versus-guessed denial and revocation checks
remain part of that run.

The Angular invoice lifecycle and adjacent billing/scope regression passed
**3/3** at `7834ddec`:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularBillingWorkspaceJourneyTests|FullyQualifiedName~AngularInvoiceScopeJourneyTests|FullyQualifiedName~AngularInvoiceWorkflowJourneyTests'
```

The journey verifies FinanceManager sees no approve action and receives HTTP
403 for direct approval. A separate FinanceReviewer approves the invoice and
records that reviewer identity. The reviewer sees no post action and receives
HTTP 403 for direct posting. The FinanceManager then posts under an approved
QAR profile and sends the invoice; PostgreSQL retains `SENT`, reviewer identity,
`PostedAt` and `SentAt`. Repeating the post command returns the same successful
response. The combined workflow, receipt/credit recovery, invoice-scope and
grant-revocation cohort passed **3/3** in 1m4s with no browser errors.

`BillingInvoiceWorkspaceQuery` now supplies action capabilities from current
authorization and persisted finance-profile state. Angular only offers
Approve to a non-preparer FinanceReviewer, Post to an authorized FinanceManager
when an approved invoice-currency profile exists, and Send when billing
authority is present; Application commands remain authoritative.

The invoice boundary journey was extended at `1b439f25` to seed a separate
foreign firm, client, account and review-required invoice. An authenticated
FinanceManager compares that real invoice with a random unknown ID against the
detail read and each approve, post and send endpoint. All responses are HTTP
403 with identical bodies for each pair; the foreign response contains no
invoice marker. PostgreSQL confirms the foreign invoice remains
`REVIEW_REQUIRED`, with no approval, posting or sent timestamps. The focused
API-host Angular browser journey passed **1/1** in 35 seconds with no failures.

The billing workspace journey was extended at `de27c966` for history failure
and cursor recovery:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularBillingWorkspaceJourneyTests'
```

Three malformed or empty cursor combinations return the same safe HTTP 400
body, without invoice details. The journey injects a one-time 503 for both the
older-receipt and older-credit-note requests. The Angular page retains the
previously loaded rows, presents safe retry messages for both, and user retries
retrieve the remaining history. The browser test passed **1/1** in 34 seconds
with no page errors. Stale in-flight history cancellation and the broader
authority matrix remain open.

At `793d29dd`, the same PostgreSQL/API-host browser journey holds an older
receipt page response, changes the Angular route to a different client's
invoice, and then releases the old response. The new invoice stays visible with
its empty receipt state; the old client's receipt markers do not return. This
route-generation isolation check passed **1/1** in 35 seconds.

At `e3301035`, the browser fetches an authorized receipt-history page from the
real API and holds delivery of its HTTP 200. The test advances the signed-in
user's session epoch; invoice refresh and session revalidation both return 401,
and Angular clears the page to Access unavailable. Releasing the already
fetched history response afterward does not restore the invoice or receipt
markers. This passed **1/1** in 34 seconds with no page errors.

The dedicated native Angular firm-books and boundary journeys passed **2/2** at
`3bff1eb1`:

```bash
dotnet test tests/AuditSphereOps.E2E.Tests/AuditSphereOps.E2E.Tests.csproj --no-restore --configuration Release -m:1 --filter 'FullyQualifiedName~AngularFirmBooksBoundaryJourneyTests|FullyQualifiedName~AngularFirmBooksJourneyTests'
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

The boundary journey submits a malformed form and a 5 MiB + 1 evidence file
and confirms safe HTTP 400 errors. It compares real foreign-firm expense IDs
with random unknown IDs for submit and post; both return equal HTTP 403 bodies,
and the foreign row remains Draft without journal/posting state. After the
test advances the signed-in user's session epoch, a reload renders “Access
unavailable” and does not display fixture markers. The successful post journey
repeats the post request twice after the UI reports success; both API responses
match, and PostgreSQL still contains one posting. The combined focused run
passed **2/2** in 45 seconds.

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

All three source/action rows remain `PARTIAL`. The invoice UI now gates
approve/post/send actions by the Application capability projection, and the
role-separated lifecycle journey covers approval, posting, sending and repeat
post recovery. One invoice browser journey compares cross-firm and guessed IDs
for detail, approve, post and send without disclosing or mutating the foreign
invoice. Invalid invoice cursor pairs and receipt/credit-history transient
failures and retries are covered. Route changes discard an in-flight history
response. Session revocation during an in-flight invoice-history response
now has focused negative evidence. Broader role/scope combinations and
remaining uncertain outcomes remain open.
The firm-ledger path has
focused scoped-read, foreign/guessed close-ID, unchanged-state, revoked-session
clearing, and invalid-reason evidence. State-conflict/provider failure and
recovery remain open. Firm books now has focused
evidence for malformed/oversized upload handling, foreign/guessed submit and
post isolation, session-epoch clearing, and idempotent post retry. The full
FinanceManager/FinanceReviewer and scope matrix, remaining input/evidence
validation, post failure/unknown-outcome recovery, and invoice stale in-flight
history handling still need broader assertions.
Assistive-technology and wider-locale review,
production-like rollback/canary, live Microsoft gates and separate owner
acceptance remain open.

The full solution regression was not rerun for these slices. The latest complete PostgreSQL-backed Release regression remains
1001/1001 at `ead85032de2ccc4d4c8043398fa8471d395376a9`. The firm-ledger
reason journey passed at `cfbeed2d`. The invoice session-revocation journey
and Angular build passed in a clean isolated verification worktree at
`e3301035` because concurrent uncommitted API changes in the shared checkout
failed compilation. Only the tested E2E file was copied into that temporary
worktree; no concurrent source or documentation edits were modified or
included. Both test commits are pushed to `master`.
AS-PAR-002 remains partial and Blazor retirement remains
`NOT_READY`; this review does not authorize removing the rollback host.
