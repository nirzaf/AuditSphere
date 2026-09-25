# Golden fixtures, integrated journeys and verification layers

[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)

**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.

<!-- SOURCE-LINES: 1309-1380 -->
## 7. Integrated numerical fixtures and failure journeys

<a id="section-7-1"></a>
### 7.1 Golden accounting fixtures

The examples below are **proposed synthetic QA fixtures**, not client data, market rates, or approved professional treatments. Have the methodology owner approve exact inputs/expected outputs. Keep them separate from the repository's existing approved fixtures; preserve both with explicit names.

| Fixture | Inputs | Exact expected outcome |
|---|---|---|
| GOLD-R2R-01 — raw/adjusted TB | Signed raw rows: Cash 10,000; AR 3,000; Equipment 5,000; AP −4,000; Share capital −10,000; Revenue −8,000; Expense 4,000. AJE: Dr depreciation expense 100, Cr accumulated depreciation 100. | Raw signed total 0; delta total 0; adjusted signed total 0. Raw assets 18,000/profit 4,000; adjusted assets 17,900/profit 3,900/equity 13,900/liabilities 4,000. Raw rows unchanged. |
| GOLD-R2R-02 — mapping micro-unit | Amount 0.010000 split by 0.333333, 0.333333, 0.333334, stable target order; six-decimal ToEven and last-destination residual. | Allocated 0.003333, 0.003333, 0.003334; sum 0.010000. No unexplained account or loss of 0.000001. |
| GOLD-R2R-03 — bank proof | Ledger 10,000; supporting bank statement 10,200; deposit in transit +500; outstanding cheque −800; accepted unreflected ledger bank fee −100. | Explained ledger 9,900; explained support 9,900; residual 0. If ledger basis is already adjusted to 9,900, fee is not added again. |
| GOLD-R2R-04 — cash flow | Opening cash 1,000; operating +200; investing −300; financing +100; FX cash effect +10; separate noncash lease 500. | Closing cash 1,010; noncash lease excluded from cash arithmetic and separately disclosed. |
| GOLD-R2R-05 — equity | Opening 5,000 + profit 900 + OCI 50 + contributions 400 − distributions 200 − approved opening error adjustment 100. | Closing equity 6,050; error adjustment is not current profit. |
| GOLD-R2R-06 — whole-owned group | Parent: Cash 5,000; IC AR 1,000; Investment 2,000; capital/retained equity −8,000. Subsidiary: Cash 1,500; inventory 1,500; IC AP −1,000; capital −2,000. | Combined assets 11,000. Eliminate IC AR/AP 1,000 and investment/subsidiary equity 2,000: group assets 8,000, liabilities 0, parent equity 8,000. Both source TBs unchanged. |
| GOLD-R2R-07 — FX bridge | Synthetic foreign entity: cash 100; capital −80; revenue −40; expense 20. Closing rate 4, historical capital rate 3.5, representative income/expense rate 3.6. These rates are test values, not market quotations. | Cash 400; capital −280; revenue −144; expense 72; calculated translation reserve −48; signed sum 0. Equity = capital 280 + profit 72 + reserve 48 = 400. A universal closing rate would give a different and prohibited treatment. |
| GOLD-R2R-08 — source reflection | New accepted raw TB contains GOLD-01's 100 depreciation AJE; exact reflection evidence says Reflected. | Reporting delta for that journal is 0, assets/profit remain 17,900/3,900, not 17,800/3,800. Old raw and adjusted snapshots remain available. |

<a id="section-7-2"></a>
### 7.2 Complete integrated acceptance journeys

| ID | Journey / fault | Required observed result |
|---|---|---|
| R2R-AT-01 | New entity context → TB/GL → mapping → AJE → reconciliation → statements → package → close | Every displayed/exported amount reconciles to the same scope and exact inputs; no draft step is silently skipped. |
| R2R-AT-02 | Same account code and period label in two clients | No shared data, authority, cache, row, mapping, export or evidence leakage. |
| R2R-AT-03 | Existing browser circuit navigates to another unauthorized context | Prior content/IDs/counts/download links clear before new load; no full-page reload needed to enforce denial. |
| R2R-AT-04 | User grant/session revoked after opening approval dialog | Handler denies and UI clears protected content; no approval persists. |
| R2R-AT-05 | Two reviewers act on same expected revision | Unique/optimistic concurrency rule yields one applicable decision or explicit conflict, not duplicate contradictory approval. |
| R2R-AT-06 | Duplicate command, same OperationId/body | One mutation/result; retry reauthorizes. Changed body with same ID conflicts. |
| R2R-AT-07 | Import interrupted, duplicate/resumed/out-of-order chunk | Consistent staged state; no source seal before full expected counts/hash; prior accepted source remains. |
| R2R-AT-08 | Macro/formula/external-link/fake-XLSX/decompression attack | Bounded failure with safe issue list; no parser execution, network request or partial acceptance. |
| R2R-AT-09 | Missing opening data or GL-only account | Completeness stays incomplete; missing is not zero; UI and export agree. |
| R2R-AT-10 | New accepted AJE after initial plan | Adjustment-set membership changes; old statement/package cannot be newly approved as current. |
| R2R-AT-11 | Partial/rejected management AJE | No unbalanced subset applied; remainder visible for human evaluation; fresh balanced revision when needed. |
| R2R-AT-12 | Replacement TB includes a former adjustment | Fresh reflection decision; no double count; old result preserved. |
| R2R-AT-13 | Approved reconciliation supporting document replaced | Old proof/review historical; currentness stale and downstream finalization blocked. |
| R2R-AT-14 | Cash-flow noncash movement included by mistake | Validation identifies classification/bridge mismatch; no balancing cash plug. |
| R2R-AT-15 | Comparative mapping/source changes after current review | Current dependent statement stale; as-issued historical output unchanged. |
| R2R-AT-16 | Error restatement versus estimate change | Approved distinct period treatment, disclosure and lineage; no universal retrospective or current-P&L shortcut. |
| R2R-AT-17 | Required disclosure removed/added after build | Set-membership hash changes even when balances do not; prior review no longer authorizes current package. |
| R2R-AT-18 | XLSX succeeds, DOCX/PDF generation or artifact publication fails | Package not sealed/advanced; successful intermediate files explicitly nonpublished; retry deterministic. |
| R2R-AT-19 | Source acceptance races package rendering | Worker cannot publish against obsolete generation; no current success notification. |
| R2R-AT-20 | Artifact bytes tampered after rendering | Digest check blocks seal/review/download/release handoff as applicable; historical identity not rewritten to match tamper. |
| R2R-AT-21 | Closed period receives import/journal command | Denied; authorized amendment creates successor with fresh required decisions. |
| R2R-AT-22 | Group-only reviewer requests component private artifact | Group result allowed, source file denied; no sensitive filename/details leak. |
| R2R-AT-23 | Missing rate, rate direction reversed, unsupported ownership | No final figures asserted; clear bounded method/input requirement. |
| R2R-AT-24 | Intercompany mismatch or overlapping journal source inclusion | Unmatched difference visible; duplicate consumption rejected; no plug. |
| R2R-AT-25 | Component package replaced during group approval | Manifest mismatch blocks old run; explicit new pin/run required; old group result retained. |
| R2R-AT-26 | Entity close/group close sequencing | No mutual prerequisite deadlock; reviewed components feed group work without inventing approvals. |
| R2R-AT-27 | Upgrade prior schema with ambiguous/null context | Data preserved, unresolved links quarantined, no fabricated approval/currency/book. |
| R2R-AT-28 | Restore DB/artifacts and resume interrupted durable work | Exact manifest/digest reconciliation; stale leases fenced; no duplicate external effects or falsely completed operations. |
| R2R-AT-29 | Keyboard-only and responsive forms | Labels, field/summary errors, focus, line add/remove, dialog cancellation and dirty navigation usable without losing context. |
| R2R-AT-30 | Entity and group package delivered to existing release/archive contracts | Exact sealed/reviewed artifacts handed off; no source recomputation, Purview requirement or signature-provider side effect introduced. |

These supplement, not replace, the original VP criteria and existing repository tests. Automated source mentioning a test ID is not evidence that the complete journey ran.

<a id="section-7-3"></a>
### 7.3 Verification layers and evidence ledger

Use xUnit for pure/domain calculations and actual PostgreSQL integration for FKs, uniqueness, triggers, transaction isolation and concurrency. An InMemory provider cannot establish those claims. bUnit covers component parameters, cascading context, EventCallbacks, EditForm validation and transitions; Playwright covers the actual Blazor host with seeded isolated database state, real network/API behavior and downloaded files. [T8](auditsphere-r2r-reference-standards-and-source-register.md#source-t8)[T9](auditsphere-r2r-reference-standards-and-source-register.md#source-t9)

Use accessible role/label locators and auto-retrying assertions, not sleeps or UI force actions that conceal application defects. Keep fixture setup separate from user actions being verified. Calculation expectations must be independently derived, not `expected = implementation.Calculate(...)`.

| Evidence field | Required value |
|---|---|
| Requirement | Original VP/AC ID plus new R2R rule ID where applicable |
| Source | Exact code commit and migration schema |
| Scenario | Fixture/version, role/person, scope, input/source/artifact identities |
| Assertion | Named method/browser step; expected and observed result |
| Execution | Command, run ID, environment, timestamp, outcome; not merely a filename |
| Rework/security | Appropriate denial, stale, retry, concurrency and historical-preservation checks |
| Review | Independent reviewer and decision, blockers, deferred approved scope |

An assembly/module can be declared complete only when its own positive/negative/rework criteria and consuming-module contract tests pass. Compilation, lines of code, test count, screenshot count or a manually labelled “Verified” badge is insufficient.
