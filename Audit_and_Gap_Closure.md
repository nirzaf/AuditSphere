# AuditSphere — Current Code Audit and Production-Readiness Gap Closure

**Evidence boundary.** The branch was rechecked before completion and still pointed to the audited commit. This review reads pinned repository code, migrations, test definitions, requirements/ADR maps and existing GitHub Actions results. It does not execute a new .NET/Angular/PostgreSQL/browser test suite, certify every line of the repository, inspect an operator's uncommitted work, apply migrations, test a production tenant or grant professional approval. A local Git read could not resolve the GitHub host; connected GitHub reads remained available. Code counterexamples below are reasoned from actual guards, not represented as newly executed repository tests. [H01] [H02] [CI01] [CI02]

**Execution instruction.** Recheck the authorized branch before implementation, read `AGENTS.md`, preserve the original requirement/ADR authority and implement only the remaining delta. Record verified completion in the existing task/status authorities. Finding this document, or its merge codeword, is not permission to modify production, issue customer reports or merge a PR.

## Navigation

[1. Verdict and progress](#1-verdict-and-measurable-progress) · [2. Findings](#2-prioritized-audit-findings) · [3. Engineering contract](#3-shared-engineering-contract) · [4. Remaining stories](#4-residual-stories-cp-00-through-cp-25) · [5. Acceptance scenarios](#5-end-to-end-and-adverse-acceptance-scenarios) · [6. Production gates](#6-production-readiness-and-operational-acceptance) · [7. Delivery order](#7-dependency-ordered-delivery-and-definition-of-done) · [Appendix A. Inherited criteria](#appendix-a-original-reporting-acceptance-criteria) · [Appendix B. Sources](#appendix-b-source-register)

# 1. Verdict and measurable progress

## 1.1 What the current implementation supports

AuditSphere is not an empty prototype. Its repository contains substantial client/commercial management, acceptance and activation, staffing, task templates and queues, time and billing, client PBC collaboration, accounting intake and adjustments, audit fieldwork/review, structured statement rendering, certificate-aware deliverables and local archive controls. The newest work adds a real accepted-GL cash-flow preparation/review subpath. Those existing services must be extended, not replaced. The broad capability map is implementation context, not proof that every acceptance case has passed. [R01] [R03] [R06]

The current result is **neither 100% complete nor merely waiting for a production certificate**. Concrete accounting, source-review, comparative, final-report identity and practical preparation gaps remain. Production identity/provider, operational recovery, independent review and deployment acceptance also remain separately open. [R05] [R09] [R10] [R15]–[R20]

## 1.2 Positive changes since the previous story

| Change | Evidence now available | Completion boundary |
| --- | --- | --- |
| Previous analytics calendar failure addressed | Compare history includes the Qatar-working-week fixture correction; the current CI analytics cohort succeeds | Do not reopen the old `(1440,300,20.8)` mismatch as a current failing test |
| Shared canonical validation completeness | `FinancialPackageValidationReadiness` requires exactly one passing result for each of 11 required codes, in the correct firm/package | Missing/failed/ambiguous canonical results are no longer silently accepted; this is not yet the complete approved reporting profile |
| Accepted-source discovery and actual persisted allocation | GL picker, accepted batch checks, exact line/transaction IDs, allocation bounds and stored source hashes | Cash-event counterpart amounts are projected separately from debit-positive source amounts; indirect classification remains policy-dependent |
| Cash-flow source projection | `ProjectCashFlowContribution` reverses ledger sign for cash movements and retains it for noncash rows; tests cover receipts, equipment payments, noncash purchases, compound journals, transfers, omitted events and period-specific sources | This closes the original sign/population counterexample for the implemented contract; it does not define an approved indirect method or prove all schedule roles |
| Independent cash-flow source review | Separate reviewer approves the exact link digest and prior decision; review is bound to original request identity and the package/client/source locks; authority is rechecked after locks | Independent source review is now revision-fenced; it does not approve the accounting classification or methodology |
| Stronger pinned source identity | `auditsphere.statement-source-manifest.v3` and conditional `.v3.reviewed` include allocation and review facts | Preserve previous artifacts; a new hash does not prove that the chosen accounting interpretation is correct |
| Product build and receipt workflow | Angular form submits optional comparative package, equity rows, numeric note rows and FX effects together with cash movements; durable operation polling and package navigation exist | Assessed-zero FX evidence/review and source-backed equity/note schedules are still absent; the form's entries alone do not establish a complete reviewable report |
| Green exact-head CI | All three configured jobs and their required build/drift/filtered browser steps succeeded | .NET suites are filtered; this is not the full CP-00–CP-25 acceptance or a production deployment test |

Evidence: compare metadata, shared evaluator, allocation/review services, manifest, frontend and exact CI definition/results. [H02] [R09] [R10] [R13] [R14] [R20] [R21] [R23] [CI01] [CI02]

### Revalidation of cash-flow and statement findings — candidate `1ee3c0e7` (2026-10-17)

The following dispositions supersede the baseline descriptions in F-01 through F-04 and F-06 only to the extent stated here. The unfiltered Domain suite and Angular full-spec job passed for this candidate; the unfiltered API and Playwright jobs were still running when this note was prepared. This is not an overall release or production-acceptance verdict.

| Finding | Current disposition | Evidence and remaining boundary |
| --- | --- | --- |
| F-01 | The cited receipt/equipment sign contradiction is corrected in the accepted-source projection path. | `ProjectCashFlowContribution` separates debit-positive ledger evidence from cash-flow effect. Domain regressions cover customer collections, equipment payment, noncash purchase, compound journal, internal transfer and omitted cash event. Do not close the whole finding until the remaining F-01 transaction classes and approved classification rules are evidenced. |
| F-02 | The source-review command binds request identity, link digest and expected previous review under the documented client/package/grant lock order, then rechecks authority. The revocation-during-lock-wait gap is closed in code and current-candidate CI. | The unfiltered Domain job passed `SourceReview_RevokedActorDuringLockWait_RecordsNothing`: it observes the decision waiting on the package lock, revokes the reviewer role while retaining a non-review grant, then verifies `ScopeDenied` and no review row. Stale digest/previous decision, concurrent approve/return and replay cases also passed. This closes the timing-test blocker only; it does not close the remaining source-kind/provenance acceptance. |
| F-03 | Current and comparative ledger selection is period-bound in the source-resolution path. | `CashFlowContractTests.TwoPeriods_RetainTheirOwnAcceptedCashSources` passed in the unfiltered Domain job. This does not close comparative statement completeness as a whole. |
| F-04 | The Angular build request now carries optional comparative-package evidence and entered equity, numeric-note and FX-effect rows; the older statement that it sends those fields as `null` is stale. | `financial-package-build.ts` maps these entries and its spec validates exact request serialization. The UI still lacks assessed-zero FX evidence, the equity/note amounts remain preparer-entered rather than accepted-ledger-linked, and no browser journey establishes full final-package readiness. |
| F-05 | OPEN / BLOCKED_PROFESSIONAL. | No approved indirect cash-flow classification profile or qualified owner decision is present. Do not infer one from the source-projection tests. |
| F-06 | Independent review of exact draft content now covers note/disclosure content and explicitly identified manual equity evidence. | `FinancialStatementService.ContentReview.cs` pins the review to the draft snapshot and never labels manual evidence as ledger-derived. Accepted-ledger lineage for equity and approved disclosure applicability remain open. |

Candidate evidence: [full release verification run 39003733534](https://github.com/nirzaf/AuditSphere/actions/runs/39003733534), SHA `1ee3c0e7ed743a3b2a838cad102c76428e07c267`. The Domain suite and Angular full specs passed; API and Playwright were not yet terminal when this entry was drafted.

The earlier signed-equity correction is also retained. Current/prior signed totals cannot pass merely by matching absolute magnitude. It is a completed correction, not an outstanding defect. Equity source evidence and component review remain different requirements. [R16]

## 1.3 How much is complete?

A defensible **product-completion percentage is not established by the available evidence**. The previous story has 225 criteria, but no current, independently reviewed matrix proving execution of all 225 was obtained. Counting 42 commits, 11 validation codes, routes or passing test cases would give a misleading percentage. The retained criteria include compound behaviors and conditional service-profile branches, so they are not equal units of development effort.

Use three separate measurements in the existing acceptance register:

1. **Implemented capability:** exact behavior present in code and exposed through an authorized user/API path.
2. **Verified acceptance:** the requirement's positive and adverse assertions executed on an identified candidate and independently reviewed.
3. **Production acceptance:** named methodology, live infrastructure, operator and professional gates accepted for the chosen profile.

A metric may be published only after its full requirement denominator is fixed and statuses are evidenced. Required items cannot be removed merely because they are blocked. Approved conditional exclusions retain their authorization, disabled behavior and effect on the denominator. Until then, use the following maturity view rather than an invented “80%” estimate.

| Area | Current assessment | Principal completion work |
| --- | --- | --- |
| Commercial, client acceptance and activation | Substantial implementation; residual verification and decisions | Complete exact-offer/recipient/response evidence and real-role commercial acceptance |
| Staffing, time, finance and firm reporting | Substantial implementation; broader acceptance incomplete | End-to-end billing/reversal/period-cutoff and scoped analytics verification; preserve the calendar fix |
| Seven-phase workflow, templates and recurring engagements | Implemented foundations; partial completion | Remaining policy-driven deadlines/notifications, recurrence edge cases and phase-wide acceptance |
| PBC, document evidence and portal | Implemented local/development paths; production validation pending | Provider reconciliation, direct-site permission truth, production isolation and failure recovery |
| TB, adjustments, mappings and account traceability | Implemented core; full source-to-report evidence incomplete | Realistic accepted GL/TB fixture, all contributing adjustments and approved opening/current/prior linkage |
| Cash-flow preparation | Accepted-source sign projection, period selection and exact independent review are implemented and covered by Domain regressions | Complete approved indirect roles, assessed-zero FX, source-backed schedules and remaining CP-11/CP-12 acceptance |
| Equity, notes and comparatives | Partial | Source-backed reviews, required disclosure applicability and two-period reporting continuity |
| Final formatted report approvals and issue | Partial | Exact composition, required artifact set and decisions through signing/release/archive |
| Release engineering | Exact-head configured CI green | Full required regression and candidate-specific independent/security/migration evidence |
| Production operations | Not accepted by this audit | Live identity/provider/checkpoint, secret rotation, recovery/load/alert drills and operator/professional acceptance |

This table distinguishes implementation from verified readiness. Broader practice gaps that have not been individually re-executed are specified below as verification work or documented-open decisions—not asserted to be absent code. [R03]–[R08] [CI02]

## 1.4 CI and governance facts

The current run `38609444242` succeeded for Docs Health, Angular build/tests and the .NET Release job. The latter also passed its configured focused Domain/API regressions, EF model drift, selected real API/browser journeys, standalone API/Angular publication and OpenAPI drift. [CI01] [CI02]

The CI file contains explicit filters for the .NET Domain, API and E2E commands. It does not execute the full solution test command required by the overall release contract. In particular, the filter does not explicitly include the complete `CashFlowContractTests` or `FinancialStatementPresentationTests` classes; incidental execution through other tests does not replace their full assertion coverage. Do not copy the newest handoff's earlier-run counts into a claim about this exact run without reading its corresponding log. [R01] [R23]

The inspected branch metadata reports `protected: false`, and the repository ruleset API returned an empty list with parent rulesets requested. No enforced PR approval/status-check rule was observed through those endpoints. This is a governance gap to resolve under owner authorization, not permission for this review to change repository settings. [H01] [G01]

The top-level `status.json` verification snapshot remains older than this HEAD. Newer targeted entries and GitHub Actions are separate evidence. Older tracker paragraphs saying “discovery only” or “all cash evidence is free text” are superseded by the newer allocation/review code. Reconcile the wording without deleting historical evidence or claiming a full release pass. [R06]–[R10]

# 2. Prioritized audit findings

**Priority convention:** P0 = release-blocking correctness, authority or evidence risk for the intended profile; P1 = mandatory functional/operational closure; P2 = optional or bounded improvement only when the approved profile requires it. Priority does not assert that an issue has been exploited in production.

**Evidence convention:** `SOURCE_CONFIRMED` is directly visible in pinned code; `STATIC_COUNTEREXAMPLE` follows from those guards without executing the repository; `DOCUMENTED_OPEN` is an explicit source backlog/owner gate; `VERIFICATION_GAP` means adequate current execution evidence was not obtained. No finding in this document claims a newly executed production exploit.

## F-01 — Ordinary cash transactions cannot use the new source-sign contract correctly

**P0 · STATIC_COUNTEREXAMPLE · CP-11/CP-12; CP-09/CP-25 regression impact.**

`ValidateCashFlowSourceAllocationShape` requires the total allocated contribution to stay within the movement's magnitude and have its sign. `ResolveCashFlowSourceLinksAsync` also requires every contribution to have the source GL line's sign, and rejects every selected cash-account line. `ResolvedCashFlowMovements` then requires the contributions to sum to the movement. These constraints conflict for a normal cash transaction under the repository's debit-positive convention. [R09]

Consider only this valid QAR 25,000 journal:

| Line | Signed ledger amount | Intended cash effect |
| --- | ---: | ---: |
| Debit equipment | +25,000 | Investing outflow is -25,000 |
| Credit bank | -25,000 | -25,000 |

Linking the bank line is refused because it is a cash account. Linking equipment as +25,000 fails the negative movement-sign check. Linking it as -25,000 fails the positive source-sign check. Thus the simple journal cannot obtain the correct source-linked -25,000 movement through this contract. A cash collection with a credit receivable counterpart has the inverse problem. This is a code-guard counterexample, not a claim that a new application test was executed.

The blanket assertion that a cash-account line is an internal cash transfer is also insufficient: ordinary customer receipts and equipment payments have cash lines. An internal transfer must be established from its cash-to-cash journal/population relationship, not the mere presence of a cash account. The inspected resolver does not prove that a chosen noncash counterpart belongs to a cash transaction before accepting it as source support. [R09]

**Required correction:** retain signed ledger provenance separately from its accounting-role-specific cash-flow contribution. Prove cash-event membership and allocation using the approved method. Use explicit projection roles; do not simply negate every source, because indirect add-backs, working-capital adjustments and disclosure-only noncash events have different meanings. Preserve old revisions and introduce the smallest version-compatible representation needed.

**Required tests:** balanced imported cash receipt; cash equipment purchase; cash supplier payment; loan receipt/repayment; internal bank transfer; noncash equipment purchase on credit; compound journal with fees; partial allocation; reversal; duplicate use. Verify both exact source signs and final cash effects. The first two tests must expose the current contradiction before implementing the correction.

## F-02 — Independent source decisions have no original-intent or expected-review fence

**P0 · SOURCE_CONFIRMED contract omission / race verification required · CP-11/CP-19/CP-25.**

`RecordCashFlowSourceReviewAsync` takes package ID, decision and reason but no original request identity, expected review sequence or expected source/allocation manifest. Each successful call creates a new GUID/timestamp and appends a row. Its inspected API path delegates through `CommandAsync`, which resolves the actor and validates antiforgery but does not add an idempotency receipt or enclosing publication transaction. The migration protects updates/deletes, but does not give this command an original-intent uniqueness key. [R10] [R12] [R26]

A repeated approval request can therefore create another decision rather than recover the original result. Because the presentation manifest includes exact review IDs and timestamps, this can change presentation identity and invalidate a pin even when a retry intended no new professional decision. The pre-write selected-source read is also not serialized with a concurrent source replacement/revocation in this method. These are source-level observations; adverse runtime tests must establish the precise reachable outcomes. [R10] [R13]

**Required correction:** reuse the repository's actor-owned request receipt and expected-revision pattern. Bind package revision/generation, selected current/prior source identities, allocation-manifest digest and expected previous decision. Reauthorize and recheck them under the existing lock order inside the owned decision transaction. Replay of the same intent returns the original decision; a return or reapproval is a distinct reviewed intent. Reject changed payload on the same key. Preserve history.

**Required tests:** lost reply and same-intent replay; same key/different decision; concurrent approve/return; changed accepted source between preview and commit; grant revocation while waiting for the lock; no duplicate review/hash change on recovery. Add appropriate source/actor/scope foreign-key or guarded-reference tests after checking all current configurations, not only the two new migrations.

## F-03 — Current and comparative source reviews share an engagement-wide “latest GL” lookup

**P0 for two-period final reporting · STATIC_COUNTEREXAMPLE · CP-10/CP-11/CP-17.**

The package builder requires a comparative in the same engagement. `CashFlowSourceApprovedAsync` finds the latest accepted GL using firm/client/engagement/source kind, without a period/book predicate, and requires a package's allocations to cite that batch. `CashFlowSourcesIndependentlyApprovedAsync` applies that check to both current and comparative packages. [R10] [R15] [R18]

For two valid periods in that engagement with distinct accepted GL batches, accepting the current year's batch makes the prior package's batch cease to be the engagement-wide latest. The prior approval cannot remain current under that helper even though it is the exact intended approved comparative basis. A separate prior engagement is currently blocked by package construction. This identifies a two-period contract conflict requiring a representative test; it is not proof that all single-period reporting fails.

**Required correction:** resolve source acceptance within the exact reporting context, or consume a narrowly authorized, immutable as-issued prior reporting snapshot under the approved comparative policy. Do not globally remove client/engagement checks or disable stale-source protection. Normal current-year ingestion must not impersonate a restatement of the selected prior-year source.

**Required tests:** two years with distinct GL batches and reviewed sources; current-year replacement; genuine prior-year restatement with explicit reselection; retained as-issued comparative; unauthorized prior engagement; no inheritance of prior audit/management approvals.

## F-04 — The new build screen cannot submit a complete financial-statement set

**P1 · SOURCE_CONFIRMED · CP-11–CP-14/CP-18/CP-25.**

`toBuildRequest` in `financial-package-build.ts` sends `equityLines: null`, `noteLines: null` and `fxCashEffects: null`. It offers cash movements, descriptive disclosures and source allocations, but cannot supply all of the schedules required by the parent story. A build-completed receipt proves the worker completed its operation; it does not prove a complete reviewable financial report. [R20]

The browser test exercises that cash-source subpath and asserts stored allocation/source review and lineage. It does not assert completion of all financial-statement validations, the complete ABC fixture, final composition or audited issuance. Its supporting fixture seeds a single debit revenue GL line described as cash collections rather than importing a balanced receipt journal. Keep it as a narrowly labeled control fixture; do not treat it as complete economic or source-intake acceptance. [R21] [R22]

**Required correction:** add or compose the existing equity, numeric note, comparative and assessed-zero/FX editors, independently reviewed schedule selection and complete final readiness. Preserve the useful GL picker and exact-decimal request. Run a full journey using actual balanced input and declare every seeded prerequisite.

## F-05 — Indirect cash-flow methodology and explicit zero states remain incomplete

**P0 for final cash-flow approval · SOURCE_CONFIRMED / BLOCKED_PROFESSIONAL · CP-12.**

The presentation sums movements excluding `IsNonCash`; its completeness still requires an FX row. This does not express a policy-approved profit starting subtotal, distinct noncash add-backs, supported working-capital decomposition, or independently assessed zero/no-FX facts. The new source review improves evidence linkage but does not approve that missing methodology. [R16] [R06]

**Required correction:** implement the approved indirect profile with exact statement-start, role-specific transformations, genuine cash and noncash distinctions, source allocations, separate FX effect and independently reviewed no-activity decisions. Reject residual plugs and double-counted impairment. A missing source/section remains missing; an evidenced zero is explicitly zero. Unsupported direct methods remain disabled, not guessed.

## F-06 — Equity and note content are still not independently source-approved

**P0 for final statements · SOURCE_CONFIRMED · CP-13/CP-14.**

The builder still deliberately emits failed `EQUITY_SOURCE_LINEAGE` and `NOTE_CONTENT` checks. Equity signs are corrected, and note numbering/arithmetic exist, but there is no bound approved disclosure-applicability/independent-note-review fact in this presentation path. [R16]

**Required correction:** extend existing evidence and schedule records for source-backed component movements, opening continuity, declared profit-closing convention, specialist tables, logical note IDs, applicability and independent review. Support manual-evidenced schedules only under an approved provenance policy. Do not make these checks pass merely by changing a constant or copying a sample narrative.

## F-07 — Canonical completeness now exists; final-profile completeness still does not

**P0 · SOURCE_CONFIRMED · CP-00/CP-17.**

The old missing-result problem is corrected for the evaluator's 11 canonical codes. However, the package still marks no comparative as “not required for this input”, no numeric note lines as “not applicable”, and disclosure presence through supplied supplementary data. Those are not substitutes for an approved profile's mandatory comparative and disclosure population. The shared evaluator does not itself bind all method/applicability/content/independent-review facts to an approved final policy version. [R14] [R15]

**Required correction:** extend the existing authority with approved profile/rule-version membership and exact input/decision manifests. Missing required content must fail even when the submitted input omits it. Use the same final-purpose decision across review, composition/seal, signing and release, inside their proper transaction fences. Preserve honest draft rendering and old historical files.

## F-08 — Final report composition and exact formatted-artifact approval remain disconnected

**P0 · SOURCE_CONFIRMED · CP-18/CP-19/CP-20.**

**Baseline finding:** presentation pins were created as `UnapprovedDraft`; formal package decisions selected `FinancialPackageArtifactVersions.Text`; and the bundle resolved the standard released `FinancialPackageArtifactVersions.Pdf`, not an expressly selected final presentation composition. These were useful, separately controlled paths, but did not establish approval and issue of the new intended formatted report. [R17]–[R19]

**Implementation update — 2026-10-15, master `7023a9f4`:** the candidate's PDF, DOCX and XLSX are sealed as one immutable composition. In the candidate-bound review path, Management, Accounting and Partner decisions carry the exact candidate revision, composition ID and manifest digest, and each stage must follow the same composition. Release issuance pins the composition ID/digest and selected PDF identity/version/hash; it refuses legacy-PDF fallback. The audit bundle revalidates source currency and the three artifact hashes, includes the exact selected PDF/DOCX/XLSX, and records their IDs and hashes in its manifest. Historic releases without a composition remain readable but cannot be replayed as composed releases. The additive migration is generated and model-checked; it has not been applied to a persistent database or the VPS.

This closes the F-08 composition-to-review/release/bundle wiring. F-08 remains **PARTIAL**: format-content parity and document-quality acceptance, complete approved report-profile membership, management signatory/capacity evidence, revision/receipt recovery, signing/legal acceptance and live-provider gates remain open. Focused verification: Release build 0 warnings/errors; 35/35 PostgreSQL-backed Domain tests; Angular 127 files / 736 tests; OpenAPI, EF model-drift and documentation gates pass. Interactive built-in-browser acceptance and full Domain/API/E2E suites were not run.

**Remaining correction:** complete professional and output-profile approval, validate that the required formats contain matching approved content and references, and exercise malformed/clipped/missing-output refusals. Resolve the selected composition through final signing/delivery without substituting the legacy PDF. Preserve canonical text in the evidence chain and retain its historical approval meaning. A changed banner, note, layout or report date creates new bytes and requires the appropriate successor/review before issue.

## F-09 — Partner-stage role contract needs an explicit policy-aligned denial test

**P0 authority review · SOURCE_CONFIRMED allowlist / reachability verification required · CP-01/CP-19.**

`FinancialPackageReviewService` includes `Administrator` in `PartnerRoles`. The parent story and ADR register require Partner-only actions not to derive professional authority from administrator access. This is a source-level policy/allowlist discrepancy, not a demonstrated production bypass; verify the entire command/assignment/authorization/database path with an administrator-only actor. [R18] [R02] [U01]

**Required correction where reachable:** require the applicable deciding Engagement Partner authority for professional Partner decisions, while preserving legitimate administration and historical reads. Do not simply remove all administrator access from unrelated configuration or review tasks. Test wrong Partner, unassigned Partner, administrator-only, multi-role self-approval and revoked assignment. Offline management evidence also needs actual signatory/capacity and verified capture identities, not staff impersonation through a mode field.

## F-10 — Commercial identity, routing and broader practice completion still need closure

**P1; P0 when confidential dispatch/consent is affected · SOURCE_CONFIRMED fallback plus DOCUMENTED_OPEN/VERIFICATION_GAP · CP-02–CP-08/CP-15/CP-16/CP-22/CP-23.**

The baseline fallback offer hash used proposal/revision, optional `CreateRequestHash`, quotation references and amounts/currency. It did not independently enumerate all entity/scope/period/terms fields. The current dispatch path now requires the exact current rendered quotation and immutable response provenance; legacy or insufficiently evidenced offers remain blocked. Broader recipient-purpose routing and proposal-content acceptance remain open. [R25] [R28]

The v2.2 map and code map identify residual routing, professional template content, response history, assignment/recurrence/deadline and cross-workbench acceptance work. Some old “missing” views, reminder mechanisms and analytics definitions already exist. Re-inspect each affected command before implementation; broad gaps below are not blanket claims that CRM, billing, planning or audit programs are absent. Preserve ADR-0017/0018 decisions and existing task/PBC/reminder work. [R02]–[R04]

**Required closure:** exact recipient/offer intent, authentic append-only consent, service-specific content approval, safe annual recurrence and work queues, complete fieldwork/rework/confirmation evidence, correct firm-finance cutoffs and a current per-requirement UI/API acceptance map.

## F-11 — Green filtered CI is not the release verification contract

**P0 release evidence · CI_OBSERVED / VERIFICATION_GAP · CP-00/CP-25.**

The new exact-head run is green; the old analytics failure is historical. Its .NET commands are explicitly filtered, and the source-linked browser scenario is not the full source-to-audited-report scenario. No new full-suite run or complete 225-criterion acceptance execution was performed by this audit. [CI01] [CI02] [R21] [R23]

**Required closure:** execute the full required suites on one candidate; preserve fast focused PR checks separately; add realistic source-sign/two-period/review-replay/complete-report tests; identify all seeded prerequisites; parse and inspect generated artifacts; obtain independent review. A filtered pass must not overwrite full-suite evidence with an inaccurate “all tests passed” claim.

## F-12 — Production governance, provider and recovery gates remain open

**P0 production gate · DOCUMENTED_OPEN plus observed repository settings · CP-01/CP-08/CP-20/CP-21/CP-25.**

No branch protection/ruleset was observed. Production acceptance is not established for the required live identity/provider/checkpoint, separate-store recovery, secrets/observability/capacity and operational pilot gates. Some selected-site behavior has been exercised in Development; it must not be described as nonexistent or automatically production approved. Production Data Protection configuration already includes persistent encrypted keys and certificate checks; the remaining task is correct deployment/rotation/recovery evidence, not invention of a second key service. [H01] [G01] [R05] [R24]

**Required closure:** the named gates in section 6, through authorized operators and independent owners. Preserve the deliberate distinction between application isolation and the approved staff Full Control on a client SharePoint working site. Local terminal freeze cannot prove provider-side retention against direct site edits. [R01] [R02] [R05]

# 3. Shared engineering contract

This section is a proposed implementation contract resolving the findings. It does not assert that these additions already exist or approve any accounting treatment.

## 3.1 Preserve the product and authority model

Retain all five STE modules, the eleven evidence-derived lifecycle states and the seven user-facing engagement phases. The eight reporting stages are a nested preparation/readiness view, not a new workflow authority. Operational completion percentages never manufacture client acceptance, paid advances, materiality approval, an audit opinion, a signature or a release.

Keep the .NET modular monolith, static capability services, existing `CommandResult` conventions, PostgreSQL module partials, Angular same-origin API, exact decimal strings and durable operation infrastructure. Keep the Web rollback host until the separately accepted retirement decision. Firm books, client books and consolidation remain separate. No second ledger, renderer stack, generic workflow engine, arbitrary formula execution or unnecessary dependency is authorized. [R01] [R02] [U01]

Do not reintroduce temporary-password email, an outbound WhatsApp integration, normalized PBT, automatic invoice posting, guessed standard working days, Purview, third-party eSignature or the excluded operational ERP/payroll/payment/tax-filing scope. No malware-scanning subsystem is added by this handoff. Required safe file parsing, bounded inert rendering, protected storage and deployment security remain within their existing responsibilities.

## 3.2 Separate source consumption from accounting projection

Use existing records first; add a versioned field or related revision only where the current model cannot express the meaning safely. The following are conceptual fields, not mandatory new type names:

| Fact | Purpose |
| --- | --- |
| Exact context | Firm, reporting entity/client, engagement, period, book, basis, currency and source mode |
| Source identity | Accepted source decision, immutable batch/row/journal or approved adjustment/schedule version, digest |
| Source signed amount / consumed portion | Original debit-positive ledger provenance and bounded allocation; never mutate it to match presentation |
| Contribution role / policy revision | Starting profit, noncash add-back, working-capital change, investing/financing cash, FX effect or disclosure-only event |
| Projected statement contribution | Deterministic signed amount produced under that approved role; separate from ledger sign |
| Cash-event/population proof | Relevant cash legs, counterpart allocation, transfer pairing and noncash exclusion when a cash movement is claimed |
| Manual evidence provenance | Exact verified document version, preparer, method and independent approval when permitted; never labeled GL-derived |
| Review and currentness | Exact schedule/allocation manifest, reviewer, prior decision sequence, source changes and successor relationship |

A simple counterpart projection may reverse the ledger sign for a proven cash event, but that is not a universal rule for all indirect-method rows. Noncash depreciation participates in the profit reconciliation; noncash equipment acquisition is excluded from investing cash and separately disclosed. Working-capital movements require opening/closing support and removal of acquisitions, transfers, FX and noncash components where applicable. A closing TB alone cannot supply those facts.

Prove population completeness as well as per-line allocation limits. An underallocated or omitted cash event cannot be hidden by a compensating row. Internal transfers must not inflate operating/investing/financing activity. Gross-receivable and net-receivable approaches must not be mixed with a duplicated impairment add-back. Approved zero and not-applicable records are separate from null or an empty collection.

## 3.3 Request, review and publication identity

Use the repository's existing metadata/receipt patterns. Every protected revisioned write carries the applicable original `RequestId`, expected subject revision/generation, selected source/composition digest and expected prior decision. These are checked on the server, not just in Angular.

For source/schedule/report decisions: resolve actor -> authorize scope -> acquire established firm/client/engagement/subject locks in the existing order -> reauthorize after waits -> verify exact inputs/currentness -> resolve same-intent replay -> append decision and receipt atomically -> return the retained result. Same key/different payload conflicts. Opposing concurrent decisions must not silently win by wall-clock ordering. Return/reapproval creates a distinct intent over the correctly reviewed version.

Rendering captures immutable inputs in a short transaction, performs expensive document work outside long-held locks, then rechecks authority/currentness and publishes the exact result. No completion callback may substitute newer input or cross a route/period boundary. Historical byte retrieval is an authorized read, not a re-render or new signing request.

Inspect all current migrations/configurations before adding constraints. The inspected new cash tables protect historical updates/deletes, but their shown foreign keys do not themselves prove all source-line/transaction/batch/user scope and publication rules. Add only missing composite references/guards, actor proof and request uniqueness. Test raw invalid insertion as well as API refusal. Upgrade existing missing preparer/source metadata as explicitly unproven; never invent historical approval.

## 3.4 One policy-bound final-readiness result

Extend `FinancialPackageValidationReadiness` and existing validation records; do not discard its missing/failed/ambiguous protection. An approved final profile identifies all mandatory rules, rule versions, required statement/note membership and permitted exceptions.

The final result binds exact accepted sources, adjusted plan, mapping, current/prior selection, schedule/zero/applicability reviews, layout/composition and calculator/policy revisions. It must prove at least the applicable accounting equation, complete account contributions, profit/OCI transfer, signed equity reconciliation, cash bridge/populations, note totals and independently approved disclosure content.

No caller can make a mandatory comparative or note optional by submitting `null`. A required rule needs exactly one current eligible result or the explicitly permitted reviewed exception; absence and unknown rule versions fail. Technical build completion, source-review approval, accounting statement readiness, audit completion, signature verification and delivery receipt remain separate statuses.

## 3.5 Exact report identity from preparation to archive

Keep three distinct identities, using existing manifests and stores:

1. **Content basis:** immutable accounting result, policy, schedules, current/prior references, ordered composition, locale/branding/fonts/template and renderer profile before rendering.
2. **Selected artifact set:** exact ordered IDs/formats/sizes/hashes after all required unsigned outputs are validated. Approvals bind the exact intended composition and artifact set, not merely a loosely related text artifact.
3. **Signed issue artifact:** the permitted signature operation produces new exact bytes linked to the approved unsigned candidate, signer/certificate facts and verification result. Release pins the signed bytes and required approvals. A retry retrieves the original signed result.

A file cannot contain its own final byte hash as a self-referential value. Do not rewrite a watermark, date, page number, metadata or note after approval/signing without the required successor and review. PDF/DOCX/XLSX share one canonical result and must agree; signing need not make separately executed operations byte-identical because signature metadata may differ. Idempotency must preserve the first successful signed result.

Preserve the existing ZIP and fee/checkpoint sequence. A combined audited PDF is delivered only under an approved selected profile: assemble approved unsigned components, validate page order/content, then sign. Never concatenate already signed documents and claim their previous signature covers the combined bytes. Do not introduce a cycle in which statement release requires a bundle whose existing prerequisites already require statement release and a posted balance invoice.

## 3.6 Practical Angular completion

Extend the current build, package, evidence, review and completion screens. Users must select meaningful periods/mappings/schedules rather than enter unexplained GUIDs, fabricate FX records or repair Word files externally. Provide role-safe draft/save/submit/return/compare/lineage controls and server-derived capabilities. The new cash picker, exact decimal serialization, operation status and original-actor receipt restrictions are retained.

An unavailable editor or required schedule must be visible as a blocker, not silently serialized as null while the page implies a full reviewed build. Carry scope and request identity across refresh/recovery. Context changes clear unauthorized state; late responses cannot restore an old client's data. Keep list/board/calendar totals derived from the same scoped source and use bounded pagination rather than truncated financial aggregates.

# 4. Residual stories: CP-00 through CP-25

The original CP names, criterion IDs and criterion wording below are retained. This is an updated implementation delta over those requirements, not a new parallel backlog. Each criterion remains mandatory according to its original applicable scope. A corrected historical defect is credited in the status/delta; retaining the original acceptance sentence is not reopening that defect.

**Reading rule:** `SUBSTANTIAL CORE` recognizes real existing functionality but is not whole-package completion. `DOCUMENTED_OPEN` and `VERIFICATION_GAP` require focused reinspection before new code. Each slice must deliver its required Application/API/Angular/persistence/test changes together. Detailed inherited WP acceptance is also retained in Appendix A.

## CP-00 — Verified baseline, approved profile and requirement closure

**Priority:** P0. **Current disposition:** PARTIAL; current configured CI green; professional and full-release acceptance open.

**Dependencies:** None for inspection; qualified policy owners before dependent final-grade behavior.

**Outcome:** Make completion measurable on an exact candidate without losing any mandatory source requirement.

**Evidence / reuse map:** [R01] [R02] [R06] [R07] [R08] [R23].

### Remaining implementation

1. Reconcile the 26 CP aliases, all retained 225 criteria, existing R2R/AUD/FSR cards and approved STE requirements. For each, record implementation symbol/route, required positive and adverse assertions, current tested source, reviewer, decision and blocker. Do not create a competing task/status ledger.
2. Credit the Qatar-week analytics fix, signed-equity correction, v3 source manifest, shared mandatory-rule evaluator and accepted-GL cash allocation/review journey. Do not rebuild them or copy their old missing-feature descriptions. Qualify older discovery-only tracker entries with dated superseding evidence.
3. Fix the release scope in a versioned supported profile: service, framework edition, period/currency/basis, statement/disclosure set, cash method, evidence modes, comparative policy, delivery form and resource limits. Separate technical configuration from an actual qualified approval.
4. Retain fast filtered CI, but add/execute the existing full-suite release verification and criterion-level journey coverage on one candidate. Preserve failures and declared seeded prerequisites. Update the full verified snapshot only from a full observed run.
5. Record the unresolved professional decisions explicitly. Negative technical tests and source/identity corrections may proceed where task dependencies permit; do not fabricate policy approval to unblock a final report.

**Required focused verification:** A complete evidence matrix links every mandatory criterion to proof or a named open gate; the current green filtered run is not relabeled as full acceptance.

### Retained acceptance criteria

- **CP-00.AC-01:** Every mandatory source requirement and all 69 inherited reporting criteria have an explicit current disposition; none disappears through consolidation.
- **CP-00.AC-02:** The source/profile/decision inventory separates existing functionality, missing code, missing verification and professional/external dependencies.
- **CP-00.AC-03:** The diagnosed CI discrepancy has a justified regression fix or corrected fixture, followed by actual execution of previously skipped required gates.
- **CP-00.AC-04:** Unsupported or unapproved final reporting profiles fail closed; there is no “generic IFRS-approved” default.
- **CP-00.AC-05:** The same source snapshot identifies code, migrations, contracts, test results and required independent review.
- **CP-00.AC-06:** Mandatory blocked requirements prevent the relevant functional/release/production completion claim; an approved deviation retains its rationale and source reference.

## CP-01 — Firm setup, identity, scoped authorization and administrative usability

**Priority:** P0. **Current disposition:** PARTIAL — extensive existing scope controls; F-09 and full parity need verification.

**Dependencies:** CP-00; current administrative and professional role decisions.

**Outcome:** Make a fresh firm configuration and every protected action usable without broadening professional or client authority.

**Evidence / reuse map:** [R01] [R02] [R03] [R05] [R18] [R24] [R26].

### Remaining implementation

1. Reuse trusted actor/session resolution, RoleGrant, administrative setup and current profile/chart/rate/template configuration. Verify a clean installation can perform required setup through supported UI/API instead of test-only SQL.
2. Trace administrator-only access to the formal Partner package decision. Add a direct Application/API regression against the effective ADR/assignment rule; remove the professional-authority shortcut if reachable. Preserve legitimate administration and any separately authorized nonprofessional review capability.
3. Complete grant matrices for firm/client/engagement/group, current and prior-year snapshot access, list/count/export/search and exact-byte downloads. A permission to view a financial summary must not reveal underlying restricted evidence.
4. Test disabled/expired/revoked actor transitions while a command waits for locks, while a worker publishes and while a browser request is in flight. Use the same final authority for can-flags and commands; role-name presence alone is not scoped authority.
5. Verify Microsoft first-sign-in and delegated access, production key persistence, installation separation and recoverable rotation using existing providers. No temporary-password emails or browser-held Graph bearer tokens.
6. Keep the Angular/Blazor source-action migration crosswalk explicit. Retain the rollback host until the actual retirement gate and owner acceptance; no broad UI rewrite is part of this slice.

**Required focused verification:** Administrator-only, wrong Partner, self-review, sibling-scope, revoked-session and queued-worker cases fail safely; fresh setup and retained permitted history still work.

### Retained acceptance criteria

- **CP-01.AC-01:** A clean permitted installation can configure core operating prerequisites through UI/API; no test-only seeding is needed to price, prepare or review real work.
- **CP-01.AC-02:** Wrong-firm, sibling-client and unauthorized sibling-engagement reads/writes return protected errors without exposing data or counts.
- **CP-01.AC-03:** Revoked/expired actors cannot perform new commands, queued publication or protected downloads; browser content clears after the supported reauthorization mechanism.
- **CP-01.AC-04:** A multi-role preparer cannot self-approve or obtain Partner authority through an administrator role or hidden endpoint.
- **CP-01.AC-05:** Microsoft sign-in never issues/emails local temporary passwords; client first-sign-in gates protect applicable upload actions.
- **CP-01.AC-06:** Provider permissions and secrets remain capability-specific; an unavailable optional integration does not masquerade as authorized production setup.

## CP-02 — Client hierarchy, contacts and purpose-based routing

**Priority:** P1; P0 for confidential routing. **Current disposition:** PARTIAL / DOCUMENTED_OPEN — current broad routing acceptance not established.

**Dependencies:** CP-01; approved recipient-purpose precedence.

**Outcome:** Route offers, invoices, requests and final reports to the correct current same-client people.

**Evidence / reuse map:** [R02] [R03] [R04] [R25] [R28].

### Implementation update — 2026-10-15, master `d991ddc6`

Proposal dispatch now snapshots the eligible same-client commercial recipient and binds it to the current approved quotation and its rendered artifact. The broader purpose-based contact model, ambiguity/expiry screens and contact-history workflows remain open. See [R28].

### Remaining implementation

1. Complete create/amend/deactivate and history flows for legal identity, contacts, validity windows, signatory capacity and approved communication purposes. Job-title text is descriptive and must not become an access grant. Preserve the existing parent/subsidiary/affiliate model and distinguish a holding relationship from consolidated-book authority. Test cycles, duplicate links, invalid scope and supported tree depth.
2. Resolve COMMERCIAL, FINANCE, AUDIT_FIELDWORK and final-deliverable recipient rules with the owner. PBC assignment to a portal identity is not automatically equivalent to emailing a routed contact; keep the selected security model explicit.
3. Extend the proposal dispatch’s recipient snapshot and stale-intent protection to the remaining approved communication purposes. Recheck eligibility at dispatch; do not silently replace a queued recipient after a contact amendment.
4. Provide an authorized ambiguity/expiry/remediation screen and safe contact history. Multiple recipients must each be permitted for the document subset. Do not add WhatsApp dispatch from a channel label.

**Required focused verification:** Recipient change, contact deactivation, expired delegation, wrong-client contact, ambiguous role routing and a lost send reply preserve exact intent and confidentiality.

### Retained acceptance criteria

- **CP-02.AC-01:** Client and relationship amendments preserve prior evidence; cycles, wrong-firm relationships and ambiguous authoritative identity are refused.
- **CP-02.AC-02:** Every supported communication purpose selects only currently eligible same-client recipients under the approved precedence.
- **CP-02.AC-03:** An inactive, expired, wrong-client or ambiguous contact cannot receive a newly published protected notification or report.
- **CP-02.AC-04:** A queued intent retains exact recipient/document identity and does not silently adopt changed contact data.
- **CP-02.AC-05:** Delegated portal contact authority is independently verified and revocable; descriptive contact role alone grants no system access.
- **CP-02.AC-06:** Hierarchy, contact history and routing are usable through scoped Angular/API paths, including empty, conflict and recovery states.

## CP-03 — Pricing, commercial documents and exact-offer acceptance

**Priority:** P0 offer identity; P1 content/UX. **Current disposition:** PARTIAL — exact rendered-offer response provenance is implemented for the verified path; broader routing, content and acceptance remain open.

**Dependencies:** CP-00–CP-02; offer identity, wording and acceptance decisions.

**Outcome:** Make client commercial consent refer to the exact offer actually issued, including legacy/fallback cases.

**Evidence / reuse map:** [R02] [R03] [R04] [R25] [R28].

### Implementation update — 2026-10-15, master `d991ddc6`

New proposal dispatches require the current approved quotation and its exact current rendered artifact with a matching SHA-256; an incomplete legacy fallback cannot be treated as an offer. Acceptance records the exact offer digest, frozen recipient, respondent, authority basis, response channel, evidence reference, actor and time in append-only evidence. Identical replay is idempotent; changed replay is refused. Client conversion and fee-agreement progression require this evidence. The worker attaches the exact rendered quotation bytes, and the Angular response form captures the required acceptance evidence. See [R28].

Focused verification at `d991ddc6` passed: Release solution build; 42 PostgreSQL-backed Domain tests; 1 API journey; 1 Playwright commercial journey; 752 Angular tests; Angular production build; EF model drift; OpenAPI drift. The Angular build retains an existing component-style budget warning. No full .NET suite, live mailbox delivery, tenant acceptance, deployment or persistent database migration is claimed.

### Remaining implementation

1. Complete the approved offer-content model across every proposal creation path, including entity, service, period, scope, fee/currency, terms, timeline, pricing revision, template and artifact identity. Historical proposals without sufficient provenance stay blocked; any supported fallback needs an explicit complete-identity rule.
2. Complete purpose-specific recipient selection, ambiguity/expiry preview and remediation for the approved contact model. Recheck eligibility at dispatch and preserve the original intent if contact data changes.
3. Close the enabled brief quotation and five-chapter proposal path using reviewed firm text, exact team/CV/profile versions and mandatory content. Confirm whether DOCX is the approved presentation output; do not add PowerPoint just because a source uses the word presentation.
4. Verify pricing boundaries, independent approvals/revocation, expiry and unknown-send recovery across Angular and API. Record provider delivery only when the provider supplies evidence; the current dispatch receipt does not prove mailbox delivery.

**Required focused verification:** Missing fallback provenance, altered period/terms/CV/timeline, stale price approval, wrong signatory and repeat dispatch cannot create a valid acceptance of a different offer.

### Retained acceptance criteria

- **CP-03.AC-01:** Changing any material offer field creates new identity and requires fresh acceptance before dependent protected actions.
- **CP-03.AC-02:** No-quotation dispatch follows the approved complete-identity rule; incomplete fallback evidence cannot clear the new finalization path.
- **CP-03.AC-03:** Responder, recorder, channel, exact offer and decision time are immutable and attributable; replay cannot accept different content.
- **CP-03.AC-04:** Commercial outputs contain the approved scope, period, fees, terms, timeline and required chapters without invented credentials or unreviewed clauses.
- **CP-03.AC-05:** Revoked pricing/expired offer/wrong recipient/stale revision prevents dispatch or generation; an unknown send outcome is reconciled through the original intent.
- **CP-03.AC-06:** Duplicate requests do not duplicate letters, offers or mail intents; old accepted documents remain readable as historical evidence.

## CP-04 — Due diligence, fresh annual engagement and unconditional activation

**Priority:** P0. **Current disposition:** SUBSTANTIAL CORE; remaining annual and professional evidence verification.

**Dependencies:** CP-01–CP-03; CP-05 paid-advance evidence for activation.

**Outcome:** Preserve new-client and annual due-diligence independence before engagement activation.

**Evidence / reuse map:** [R01] [R02] [R03] [R04] [R05].

### Remaining implementation

1. Reuse the existing two-key gate and current unconditional Partner acceptance. Commercial approval, risk clearance, letter issuance and fully allocated 50% advance remain different facts; no partial payment or operational task completion activates planning.
2. Verify required questionnaire evidence, beneficial ownership/independence/conflict facts and adverse-answer escalation using the selected firm questionnaire version. Review unverified conflict/evidence rows in the requirement map; do not create autonomous AML/KYC conclusions.
3. Complete the prior-engagement to new-period workflow with fresh answers/continuance, exact service-period offer, eligible current contacts/template/mapping suggestions and explicit scope. Copy references only, never old approvals or opinions.
4. Exercise nonstandard year ends, leap dates, changed legal entity/service, duplicate or overlapping period requests, abandoned drafts and source changes during annual creation. Business-calendar behavior comes from the approved rules, not arbitrary date offsets.
5. Verify acceptance/activation commands under real distinct role identities, stale previews, returned/conditional decisions and revoked staff. Preserve the existing reviewed shell/activation receipts and narrow future work to unresolved cases.

**Required focused verification:** New and recurring engagements traverse the two independent keys and full advance gate without SQL status edits; rejected, conditional, stale or duplicate inputs remain blocked.

### Retained acceptance criteria

- **CP-04.AC-01:** Neither key, only one key, conditional/stale Key 2 or changed Key 1 blocks letter issuance and dependent activation as prescribed.
- **CP-04.AC-02:** Full allocated advance is required in addition to both keys; posted, sent or partly paid invoices do not satisfy it.
- **CP-04.AC-03:** A successor engagement retains prior references but starts with no copied current professional decisions or payment allocations.
- **CP-04.AC-04:** Concurrent annual creation or retry creates one supported successor; overlapping/wrong-client/service/period cases fail safely.
- **CP-04.AC-05:** Required due-diligence evidence is validated; client projections never disclose sensitive AML/KYC or internal risk narratives.
- **CP-04.AC-06:** Actual lifecycle/phase and next action reflect committed current gate evidence, not task progress or a manually assigned stage.

## CP-05 — Firm fee billing, receipts, credits and delivery evidence

**Priority:** P0 accounting/control; P1 notification closure. **Current disposition:** SUBSTANTIAL CORE / PARTIAL provider and lifecycle acceptance.

**Dependencies:** CP-01–CP-04; selected finance and dispatch policy.

**Outcome:** Complete the audit firm fee cycle without mixing firm revenue with the client financial statements.

**Evidence / reuse map:** [R01] [R02] [R03] [R04] [R05] [R19].

### Remaining implementation

1. Reuse BillingService, FeeAgreementService, payment-term revisions, receipt allocation/reversal and receivables aging. Automation creates drafts only; independent finance approval/posting and observed external payment evidence remain mandatory.
2. Run the QAR 20,000 fee scenario: exact offer/letter, QAR 10,000 advance, partial then fully allocated receipt and activation. Complete the separate QAR 10,000 balance workflow at its approved release milestone; do not make the bundle/release dependency circular.
3. Cover currency/rounding/residual policy, duplicate receipt references, overpayment/unallocated cash, credits, reversed allocations, canceled drafts, posted-invoice correction, changed terms and cutoffs. Preserve exact source allocations; do not silently edit a posted invoice.
4. Complete supported invoice/receipt notification intents using approved FINANCE routing and the existing mail worker. Separate queued/provider-accepted/delivered/acknowledged; do not claim delivery without provider evidence.
5. Trace billed/collected/outstanding values and exports to posted documents and effective allocation/reversal history. Test cross-client/fiscal-period requests and original-intent recovery for each state-changing command.

**Required focused verification:** Fee records reconcile, a partially paid advance cannot activate, no duplicated posting/receipt occurs on replay, and firm finance never alters ABC client revenue or profit.

### Retained acceptance criteria

- **CP-05.AC-01:** A QAR 20,000 agreed fee produces exactly QAR 10,000 advance and QAR 10,000 final obligations with no duplicate agreement/milestone on retry.
- **CP-05.AC-02:** Automation produces drafts only; unauthorized/self review, posting and allocation attempts are denied by Application.
- **CP-05.AC-03:** QAR 6,000 allocated toward the advance remains insufficient; a further QAR 4,000 makes payment eligible only when all other current gates pass.
- **CP-05.AC-04:** Duplicate receipt/allocation intent cannot double-post or double-settle; over-allocation, wrong-currency and changed-intent replay fail.
- **CP-05.AC-05:** Credit/reversal history is immutable and reflected in current AR/eligibility without rewriting old issued evidence.
- **CP-05.AC-06:** Invoice/receipt communication uses exact authorized recipients and truthful delivery state; the final fee document cannot be manufactured to bypass release controls.

## CP-06 — Staffing, capacity, milestones, time and budget attribution

**Priority:** P1 with P0 approval/attribution controls. **Current disposition:** SUBSTANTIAL CORE; original calendar failure addressed.

**Dependencies:** CP-01/CP-04; approved staffing and rate/calendar policies.

**Outcome:** Plan people, deadlines and budgets, then report actual time with stable approved attribution.

**Evidence / reuse map:** [R02] [R03] [R06] [R23].

### Remaining implementation

1. Preserve ADR-0017 Sunday–Thursday and ADR-0018 utilization/non-billable/rate-alias decisions. The previous fixture discrepancy is fixed; add remaining date/holiday/leave cases instead of repeating that correction.
2. Verify staffing Partner/Manager/Senior/Staff eligibility, one deciding Partner, availability overlap, capacity bounds, role changes and replacement history. Scheduling a person does not authorize unrelated engagement evidence.
3. Complete milestone and budget editing with reviewed deltas, exact source calendar/period, immutable previous schedules, stale previews and amendment reasons. Confirm nonstandard working calendars through approved configuration only.
4. Retain task/phase/FSLI time attribution pinned to its approved mapping. Exercise time drafts, submit/return, independent approval, non-billable classification, correction and original-intent recovery; mapping changes cannot rewrite historical attribution.
5. Compute capacity, billable utilization, budget variance and standard value from consistent period/clock inputs and captured rates. Keep recorded cost separate from charge-out value and financial margin; missing rates/currency compatibility make the relevant measure unavailable, not zero.

**Required focused verification:** Boundary dates, leave, non-billable hours, role/rate changes, dual assignments and revised mapping produce the approved capacity and financial meanings across UI/export.

### Retained acceptance criteria

- **CP-06.AC-01:** Weekly and department utilization reconcile to approved billable minutes and the same applicable capacity basis.
- **CP-06.AC-02:** Time, quotations and budgets resolve approved role aliases consistently; unapproved/missing required rate data is explicit.
- **CP-06.AC-03:** Non-billable/no-charge time contributes to actual minutes and cost, but not standard billable value or duplicate billing.
- **CP-06.AC-04:** Timesheet corrections retain original actor/rate/FSLI evidence; a changed mapping or assignment cannot silently relabel approved time.
- **CP-06.AC-05:** Leave, statutory-relative milestones, partial weeks, timezone boundaries and overlapping allocations have deterministic validated outcomes.
- **CP-06.AC-06:** Unauthorized staff costs/performance data remain protected in screens, counts, exports and direct API queries.

## CP-07 — Seven-phase cockpit, operational tasks, templates and safe automation

**Priority:** P1; domain gates remain P0. **Current disposition:** SUBSTANTIAL FOUNDATIONS / PARTIAL remaining orchestration.

**Dependencies:** CP-01/CP-04/CP-06; existing workflow/template authorities.

**Outcome:** Finish an understandable seven-phase cockpit without creating a second authority for progression.

**Evidence / reuse map:** [R01] [R02] [R03] [R06].

### Remaining implementation

1. Retain the existing phase mapping, WorkTask, versioned templates/plans, evidence prerequisites, dependency amendments and cycle detection. Verify all eleven states map correctly and task percentage remains distinct from domain readiness.
2. Close remaining task-plan editing, owner reassignment, dependency/evidence template declarations, nonstandard recurrence and supported deadline policies using the current services. New template versions do not rewrite existing plans.
3. Reuse existing My/Team/Waiting for client/Under review/Rework/Overdue/Completed queues, board/calendar and unlinked-procedure inbox. Test matching memberships, source authority, empty states and pagination before asserting that a view is missing.
4. Extend existing default-off PBC reminder/outbox behavior only for missing approved notifications/escalations. Bind original event, recipient and schedule version; suppress stale/disabled/frozen work. No notification can approve a workpaper or execute a protected financial decision.
5. Verify user actions and automation with the feature enabled and disabled, in-flight revocation, canceled tasks, rework resubmission and missing owners. Operational success must not hide an unresolved professional gate.

**Required focused verification:** Completing all tasks leaves unpaid, stale, critical-confirmation and open-review gates blocked; retries and recurrence create no duplicate work or forged domain evidence.

### Retained acceptance criteria

- **CP-07.AC-01:** Every canonical state maps to one permitted phase; unknown/inconsistent values fail closed, and task completion cannot write any stage.
- **CP-07.AC-02:** A fully completed task plan with an unpaid advance, stale mapping, open review note or critical confirmation still shows its blocked domain gate.
- **CP-07.AC-03:** List/board/calendar and reviewer inbox membership reconcile with the same scoped source and stable pagination.
- **CP-07.AC-04:** Cyclic/cross-engagement dependencies and stale template amendments are rejected; existing reviewed amendments/retries retain immutable history.
- **CP-07.AC-05:** A reminder or task event cannot create human approval, payment, opinion, posted invoice, signature or release; replay does not duplicate its effect.
- **CP-07.AC-06:** With workflow-experience features off, underlying professional controls still work and returned procedures cannot bypass rework; enabling is independently tested and approved.

## CP-08 — Client portal, PBC, evidence documents and communication history

**Priority:** P0 privacy/integrity; P1 provider completion. **Current disposition:** LOCAL/DEVELOPMENT CAPABILITIES EXIST; production acceptance open.

**Dependencies:** CP-01/CP-02/CP-04; approved site and portal policy.

**Outcome:** Make PBC collection, conversations and exact evidence versions dependable under real provider failure and release races.

**Evidence / reuse map:** [R01] [R02] [R03] [R05].

### Remaining implementation

1. Reuse PbcService, first-sign-in/delegation, received upload versions, transfer workers and physical/document evidence links. Complete actual per-request/per-upload review/rejection semantics and client-visible mandatory rework reasons.
2. Verify staged -> stored -> provider-readback -> accepted-source transitions separately. A hash from staged bytes or an upload success notification is not independent reviewer acceptance.
3. Exercise large/bounded uploads, duplicates, unreadable/encrypted unsupported files, replaced versions, canceled transfers, malicious filenames and wrong-client metadata using existing safe parsing/storage boundaries. No additional scanning subsystem is introduced.
4. Preserve the approved one-client SharePoint working site and assigned-staff Full Control rule. Document which direct-site access differs from app engagement scoping; verify revocation reconciliation and selected-site grants with the separately authorized provider identities.
5. Test PBC and representation uploads racing report release/freeze. Preserve earlier bytes, append decision history, prevent unauthorized post-release writes and maintain permitted historical downloads.
6. Finish authorized live tenant acceptance for identity, folder provisioning, exact-version delivery, throttling/lost replies and revoked credentials. Development live successes are retained evidence, not automatically production acceptance.

**Required focused verification:** Two-client and sibling-scope tests, lost provider replies, replaced files and release races preserve source identities and access rules; live tests have explicit authorized environment evidence.

### Retained acceptance criteria

- **CP-08.AC-01:** Client A cannot read/list/download/respond to Client B requests or files, including guessed IDs and stale browser state.
- **CP-08.AC-02:** Rejection provides the approved mandatory reason and resubmission path; original document bytes and review history remain intact.
- **CP-08.AC-03:** Upload/release/freeze races cannot admit an unauthorized new version; permitted historical downloads remain separately controlled.
- **CP-08.AC-04:** Stored evidence matches the approved document/version digest and provider receipt where required; missing readback is not represented as acceptance.
- **CP-08.AC-05:** Lost responses/retries create no duplicate provider object or message; unknown/failed/queued/accepted states remain distinct.
- **CP-08.AC-06:** Live tenant/site isolation and revocation are evidenced separately from local/fake-provider tests; direct-site Full Control is not marketed as immutable retention.

## CP-09 — Accepted source, reviewed adjustments and exact mapped-account lineage

**Priority:** P0. **Current disposition:** IMPLEMENTED CORE; realistic full-source acceptance incomplete.

**Dependencies:** CP-00/CP-01; approved import-first or explicitly enabled native mode.

**Outcome:** Prove the exact source-to-adjusted-account-to-statement chain before relying on reporting totals.

**Evidence / reuse map:** [R01] [R03] [R15] [R22] [R27].

### Remaining implementation

1. Reuse accepted TB/GL, chart/taxonomy, source acceptance, GL completeness, journals, reflection plans and mapping. Build representative balanced GL through the actual approved import/acceptance path, not a single raw seeded counterpart.
2. Reconcile opening plus period movements to each TB account and the financial statements. Verify correct period/book/basis/currency and source mode; a balanced total alone must never be labeled complete evidence.
3. Exercise all accepted adjustment treatments: applied exactly once, already reflected, rejected/excluded, reversal and partial acceptance with explicit balanced authorized amounts. Preserve uncorrected differences and never post reporting adjustments into firm or client operational books implicitly.
4. Require mapping of new allowance/accrual/expense accounts introduced by approved adjustments. Reconcile source contributions and fractions at canonical precision and handle residuals only through the approved rounding policy.
5. Complete user drill-down from QAR 47,000 net receivables to gross 50,000 and allowance 3,000, including exact adjustment and source IDs. Underlying evidence reauthorizes separately from the summary.
6. Bind the approved pre-/post-closing convention and current-period profit source. Preserve original data and old issued reports when a new source is accepted.

**Required focused verification:** The full ABC imported source and adjustment set reaches 515,000/535,000 balanced TB and 120,000 profit with reproducible per-account lineage and no duplicate reflection.

### Retained acceptance criteria

- **CP-09.AC-01:** The continuing ABC fixture produces QAR 515,000 unadjusted and QAR 535,000 adjusted totals per side, with QAR 120,000 profit, through the actual authorized services.
- **CP-09.AC-02:** Source/plan replay and already-reflected adjustments produce no duplicate delta; approved reversals retain immutable original links.
- **CP-09.AC-03:** Missing/overallocated mapping for any nonzero adjusted account blocks final output with the exact account and permitted remedy.
- **CP-09.AC-04:** QAR 47,000 net receivables drills to QAR 50,000 gross and QAR 3,000 allowance, including accepted source and adjustment identities.
- **CP-09.AC-05:** Partial management acceptance cannot apply unspecified amounts or unbalance a journal; uncorrected differences remain in the audit workflow.
- **CP-09.AC-06:** Changed accepted source stales dependent pending output and decisions, while prior issued artifacts and original source records remain unchanged.

## CP-10 — Comparatives, opening continuity, period close and restatement

**Priority:** P0. **Current disposition:** PARTIAL; F-03 requires correction for two-period final readiness.

**Dependencies:** CP-09/CP-11; approved prior-snapshot authorization and restatement policy.

**Outcome:** Support real annual reporting with independently approved current and prior sources that can coexist.

**Evidence / reuse map:** [R10] [R15] [R18].

### Remaining implementation

1. Add a regression for F-03 before changing comparative selection. Both current and comparative packages must retain the correct accepted source within their own period/book/basis; current-year ingestion cannot invalidate the prior merely through an engagement-wide latest lookup.
2. Reuse period/restatement facilities and exact package references. Implement the approved narrow cross-engagement reporting-snapshot handoff for the same entity; permission to consume a prior approved report is not permission to inspect its entire raw audit file.
3. Pin as-issued/reclassified/restated-error/policy-transition/estimate-change treatment and qualified evidence. Preserve the selected original and require explicit successor selection rather than swapping to the newest prior record.
4. Reconcile current opening balances to approved prior closing values and any authorized bridge. Normal opening continuity, professional annual acceptance and disclosure restatement are separate decisions.
5. Complete close and roll-forward with current required reviews, original-intent receipts and signed profit-transfer convention. Reopening an accounting period must not reopen a terminal frozen audit archive.
6. Keep missing comparatives unavailable and profile-blocking unless an explicitly permitted reviewed exception exists. Complete comparative monetary and note schedules, not only the face-statement column.

**Required focused verification:** Two distinct prior/current GL batches remain reviewable; actual restatement stales only the relevant pending consumers; unauthorized historic evidence stays protected.

### Retained acceptance criteria

- **CP-10.AC-01:** Every current/prior statement and note uses the same exact selected comparative basis and preserves its source history.
- **CP-10.AC-02:** A permitted same-entity annual snapshot works without broad prior-engagement access; foreign/unauthorized references return protected errors.
- **CP-10.AC-03:** Missing prior data remains unavailable and blocks profiles requiring it unless a separately approved permitted exception applies.
- **CP-10.AC-04:** Reclassification/restatement differences and opening bridges are explained and reviewed; original issued bytes never change.
- **CP-10.AC-05:** Period close and opening carry-forward require their current inputs/reviews; retries do not duplicate opening balances or transfer profit twice.
- **CP-10.AC-06:** Stale comparative selection, incompatible currency/basis, overlapping periods and an attempt to edit a frozen source are refused through UI, API and worker paths.

## CP-11 — Source-backed schedule preparation and independent review

**Priority:** P0. **Current disposition:** NEW GL SUBPATH IMPLEMENTED; source projection/review contract incomplete.

**Dependencies:** CP-01/CP-09; resolve F-01/F-02/F-03; approved provenance policy.

**Outcome:** Extend the delivered GL allocation/review feature into a correct, revision-safe source-backed schedule foundation.

**Evidence / reuse map:** [R09] [R10] [R11] [R12] [R13] [R20] [R21] [R22].

### Remaining implementation

1. Keep FinancialScheduleSourceQuery discovery, actual stored allocations, bound source hashes, independent review and the browser picker. Correct the ledger-sign versus cash-effect contract in F-01 rather than replacing the entire feature.
2. Introduce only missing source-kind/projection/provenance facts: accepted GL, authorized adjustment, reviewed reconciliation/specialist line and policy-permitted manual-evidenced document revision. Preserve original ledger signs and separately validate consumed magnitudes and role-specific contributions.
3. Add original-intent receipt, expected allocation/source/review manifest and lock/recheck semantics to RecordCashFlowSourceReviewAsync. Same successful intent returns the original decision and does not alter v3.reviewed identity; a genuine new return/reapproval is append-only.
4. Prove all supporting references match the exact scope/period/book/currency and actual immutable source version. Audit current EF/migrations for missing source/actor references, post-creation insertion protection and immutable membership; add only required constraints with legacy handling.
5. Complete submitted/returned/successor editing with source replacement and explicit independent approval. An uploaded manual workpaper requires verified bytes, method, accountable preparer and reviewer; a text label alone remains insufficient.
6. Expose saved draft, contribution trace, independent review, conflicts and original-request recovery through the existing API/Angular surfaces. Allocation approval means source support only; it cannot select cash-flow policy or approve the final report.

**Required focused verification:** Correct balanced source events work; wrong signs, no-cash journals, overconsumption, cross-scope references, self-review, source races and duplicate decision intents are detected without changing history.

### Retained acceptance criteria

- **CP-11.AC-01:** Every final-required monetary schedule contribution resolves to an accepted source/version or policy-approved manual-evidenced record with verified digest.
- **CP-11.AC-02:** Plain descriptive text cannot make a required source-lineage check pass, even when its hash is present.
- **CP-11.AC-03:** Duplicate economic contribution, over-allocation, wrong scope/currency/basis and substituted evidence block final approval.
- **CP-11.AC-04:** The preparer cannot approve their own schedule across roles; submission/approval/rework history is immutable.
- **CP-11.AC-05:** Source replacement or evidence/review revision invalidates the dependent pending result without rewriting issued history.
- **CP-11.AC-06:** Both accepted-source and approved manual-provenance journeys work through actual API-hosted Angular screens with truthful provenance labels.

## CP-12 — Complete indirect cash flows and assessed-zero states

**Priority:** P0. **Current disposition:** PARTIAL / final profile BLOCKED_PROFESSIONAL.

**Dependencies:** CP-10/CP-11; approved indirect method and zero-state policy.

**Outcome:** Prepare a complete source-backed indirect cash-flow statement rather than merely entering movement totals.

**Evidence / reuse map:** [R06] [R09] [R14] [R15] [R16] [R20].

### Remaining implementation

1. Pin the approved starting P&L subtotal and selected cash/equivalent account populations for each period. Confirm the policy reference through qualified review; no default IFRS text or generic IsNonCash interpretation constitutes approval.
2. Model explicit roles for starting profit, noncash profit adjustments, working-capital decomposition, investing and financing cash, cash FX and noncash disclosure-only events. Use the corrected source projection/consumption contract from CP-11.
3. Connect depreciation, impairment, accruals and working-capital movements to actual approved account/schedule contributions. Remove noncash acquisitions, FX, transfers and reclassifications from cash effects where required; reconcile cash-to-cash transfers separately.
4. Replace the FX-row/count completeness shortcut with assessed-zero/no-activity evidence and independent review. Missing stays incomplete. Do not invent zero currency pairs, journals or rows solely to satisfy required counts.
5. Provide current/prior schedule editing and reviewer selection in the product. Show unsupported roles, unresolved populations and exact residuals; never generate a balancing plug or silently include a duplicated impairment add-back.
6. Keep unsupported direct-method functionality disabled with a clear profile reason. Complete the supported indirect profile and comparative bridge before final approval/signing.

**Required focused verification:** ABC CFO125,000/CFI-25,000/CFF15,000/FX0 reconciles cash50,000 to165,000; net-receivable movement plus an extra ECL add-back is refused as a mixed method.

### Retained acceptance criteria

- **CP-12.AC-01:** ABC current-year CFO is 125,000, CFI -25,000, CFF 15,000, FX explicitly assessed 0, and ending cash 165,000 QAR.
- **CP-12.AC-02:** The starting profit and each noncash/working-capital adjustment trace to their approved source and classification review.
- **CP-12.AC-03:** Equipment acquired on credit is excluded from investing cash, while approved depreciation is added back in the indirect reconciliation.
- **CP-12.AC-04:** Genuine assessed-zero sections/FX can satisfy the approved profile without invented records; unspecified data cannot.
- **CP-12.AC-05:** Gross receivable movement plus impairment add-back is not mixed with net receivable movement plus the same add-back; duplicate transfers/allocations are detected.
- **CP-12.AC-06:** Current/prior bridges reconcile independently to the respective cash populations; any unexplained residual or stale source blocks final acceptance.

## CP-13 — Complete signed equity components, transfers and profit convention

**Priority:** P0. **Current disposition:** PARTIAL; signed cross-cast correction preserved.

**Dependencies:** CP-10/CP-11; approved equity treatment and closing convention.

**Outcome:** Explain every equity component and owner movement with source-backed review and correct signs.

**Evidence / reuse map:** [R15] [R16] [R20].

**Implementation update (2026-10-17):** CP-13.AC-04 now has explicit package support for paired reserve/component transfers. A transfer identifies its source and destination equity codes, unique reference, positive six-decimal amount and evidence reference. Server validation requires both equity lines, applies the same amount as source outflow and destination inflow in both component rollforwards, and prevents transfer data from entering profit/loss or OCI. Transfers are persisted append-only, included in package and presentation source hashes, and shown as linked rows in the equity statement and package artifact. The build form accepts these rows. CP-13 remains **PARTIAL**: this records preparer-supplied evidence and package-level review binding; it does not resolve evidence references to accepted source records, supply independent reserve-policy approval, or close profit convention, distributions, opening lineage or explicit no-movement assessments.

### Remaining implementation

1. Reuse existing equity lines and signed current/prior checks. Add exact opening/source/movement/review references where absent; do not reintroduce absolute-value comparisons.
2. Support required share capital, retained earnings, reserves and owner/partner current-account components with signed balances. Model distributions, contributions, OCI, reserve transfers (paired transfer rows now supported; independent policy approval remains open) and approved prior-period adjustments explicitly.
3. Prove opening-to-closing continuity per component and total. Transfer pairs conserve total equity. A distribution cannot be silently missing, and a negative component cannot be turned into a positive magnitude.
4. Bind pre-closing or post-closing source convention so profit is transferred exactly once. A source already closed to retained earnings must still supply the approved period-result basis for the statement; it cannot be treated as a zero-result year.
5. Complete independently reviewed evidence for owner/reserve decisions and explicit no-OCI/no-movement assessments. Add usable component/movement matrix selection to the build/report form and all selected export formats.

**Required focused verification:** Capital70,000 plus retained earnings135,000 equals equity205,000; deficit, reserve transfer, omitted distribution, stale opening and both closing conventions have signed regression coverage.

### Retained acceptance criteria

- **CP-13.AC-01:** ABC opening equity 100,000 + profit 120,000 - distributions 15,000 = closing equity 205,000 QAR.
- **CP-13.AC-02:** Share capital is 70,000 and retained earnings close at 135,000 QAR with exact once-only profit treatment.
- **CP-13.AC-03:** Correct deficits and negative owner components remain valid and signed; wrong-sign equal-magnitude totals continue to fail.
- **CP-13.AC-04:** A reviewed reserve/component transfer does not change total equity or generate duplicated income/expense.
- **CP-13.AC-05:** Pre-closing and approved post-closing source fixtures reconcile to the same intended final equity without duplicate profit transfer.
- **CP-13.AC-06:** Omitted distributions, incomplete components, unsupported sources or stale openings block final readiness despite internally balanced rows.

## CP-14 — Disclosure applicability, narratives and specialist note schedules

**Priority:** P0. **Current disposition:** PARTIAL; NOTE_CONTENT remains explicitly unreviewed.

**Dependencies:** CP-10/CP-11; approved disclosure register and specialist evidence.

**Outcome:** Produce complete reviewed notes rather than numbered text with a matching total.

**Evidence / reuse map:** [R15] [R16] [R20].

### Remaining implementation

1. Bind each stable logical note to the approved policy requirement and period. Applicability is pending, required or independently approved not applicable; omission is not N/A. Preserve narrative-only requirements and notes linked to multiple statements.
2. Reuse evidence, valuations and reconciliation schedules for PPE, receivables/allowance, cash, payables/accruals, borrowings, equity, revenue/expenses and any required related-party, benefit, commitment, risk or subsequent-event note.
3. Support typed detail rows with genuine source provenance and exact current/prior control totals. Do not reconstruct aging, asset lists, maturities or related parties from a TB aggregate. Approved manual-evidenced detail remains labeled manual.
4. Freeze submitted/reviewed note revisions and membership. A changed text, table, note insertion/order, applicability or linked source invalidates affected pending composition and decisions, even when total amounts are unchanged.
5. Replace permanent draft NOTE_CONTENT failure only when actual policy, content, evidence and independent decisions are bound. Numeric note lines, FX/zero and equity references must be selectable in the product rather than always serialized null.
6. Record authorization date and subsequent-event assessment separately from rendering time. Any adjusting event uses the normal journal/mapping/review path. Template wording and legal statements come from the qualified owner.

**Required focused verification:** PPE93,000, receivables47,000, cash165,000 and payables/accruals30,000/5,000 reconcile; required unanswered/unreviewed notes and inconsistent N/A decisions block final output.

### Retained acceptance criteria

- **CP-14.AC-01:** ABC PPE cost closes at 125,000, accumulated depreciation at 32,000 and carrying value at 93,000 QAR from reviewed movements.
- **CP-14.AC-02:** Gross receivables 50,000 less allowance 3,000 = 47,000; cash 165,000; payables 30,000 and accruals 5,000 tie to the face statements.
- **CP-14.AC-03:** Missing/unreviewed required disclosure or unsupported N/A prevents final readiness; approved applicable notes have actual independent review evidence.
- **CP-14.AC-04:** Reorder/insert/remove operations preserve correct references in PDF/DOCX/XLSX and create the appropriate successor identity.
- **CP-14.AC-05:** Unsupported detail is not replaced by zero, generic text or invented data; manual-evidenced content is explicitly labeled.
- **CP-14.AC-06:** NAQAHA note exceptions remain recorded; only an authorized expected-output correction changes reference fixtures, and customer signatures/private data never enter public tests.

## CP-15 — Planning authority, assigned FSLI procedures and safe concurrent execution

**Priority:** P0 professional authority; P1 residual workflow. **Current disposition:** CORE PRESENT; remaining assignment/methodology assertions must be verified.

**Dependencies:** CP-04/CP-06/CP-09; approved audit methodology.

**Outcome:** Tie audit planning and assigned procedures to the exact accepted financial-statement basis.

**Evidence / reuse map:** [R01] [R02] [R03] [R04] [R06].

### Remaining implementation

1. Retain current materiality, risk routing, planning authorization and the already-delivered audit submission fence. Verify any prior assignee/FSLI-link gap against current schema and API before changing it.
2. Require approved exact TB/mapping and professional selection for materiality/risk assessments. Preserve approved no-normalization and practical-rounding rules; revised data must stale the dependent planning basis, not silently preserve approval.
3. Finish procedure-level assignee/backup/reassignment history and exact FSLI applicability where missing. Per-line workprogram/analytical-review links must retain their source context and respect restricted assignee/reviewer roles.
4. Verify concurrent edits on distinct procedures do not overwrite each other, and concurrent submission/review/plan amendment on the same subject is fenced. New ad-hoc procedures affect completion membership and cannot disappear from mandatory review.
5. Preserve actual human judgment for risk/materiality/sample interpretation. Color bands and computed indicators do not substitute for Partner clearance or qualified methodology.

**Required focused verification:** Assigned fieldwork, ad-hoc steps, stale materiality, same-subject races and higher-risk reviewer hierarchy are proven through the actual commands and Angular screens.

### Retained acceptance criteria

- **CP-15.AC-01:** A statement-row action opens the exact linked procedures and approved shared work, without granting access to unrelated lines/engagements.
- **CP-15.AC-02:** Missing/stale TB mapping, materiality or required planning approval blocks protected fieldwork through the authoritative command.
- **CP-15.AC-03:** Only eligible assigned preparers can submit; reassignment preserves previous results and denies stale previous-owner writes.
- **CP-15.AC-04:** Angular submission creates exactly one immutable result/receipt and a real review item; unknown outcomes recover the original intent.
- **CP-15.AC-05:** Different permitted procedures can commit concurrently; stale same-procedure writes and writes losing a freeze race cannot commit.
- **CP-15.AC-06:** Legacy unassigned/unlinked procedures remain visible for authorized remediation, not silently backfilled or counted as complete.

## CP-16 — Substantive workpapers, confirmations, review/rework and SRM

**Priority:** P0 audit completion; P1 specialist coverage. **Current disposition:** CORE PRESENT; full applicable fieldwork/review coverage unverified.

**Dependencies:** CP-08/CP-09/CP-15; applicable audit-program and confirmation policy.

**Outcome:** Complete the professional evidence chain from audit tests through manager review and Partner-ready SRM.

**Evidence / reuse map:** [R01] [R02] [R03] [R04] [R05].

### Remaining implementation

1. Inventory the selected audit programs and existing workpapers before implementing. Cover the required cash/bank, receivable allowance, revenue/cutoff, payables/unrecorded liabilities, assets, expenses, borrowing, related-party and other selected audit areas; do not build payroll execution or an inventory operating system.
2. Retain reproducible sampling with exact population/seed/method, item testing, exception resolution and accepted digital/physical evidence versions. Missing evidence or inconsistent sample populations cannot be hidden by a completed task.
3. Complete review-return-resubmission on exact procedure/result versions, mandatory reasons and independent hierarchy. Resolve anchored open notes; a new result cannot inherit review of an older result. Preserve linked operational rework without letting its completion approve fieldwork.
4. Keep critical confirmations and permitted alternative-procedure disposition under the approved methodology. Auto-generated holding letters remain truthful about missing confirmations; provider submission is not a received confirmation.
5. Bind corrected/uncorrected differences, estimates, analytical review, going-concern and subsequent-event assessments to the current accounting/audit basis. A corrected audit adjustment returns through normal source/plan/mapping and stales affected conclusions.
6. Generate the SRM from exact current reviewed facts, with approved recommendations/management-response content. Partner clearance is explicit over that version. Run all selected professional-gate regressions; a short certificate/bundle cohort cannot certify every workpaper.

**Required focused verification:** Critical confirmation, open note, stale result, unreviewed ad-hoc step and unresolved applicable conclusion block completion; a returned/resubmitted procedure produces fresh evidence and review.

### Retained acceptance criteria

- **CP-16.AC-01:** Each required workpaper/confirmation population is traceable and has a current reviewed outcome or approved applicability decision; missing required work blocks audit completion.
- **CP-16.AC-02:** Preparer -> review -> return with reason -> corrected successor -> independent review works end to end, preserving all earlier results and assignments.
- **CP-16.AC-03:** Open review notes, stale inputs and returned current results remain blockers even with 100% task completion or workflow features off.
- **CP-16.AC-04:** Required critical confirmation is resolved only by the permitted evidenced outcome/alternative decision; otherwise final report remains blocked and holding status is truthful.
- **CP-16.AC-05:** SRM, red-risk clearance, significant estimates and audit-difference evaluation pin their current sources; changing an input invalidates affected clearance.
- **CP-16.AC-06:** No calculator, automator, sample result or AI assigns an opinion, clears professional judgment or impersonates an independent reviewer.

## CP-17 — One complete final-readiness authority

**Priority:** P0. **Current disposition:** IMPROVED canonical 11-rule guard; final profile still partial.

**Dependencies:** CP-09–CP-14 for statement readiness; CP-15/CP-16 for audited issue.

**Outcome:** Use one complete versioned readiness contract for the actual output purpose.

**Evidence / reuse map:** [R14] [R15] [R16] [R18].

### Remaining implementation

1. Preserve exactly-one-pass semantics for the existing canonical population. Add versioned approved profile membership and manifests instead of reintroducing an ad-hoc second validator or deleting failing codes.
2. Distinguish source-link acceptance, schedule methodology review, disclosure applicability/content, statement readiness, audit completion, signing eligibility and release. Explicitly pin which set is required for the selected entity/service/output purpose.
3. Make required comparatives and notes depend on policy, not the presence of request fields. Unknown required rule versions, missing/duplicate/failed results, unavailable evidence or stale membership block the applicable final stage.
4. Bind readiness results to canonical amounts, current/prior contexts, source acceptance, adjustment/mapping revisions, reviewed cash/equity/notes, selected layout/composition and relevant professional facts. Recompute/recheck at publication and protected decisions under the correct locks.
5. Provide exact blockers and permitted remediation on the UI and queue, scope-filtered before counts. A completed background build or independent source-link review must never be labeled a fully approved financial report.

**Required focused verification:** An omitted comparative/required note cannot pass by absence, all entry points honor the same purpose-specific manifest, and source changes after preview refuse final publication.

### Retained acceptance criteria

- **CP-17.AC-01:** A complete approved fixture passes every required current accounting rule; the existing incomplete draft fixture remains visibly incomplete.
- **CP-17.AC-02:** Missing cash/equity source lineage, note review or any mandatory result blocks final readiness even when no failed database row exists.
- **CP-17.AC-03:** Direct API, worker, review, sealing, signing and release cannot use a weaker required-rule population for the same purpose/profile.
- **CP-17.AC-04:** Source/policy/membership changes between preview and action produce a stale/refused outcome rather than an approval of old content as current.
- **CP-17.AC-05:** A comment cannot downgrade an arithmetic, evidence or authority failure; judgment warnings require their real approved disposition.
- **CP-17.AC-06:** Final-accounting readiness does not automatically create management acknowledgment, audit opinion, signature, financial release or provider delivery.

## CP-18 — Exact composition and professionally structured document publication

**Priority:** P0. **Current disposition:** PARTIAL — immutable candidate composition and exact PDF/DOCX/XLSX membership are implemented; approved output-profile and document-quality acceptance remain open.

**Dependencies:** CP-17; reviewed design/output profile and existing artifact kernel.

**Outcome:** Produce the intended professional financial report from one immutable selected composition.

**Evidence / reuse map:** [R01] [R13] [R15] [R16] [R17] [R19].

### Remaining implementation

1. Reuse current presentation pins, renderer, package artifacts and seals. The selected composition now binds one candidate revision to PDF/DOCX/XLSX artifacts and their ordered manifest. Still approve and bind complete report membership: result/current/prior references, schedule/note membership, ordered sections, branding/locale and required formats.
2. Keep unapproved draft history. The controlled successor signing-candidate path is implemented; do not update old status/bytes or remove a draft banner after approval. Complete the approved-data cover, entity/framework/period, notes and signatory blocks.
3. Validate genuine PDF/DOCX/XLSX, repeated headers, note links, signs, current/prior amounts, rounding, long labels and multi-page coverage. Pin font/renderer dependencies without distributing raw fonts. Malformed, clipped, wrong-MIME or missing-glyph output must block final acceptance.
4. The explicitly selected artifact set is sealed and cannot be silently replaced by later draft exports. Verify approved membership and final approval behavior against the complete output profile.
5. Use existing durable render intents and immutable input capture; render outside long locks and fence final publication. Repeated original intent returns the correct stored output. Never fetch newer GL or change arithmetic inside a renderer.
6. Keep content and post-render artifact digests separate. Preserve historic legacy artifacts and make new output purpose explicit, with complete safe draft/stale/final-candidate/issued labels.

**Required focused verification:** Every required format contains the same approved values and references, malformed/clipped/missing output blocks seal, and new draft exports do not mutate the selected issue set.

### Retained acceptance criteria

- **CP-18.AC-01:** Current/prior amounts, required sections, note numbers and critical totals agree across genuine PDF/DOCX/XLSX.
- **CP-18.AC-02:** One selected composition determines required artifact membership; extra draft exports cannot silently alter an existing seal or selection.
- **CP-18.AC-03:** Missing required format, malformed bytes, wrong MIME, missing glyph, incorrect pointer or clipped critical content blocks final artifact acceptance.
- **CP-18.AC-04:** Render retry reuses its immutable original result or returns an explicit conflict; it never replaces sealed bytes.
- **CP-18.AC-05:** Source/layout/policy/note changes during rendering prevent falsely current publication; old approved downloads remain intact.
- **CP-18.AC-06:** Draft-to-candidate handling is a governed successor path, not a watermark edit; decisions can identify the exact final statement content and artifact manifest.

## CP-19 — Exact-report management, accounting and Partner decisions

**Priority:** P0. **Current disposition:** PARTIAL — formal decisions bind the same candidate composition ID/digest and its PDF; management identity, offline evidence and review recovery semantics remain open.

**Dependencies:** CP-18; approved current formal order and management evidence policy.

**Outcome:** Bind management, accounting reviewer and deciding Partner decisions to the exact intended report.

**Evidence / reuse map:** [R02] [R10] [R13] [R17] [R18] [R26].

### Remaining implementation

1. FinancialPackageReviewDecision/service now records candidate revision, composition ID and manifest digest; downstream release validates the PDF/Word/Excel set. Complete decision evidence for the selected signatory and required management capacity. Keep canonical text as source evidence, not as a substitute for examining the formatted report.
2. Management -> accounting -> Partner formal package order is preserved and enforced for the same composition. Earlier internal preparation review remains a distinct action.
3. Resolve F-09 using assignment-aware Partner authority and actual person-based independence. Test administrator-only and multi-role actors. Align UI capabilities with the exact command policy rather than roles listed on the session.
4. Capture real client signatory/capacity and permitted signed-in/offline evidence, named staff recorder and required independent verification. Selecting OFFLINE cannot impersonate client management or omit the identity of the person whose acceptance was recorded.
5. Use expected candidate/review revisions and original receipts with post-lock source/authority checks. Return/reject/revoke requires reason, a substantive successor does not inherit old approvals and uncertain responses recover the same result.
6. Preserve legacy canonical-text decisions as their original evidence class. Never backfill them into newly proven approval of rendered artifacts, missing signatories or an unexamined note revision.

**Required focused verification:** Required persons approve exact selected files; stale/changed layout or notes, wrong stage/person, unverified offline identity and replay races cannot create a misleading current approval.

### Retained acceptance criteria

- **CP-19.AC-01:** Each required decision binds the selected eligible composition and exact sealed artifact/subset identity under its actual authority.
- **CP-19.AC-02:** Note/layout/banner/signatory/date changes requiring new bytes invalidate affected pending/current approval eligibility and require a reviewed successor.
- **CP-19.AC-03:** Wrong role, wrong order, same-person self-approval, stale revision, wrong client and revoked actor are rejected independently of UI visibility.
- **CP-19.AC-04:** Offline acknowledgment requires verified actual management evidence; staff recorder and management signatory remain distinct facts.
- **CP-19.AC-05:** Duplicate/lost-response recovery retains one original decision and cannot approve another version or silently overwrite a rejection.
- **CP-19.AC-06:** Historical text-only decisions remain readable as their original evidence; no migration promotes them to approvals of files they never covered.

## CP-20 — Audit report, five-part deliverables, signing and controlled issue

**Priority:** P0. **Current disposition:** PARTIAL — release and five-part bundle use the exact selected composition; audit-report/signing/legal acceptance remains open.

**Dependencies:** CP-05/CP-16/CP-19; selected audit template, delivery and signing profile.

**Outcome:** Issue the exact approved financial statements with the actual human audit conclusion through existing release controls.

**Evidence / reuse map:** [R01] [R02] [R05] [R18] [R19].

### Remaining implementation

1. Reuse approved report generation, four opinion choices, modified-opinion basis, current SRM, critical confirmations, representation verification and authorized Partner signing. No automatic conclusion or invented professional/legal wording.
2. Release and `BundleInputsAsync` now resolve the expressly selected composition and include its exact three formats. Complete downstream signing for any approved combined output; never substitute `FinancialPackageArtifactVersions.Pdf` when a different presentation was approved.
3. Preserve the five-part ZIP, reviewed management letter, representation source/verified scan, permitted correspondence and posted balance invoice prerequisites. Redact internal audit/cost/AML content from client-facing files and metadata.
4. For a separately selected combined-PDF profile, finalize approved unsigned cover/opinion/statements/notes before signing. Preserve separately signed originals in the ZIP; do not append pages to signed content or introduce a statement-release/bundle/balance-invoice cycle.
5. Use configured certificate policy, signer authority, candidate verification, signed-byte verification and original-intent recovery. Separate signature appearance, cryptographic validity, trust/timestamp support and qualified legal acceptance; never treat a test certificate as firm authorization.
6. Pin exact signed artifacts, content/decision/certificate evidence and release/checkpoint receipts. Unknown provider outcomes remain unknown until reconciled. Readiness, issuance and delivery receipt are distinct.

**Required focused verification:** Wrong composition, missing professional evidence, stale approval, wrong signer/certificate and byte tampering block issue; retries preserve the original signed result and release event.

### Retained acceptance criteria

- **CP-20.AC-01:** The opinion, statements, supporting deliverables and final manifest identify the exact approved current statement/composition and authorized human decisions.
- **CP-20.AC-02:** Missing/stale audit evidence, required LOR verification, modified-opinion basis or permitted signatory blocks issue; task completion cannot substitute.
- **CP-20.AC-03:** Five-part assembly preserves existing finance/release/checkpoint prerequisites and has no cycle requiring the final bundle before the prerequisite financial release.
- **CP-20.AC-04:** A swapped/default/foreign/stale statement PDF cannot replace the explicitly approved artifact; every required stored source is integrity-checked.
- **CP-20.AC-05:** Selected combined output is finalized before signing; required certificate failures block, corruption is detected, and replay returns the original signed result without new signing.
- **CP-20.AC-06:** Existing ZIP history and separate signed originals remain unchanged; image-only and test-certificate outputs never falsely claim trusted production/legal approval.

## CP-21 — Immutable archive, amendments, historical reads and provider truth

**Priority:** P0 integrity; P1 external operating closure. **Current disposition:** LOCAL CORE EXISTS; external/production evidence pending.

**Dependencies:** CP-20; approved archive and retention responsibilities.

**Outcome:** Preserve issued evidence exactly and close the engagement without claiming nonexistent provider immutability.

**Evidence / reuse map:** [R01] [R02] [R05] [R19].

### Remaining implementation

1. Retain terminal local file freeze, signature-anchored countdown, authorized early lock, rejected-write evidence and exact archive exports. Verify rescheduled pre-freeze signing and already-frozen states under the approved policy.
2. Use the release/archive store as issued-evidence authority. SharePoint working copies with staff Full Control do not become immutable merely because the app disables edits. Any separately required provider protection needs real scoped enforcement and readback evidence.
3. Freeze portal write paths at their established milestone and test races across PBC/representation/source imports and worker publication. Historical authorized reads remain distinct from new writes.
4. Corrections produce linked successors/supplements in an eligible context; reopening an accounting period or administrative amendment must not mutate a terminal frozen file. Preserve old signed bytes, decision identities and supersession rationale.
5. Exercise exact-byte retrieval, corruption detection, permission revocation, large regulator exports, cross-store reconciliation and recovery. Retention/deletion and cleanup are owner-authorized; no automated cleanup touches issued evidence.

**Required focused verification:** The original issue hashes remain unchanged through freeze/amendment/restore, unauthorized writes fail, and local versus provider-enforced protection is accurately displayed.

### Retained acceptance criteria

- **CP-21.AC-01:** Only effective committed freeze produces terminal archived state; failed/late/queued freeze remains visibly unresolved.
- **CP-21.AC-02:** Protected writes after freeze are refused across UI/API/worker paths without altering issued bytes or review evidence.
- **CP-21.AC-03:** Archive/download returns the exact issued files and hashes, with explicit current/historical/superseded context and current access checks.
- **CP-21.AC-04:** An authorized correction produces a linked successor and refreshed required decisions; original signed history is not overwritten or deleted.
- **CP-21.AC-05:** Signature timer, early lock and re-sign races have deterministic idempotent outcomes and no automatic reopening.
- **CP-21.AC-06:** Provider protection, local freeze, restore status and working-copy permissions are separate observed facts; unproven external retention remains blocked.

## CP-22 — Firm ledger, receivables, WIP and explainable practice analytics

**Priority:** P1 finance/analytics; P0 ledger controls. **Current disposition:** SUBSTANTIAL CORE; calendar fix retained; broader evidence incomplete.

**Dependencies:** CP-05/CP-06; ADR-0018 and approved firm chart/cost basis.

**Outcome:** Make firm financial and operational reports reconcile to approved records with explicitly different meanings.

**Evidence / reuse map:** [R02] [R03] [R04] [R06] [R23].

### Remaining implementation

1. Keep existing FirmLedger/LedgerService, receivables aging and monthly P&L rather than building another accounting engine. Firm invoices/expenses/withdrawals stay separate from client schedules and group results.
2. Preserve distinct contracted-fee estimate, captured charge-out standard value, actual recorded cost, billed/collected amounts, realization and margin. Respect non-billable/rate-alias/utilization decisions in ADR-0018 and prevent mixed-currency aggregation without approved conversion.
3. Complete effective-date/cutoff tests for posted invoices, credits, receipts, payment-term changes and allocation reversals. All posted firm invoices use the approved aging scope, not only advance/final milestones.
4. Verify WIP and staff/department measures against actual approved time/budgets and the selected reporting basis. Complete staff-level performance views only under approved visibility/definition; counts are not quality judgments.
5. Exercise firm expense maker/checker, evidence links, drawings and manual end-of-service provision treatment. The mechanism exists; no unapproved benefit estimate or payroll execution is introduced.
6. Preserve the corrected Sunday–Thursday analytics fixture and add month-end, nonstandard period, missing-rate, zero-denominator, leave and scope cases. Fix actual logic or fixture defects only with a documented basis; do not change expected outputs merely to pass.

**Required focused verification:** Ledger reports and every headline metric reconcile to current eligible source records, with unavailable values distinguished from zero and historical cutoffs/reversals preserved.

### Retained acceptance criteria

- **CP-22.AC-01:** Each reported KPI reconciles to exact approved time/rate/fee/posted invoice/receipt or firm-ledger sources and clearly states its basis.
- **CP-22.AC-02:** Standard value and actual cost remain different; nonchargeable time treatment follows ADR-0018 and does not corrupt currency selection.
- **CP-22.AC-03:** Firm AR includes eligible advance/final/time/other posted invoices but excludes client-native invoices; ambiguous milestone binding is handled explicitly.
- **CP-22.AC-04:** Monthly firm P&L and TB reconcile to posted firm double entry across cutoff/credit/reversal cases without client/consolidation contamination.
- **CP-22.AC-05:** Missing inputs or zero denominator produce the approved unavailable state, not invented zero, infinity or silently partial totals.
- **CP-22.AC-06:** The observed analytics regression is resolved with justified tests, and all personnel/client aggregates enforce direct-query/export scope.

## CP-23 — Governed methodology library, reusable templates and firm content

**Priority:** P1; P0 professional content approval. **Current disposition:** IMPLEMENTED FOUNDATION; firm-specific content acceptance open.

**Dependencies:** CP-00/CP-01; qualified methodology and content owners.

**Outcome:** Publish controlled reusable firm methodology and report content without silently inventing professional rules.

**Evidence / reuse map:** [R01] [R02] [R03] [R04].

### Remaining implementation

1. Reuse versioned technical library, approval/publishing, audience-aware search and existing templates. Verify exact matched version, superseded access and role/client restrictions; a newer library entry must not silently rewrite engagement evidence.
2. Complete missing approved letter/report/management deficiency/representation text and accurate figures from the canonical approved package. Flag absent registration/licence/signatory metadata, not fabricated placeholders in final documents.
3. Pin applicable library/template/profile editions and effective dates at engagement preparation/report composition. Unsupported entity/industry/period profiles remain explicit; no generic template claims all-framework compliance.
4. Govern author/reviewer identity, content revision and publication independently from use on a live engagement. Practitioner approval of the actual report remains required even when a template is published.
5. Verify long text, safe markup, allowed attachments and referenced versions across search, preview, Word/PDF and archived outputs. Source standards/license restrictions remain respected; do not copy unlicensed full standards into the repository.

**Required focused verification:** Only reviewed eligible versions can be selected; changed policy/wording requires the relevant successor and review, and clients do not receive internal library or firm-cost content.

### Retained acceptance criteria

- **CP-23.AC-01:** Required professional content is adopted from an exact independently published version with valid applicability and responsible owners.
- **CP-23.AC-02:** A new version cannot silently update existing work or issued bytes; the impact/re-adoption path preserves previous evidence.
- **CP-23.AC-03:** Wrong-firm/audience users cannot retrieve content, snippets or counts through search or direct IDs.
- **CP-23.AC-04:** Superseded/unapproved/unsupported content is clearly labeled and cannot qualify a new final report contrary to policy.
- **CP-23.AC-05:** Design publication and professional treatment approval remain separate; generated/scanned/source text never supplies autonomous approval.
- **CP-23.AC-06:** Missing approved clause/font/profile blocks the affected final output and names the permitted remediation without substituting arbitrary boilerplate.

## CP-24 — Supported native-bookkeeping and group-reporting branches

**Priority:** P1 only for declared enabled branches; P0 isolation. **Current disposition:** CONDITIONAL / PARTIAL — not a reason to rebuild a full ERP.

**Dependencies:** CP-00 scope approval; existing native/group prerequisites and entity artifact contract.

**Outcome:** Complete the native or group-reporting branches actually promised by the approved service profile without breaking import-first reporting.

**Evidence / reuse map:** [R01] [R02] [R03] [R05].

### Remaining implementation

1. Make the release scope explicit. Native bookkeeping, tax/ancillary and consolidation features are independently enabled and accepted; excluded/unsupported paths must be disabled with tested safe behavior, not silently presented as completed.
2. For an enabled native client-accounting service, verify reviewed cutover/openings, balanced journal posting, client invoice/credit/settlement paths already in scope, retained GL/TB snapshots and exact reporting adapter. Never combine native movements and imported GL twice.
3. For enabled group reporting, finish the approved component package/result-to-artifact handoff, perimeter/ownership and FX/elimination/review controls. Group grants never imply component raw-book access; consolidation never changes component books.
4. Pin approved methodology for FX, intercompany, ownership or open-item measurement before enabling automation. Unavailable inputs block the enabled branch, not an unrelated mandatory import-first entity report.
5. Keep functional completeness and production qualification per profile. Do not claim all 26 CP packages complete by removing an advertised branch from the denominator without explicit scope approval and documentation.

**Required focused verification:** Every enabled native/group branch has its own source-to-report and adverse tests; disabled branches are clearly unavailable and the three financial boundaries remain intact.

### Retained acceptance criteria

- **CP-24.AC-01:** Each enabled source mode has an exact accepted snapshot and tested source-to-report lineage with no native/import double counting.
- **CP-24.AC-02:** Native cutover/openings/postings require their actual mandate, independent approvals and cutoff; a reporting adjustment cannot silently become an operational posting.
- **CP-24.AC-03:** Advertised native invoice/open-item capabilities pass their applicable CA acceptance; an incomplete branch is blocked or expressly not offered.
- **CP-24.AC-04:** Enabled group results pin approved components/perimeter/method/rates and never mutate component client or firm books.
- **CP-24.AC-05:** Group output uses the selected exact artifact/review/release chain and enforces component-summary versus underlying-source permissions.
- **CP-24.AC-06:** Unsupported optional profiles stay disabled with reasons; mandatory contracted scope remains in the acceptance denominator until completed or formally changed by the owner.

## CP-25 — Integrated user journey, release engineering and production acceptance

**Priority:** P0. **Current disposition:** CONFIGURED CI GREEN; full functional/release/production acceptance open.

**Dependencies:** All mandatory enabled CP packages; section 6 operational owners.

**Outcome:** Promote only an independently accepted, repeatable and operable production candidate.

**Evidence / reuse map:** [R01] [R05] [R06] [R08] [R23] [R24].

### Remaining implementation

1. Execute full required Domain/API/Angular/E2E and schema/contract checks on one immutable candidate. Keep the current fast filtered CI and add the complete release verification rather than replacing it with more permissive filters.
2. Run the no-SQL-status-edit new-client, annual, accounting and audit journeys in section 5 with separate actual test identities. Clearly report all seeded prerequisites and verify both supported artifact bytes and source-level monetary meaning.
3. Obtain owner-authorized branch protection/required checks/independent PR review and deployment approval. Store exact source/image/migration/contract identities and original logs; do not claim published acceptance images prove deployed production behavior.
4. Rehearse the existing installation/upgrade/migration procedures with old data, current key/certificate custody, worker isolation, rollback/recovery and immutable archive evidence. Schema rollback that destroys protected history is not a safe recovery plan.
5. Complete live identity/mail/selected-site/checkpoint, telemetry/alerts, capacity and recovery drills and real-firm methodology/signing acceptance. Record named owner, approved thresholds, environment, observed proof and independent disposition.
6. Update only materially affected canonical Wiki deployment guidance after separate publication authorization. Hand operators exact prerequisites, health checks, failure recovery, escalation and restoration procedures; no secrets or per-release duplicate runbooks.

**Required focused verification:** All applicable gates are evidenced for the exact candidate; missing professional approval, incomplete full regression, unknown provider state or failed recovery prevents a production-ready claim.

### Retained acceptance criteria

- **CP-25.AC-01:** Distinct commercial, Finance, preparer, reviewer, management and Partner actors complete the selected whole journey without database stage/approval edits or manual Word/PDF repair.
- **CP-25.AC-02:** API-hosted Angular covers saved drafts, conflict/retry/revocation, period change, source race, exact artifact preview/download and returned-work recovery.
- **CP-25.AC-03:** Exact candidate passes required full suites and artifact/visual/contract/migration checks; failures/skips/omissions are not hidden by focused green CI.
- **CP-25.AC-04:** Upgrade, rollback, restore and retained-artifact drills preserve original issued identities, with approved measured resource and recovery evidence.
- **CP-25.AC-05:** Required live-provider, certificate, independent-review and professional gates have actual authorized evidence; simulations cannot close them.
- **CP-25.AC-06:** Final sign-off maps every mandatory source requirement and inherited reporting criterion to implemented behavior, tests and reviewer disposition, with no unresolved mandatory gap labeled complete.

# 5. End-to-end and adverse acceptance scenarios

## 5.1 Two distinct businesses and two distinct ledgers

The audit firm's QAR 20,000 engagement fee is **firm revenue**, not the client's QAR 300,000 revenue. The firm's time costs, invoices, receipts and advances never enter the client's adjusted TB. A report marked audited additionally needs real audit-completion evidence and an authorized opinion; a financially balanced fixture alone does not satisfy that requirement. These boundaries are part of the retained story and architecture, not new scope. [U01] [R01]

All examples below are synthetic technical fixtures from the previous story. They are not NAQAHA's actual ledger, approved professional methodology or a legal/tax conclusion. Source documents, users and decisions used in automated tests must be explicitly synthetic. A real-client acceptance run requires separately authorized sanitized data and named practitioners.

## 5.2 Continuing ABC client-accounting fixture — full QAR, not thousands

### Opening balances on 1 January 2025

| Account | Debit | Credit |
| --- | ---: | ---: |
| Cash | 50,000 | 0 |
| Trade receivables | 20,000 | 0 |
| Equipment cost | 100,000 | 0 |
| Accumulated depreciation | 0 | 20,000 |
| Trade payables | 0 | 15,000 |
| Bank loan | 0 | 35,000 |
| Share capital | 0 | 70,000 |
| Retained earnings brought forward | 0 | 30,000 |
| **Total** | **170,000** | **170,000** |

Opening assets after accumulated depreciation are QAR 150,000; liabilities QAR 50,000; equity/net assets QAR 100,000. Do not repost the brought-forward balances as new current-year activity in an existing continuing ledger.

### Balanced 2025 journals

Give every synthetic event an explicit date within the approved period, stable journal/line IDs and source evidence. Import the complete balanced transaction, not only the source line desired by the cash-flow test.

| ID | Debit account | Credit account | Amount QAR |
| --- | --- | --- | ---: |
| J01 — Delivered services invoiced | Receivables | Revenue | 300,000 |
| J02 — Collections | Cash | Receivables | 270,000 |
| J03 — Salaries paid | Salaries | Cash | 90,000 |
| J04 — Rent paid | Rent | Cash | 30,000 |
| J05 — Office services on credit | General/administrative expense | Payables | 40,000 |
| J06 — Suppliers paid | Payables | Cash | 25,000 |
| J07 — Equipment paid | Equipment | Cash | 25,000 |
| J08 — New loan | Cash | Bank loan | 40,000 |
| J09 — Principal repayment | Bank loan | Cash | 10,000 |
| J10 — Approved distribution | Distributions | Cash | 15,000 |

The teaching fixture permits explicit synthetic subledger allocations: collections clear the opening QAR 20,000 plus QAR 250,000 of current invoices; supplier payments clear QAR 15,000 opening plus QAR 10,000 current services. Label those as proposed fixture detail, not actual client records.

### Reviewed year-end adjustments

| ID | Debit account | Credit account | Amount QAR |
| --- | --- | --- | ---: |
| A01 | Utility expense | Accrued expenses | 5,000 |
| A02 | Depreciation expense | Accumulated depreciation | 12,000 |
| A03 | Receivables impairment expense | Receivables allowance | 3,000 |

Each has its own synthetic supporting workpaper and actual preparer/independent reviewer/management treatment path where required. Do not seed a posted status and claim the posting workflow was tested.

### Unadjusted and adjusted balances

| Account | Unadjusted Dr | Unadjusted Cr | Adjusted Dr | Adjusted Cr |
| --- | ---: | ---: | ---: | ---: |
| Cash | 165,000 | 0 | 165,000 | 0 |
| Receivables | 50,000 | 0 | 50,000 | 0 |
| Allowance | 0 | 0 | 0 | 3,000 |
| Equipment cost | 125,000 | 0 | 125,000 | 0 |
| Accumulated depreciation | 0 | 20,000 | 0 | 32,000 |
| Payables | 0 | 30,000 | 0 | 30,000 |
| Accrued expenses | 0 | 0 | 0 | 5,000 |
| Bank loan | 0 | 65,000 | 0 | 65,000 |
| Share capital | 0 | 70,000 | 0 | 70,000 |
| Opening retained earnings | 0 | 30,000 | 0 | 30,000 |
| Revenue | 0 | 300,000 | 0 | 300,000 |
| Salaries | 90,000 | 0 | 90,000 | 0 |
| Rent | 30,000 | 0 | 30,000 | 0 |
| General/administrative expense | 40,000 | 0 | 40,000 | 0 |
| Utilities | 0 | 0 | 5,000 | 0 |
| Depreciation expense | 0 | 0 | 12,000 | 0 |
| Impairment expense | 0 | 0 | 3,000 | 0 |
| Distributions | 15,000 | 0 | 15,000 | 0 |
| **Total** | **515,000** | **515,000** | **535,000** | **535,000** |

### Required linked financial outputs

```text
Profit = 300,000 - 90,000 - 30,000 - 40,000 - 5,000 - 12,000 - 3,000
       = 120,000

Net receivables = 50,000 - 3,000 = 47,000
PPE carrying amount = 125,000 - 32,000 = 93,000
Total assets = 165,000 + 47,000 + 93,000 = 305,000
Liabilities = 30,000 + 5,000 + 65,000 = 100,000
Retained earnings = 30,000 + 120,000 - 15,000 = 135,000
Total equity = 70,000 + 135,000 = 205,000
305,000 = 100,000 + 205,000

Operating cash = 120,000 + 12,000 + 3,000 - 30,000 + 15,000 + 5,000
               = 125,000
Investing cash = -25,000
Financing cash = 40,000 - 10,000 - 15,000 = 15,000
FX cash effect = explicitly assessed zero
Closing cash = 50,000 + 125,000 - 25,000 + 15,000 + 0 = 165,000
```

For the required gross-receivables approach, the movement is -30,000 and impairment is a separate +3,000 add-back. Subtracting only the -27,000 net receivable increase **and** adding impairment produces the wrong QAR 128,000 operating cash and must not be accepted. A correctly specified alternative method must prove it does not double-count.

The original lesson does not supply loan maturities, asset-level depreciation detail or real tax/interest/OCI/FX policy. Approve explicit synthetic support before calling the classified statement/disclosures complete; do not guess a current/noncurrent split from the QAR 10,000 principal paid during the year. A report requiring those facts remains blocked until the fixture or real evidence supplies them.

### Explicit prior-year synthetic extension

Preserve the prior story's separately labeled 2024 extension: opening cash40,000, receivables10,000, equipment60,000, accumulated depreciation credit10,000, payables credit5,000, loan credit35,000, capital credit70,000 and accumulated deficit debit10,000. Activity: revenue200,000 invoiced; collections190,000; salaries80,000 paid; rent20,000 paid; office services40,000 on credit; suppliers30,000 paid; equipment40,000 paid; distribution10,000; depreciation10,000.

Expected 2024 profit50,000, cash50,000, receivables20,000, payables15,000, equipment carrying amount80,000, retained earnings30,000, assets150,000, liabilities50,000 and equity100,000. CFO60,000, CFI-40,000, CFF-10,000 and assessed-zero FX produce a cash increase10,000. Its closing balances reconcile to 2025 opening balances. Keep the pre-closing reporting result and the explicit closing transfer convention. This is test data, not reconstructed NAQAHA history.

Use **distinct accepted GL batches and source decisions for the two periods**. Prove the F-03 correction with those batches. A separate test supplies no comparative and expects unavailable/blocking status, not automatic substitution of this fixture.

## 5.3 Positive product journeys

### Journey A — New client, audit preparation, full report and retained issue

Use separate preparer, reviewer, client management, deciding Partner and finance identities. Declare any administrator/professional policy setup seeded only as test prerequisites.

1. Configure the supported synthetic firm/profile/chart/taxonomy/rates/calendar/templates through actual configuration workflows being claimed as tested.
2. Create client/contact/routing and the exact QAR20,000 offer; obtain independent commercial and current unconditional risk acceptance.
3. Generate the approved letter and draft advance invoice; finance independently approves/posts it. Partial receipt must block activation; fully allocate QAR10,000 before authorized activation.
4. Pin tasks/staffing/period and collect PBC. Import and independently accept the balanced 2024 and 2025 GL/TB/source evidence without editing status columns to fabricate success.
5. Process A01–A03, reflection and approved mappings; verify full adjusted balance and source contributions.
6. Prepare/review cash, equity and specialist notes. Resolve every applicable disclosure and zero-state decision; select the exact approved comparative.
7. Execute required fieldwork, reviewed confirmations or permitted alternative procedures, difference evaluation, rework and the current SRM/Partner clearance through real commands.
8. Build complete statements, select one exact composition, render/validate all required formats and seal the chosen set.
9. Obtain the approved management/accounting/Partner report decisions over those exact artifacts. Verify the actual signatory and prohibit role-switching self-approval.
10. Complete the existing statement-release/checkpoint and final-fee prerequisites in their noncircular order. Produce the approved audit deliverable variant, sign with a clearly test-only certificate and verify exact signed bytes.
11. Issue and deliver through the real supported commands; record truthful provider outcome, freeze portal uploads and archive the exact issued copy.
12. Retrieve as authorized history; create an approved successor where appropriate and prove old artifacts/decisions remain unchanged.

Local simulations only prove their named technical path. They do not satisfy real professional opinions, production certificate trust or live-provider acceptance. An acceptance report must name transitions seeded outside the UI and not count them as observed UI behavior.

### Journey B — Annual recurrence and controlled restatement

Create a new engagement for the same entity with fresh offer/continuance and advance gates. Reuse eligible templates and prior reporting references only. Prove that current-year staff can use an authorized prior report snapshot without reading restricted prior working papers. Verify two-period cash/equity/note reviews, then create an explicitly approved prior restatement and a current successor without rewriting as-issued history.

### Journey C — Firm management and exception recovery

Verify staff time and approved costs, fixed-fee and approved time-billed invoice paths, non-billable classification, credits, allocation reversal and historical aging; reconcile department/monthly/WIP views and protected exports. Inject lost replies, missed worker heartbeats and provider throttling. Recover original intents without duplicate invoices, messages, reviews, rendered artifacts or releases.

## 5.4 Required focused regressions for the newly inspected gaps

These names are suggested behaviors to add to existing suites, not instructions to create a new test project per row. Full inherited criteria still apply.

| Behavior | Expected assertion |
| --- | --- |
| `CashEquipmentPayment_PreservesLedgerSignAndProjectsNegativeCash` | Balanced DrPPE/CrBank can support -25,000 investing cash with true source signs |
| `CustomerCollection_UsesCashEventNotCounterpartSignAsReceiptSign` | DrBank/CrAR supports +270,000, without artificial debit revenue |
| `NoncashEquipmentPurchase_IsDisclosureOnly` | DrPPE/CrAP creates no investing cash outflow |
| `InternalCashTransfer_DoesNotInflateActivities` | Two eligible bank legs cancel within cash, no operating/investing/financing double count |
| `CompoundCashJournal_PreservesAllocatedTotalAndFees` | Split counterparts/fees reconcile the actual cash leg without overconsumption |
| `SourceReview_ReplayReturnsOriginalDecisionAndManifest` | One stored decision for the same request, no new timestamp-based identity |
| `SourceReview_ExpectedManifestChangesBeforeCommit` | Replacement or changed allocation refuses stale approval under the lock |
| `SourceReview_RevokedActorDuringLockWait` | No decision or professional effect after authority is withdrawn |
| `TwoPeriods_RetainTheirOwnAcceptedCashSources` | Accepted current GL does not invalidate the deliberately selected prior source merely by being newer |
| `ComparativeSnapshot_DoesNotGrantRawPriorEvidenceAccess` | Permitted prior statement read succeeds; unauthorized raw workpaper read is denied |
| `BuildForm_SubmitsReviewedEquityNotesAndFxOrAssessedZero` | The completed product workflow does not always send those fields as null |
| `MissingRequiredRule_AndOmittedDisclosure_AreBothBlocking` | Preserve new exact-one canonical semantics and enforce the approved final profile |
| `AdministratorOnly_CannotMakeProfessionalPartnerDecision` | Effective authority matches the approved policy on direct command and API |
| `FinalReportDecision_BindsSelectedCompositionNotOnlyText` | New note/layout/format membership requires a fresh eligible decision |
| `Bundle_UsesExactlyApprovedPresentationArtifact` | No fallback substitution by the legacy package-PDF enum |
| `RealisticAbcJourney_EndsInExactSignedArchivedBytes` | Full 515,000/535,000/120,000/305,000/205,000/165,000 chain with real workflow transitions |

Retain existing sign-sensitive equity, genuine-format, zero/unavailable, immutable-source and scope regressions. A fixture whose purpose is only control plumbing may remain synthetic and small, but cannot be the sole proof of source completeness or accounting correctness.

# 6. Production readiness and operational acceptance

“Production ready” means accepted for an explicitly supported service/reporting/deployment profile. It is not a claim of universal accounting coverage or regulatory certification. Required gates must be satisfied by their named owners, not by toggling a feature flag or editing a Markdown status.

## 6.1 Gate register

The following are closure actions over the existing P/T/AS-PAR/CP authorities. Map them to those registers rather than introducing a second approval ledger.

| Gate | Owner | Required implementation/operating evidence | Go/no-go rule |
| --- | --- | --- | --- |
| Approved reporting scope and professional methodology | Qualified accounting/audit methodology owner + independent reviewer | Supported framework/edition/entity/period/basis, statement/notes, cash method, source/zero/profit convention, required audit procedures and approved golden fixtures | No final-grade report under an unapproved or unsupported profile; T003 is not cleared by synthetic actors |
| Current complete release evidence | Engineering lead + independent reviewer | Same candidate SHA, lockfiles, migrations, OpenAPI/UI bundle, complete required tests and linked criterion assertions; retained run logs | A green filtered CI run alone is insufficient |
| PR and deployment governance | Repository owner + release owner | Owner-authorized protected branch/ruleset, required checks, independent review and controlled deployment approval; tested merge policy | No source-control policy change or merge is performed by this document |
| Scope/privacy review | Security reviewer + product owner | Direct command/query/count/search/export/download matrices across firm/client/engagement/group and prior reporting snapshots; multi-role and revocation races | No privilege widening through admin roles, stale sessions, task completion or unscoped metadata |
| Configuration and least privilege | Operations/security owner | Existing approved startup profiles, API/worker credential separation, narrowly consented provider identities, disabled simulation paths for real effects and documented unsupported integrations | No production bypass of a startup or external-effect safety guard |
| Authentication and session/key continuity | Identity and operations owners | Live authorized sign-in, wrong-tenant/disabled/revoked identity denial, first-sign-in/delegation checks, restart/multi-instance key persistence, old-key decryption and certificate-rotation/recovery evidence | Production Data Protection implementation already exists; configuration and recovery must actually be proven |
| Database release and historical data upgrade | Database/release owner | Tested additive upgrade from existing data; legacy-unproven evidence handling; constrained source/decision references; drift checks; actual backup/restore; database role separation | No fabricated approval backfill or destructive downgrade of immutable history |
| External document/communication behavior | Microsoft/provider administrator + independent reviewer | Authorized site provisioning, exact item/version readback, current selected-resource grants, two-client isolation, revocation, throttling and unknown-outcome reconciliation; approved mail path | Development evidence is retained, but production acceptance is separately named and observed |
| Signing and report sufficiency | Authorized Partner + credential/security owner | Actual firm credential custody, use/expiry/rotation controls, signer authority, exact candidate and signed output, verifier result and explicit trust/timestamp limitations; approved report wording | Required missing/unusable credential or insufficient required verification blocks issue; no test certificate passes this gate |
| Release checkpoint and archive | Records/release owner | Exact selected artifacts, independent checkpoint/readback where required, immutable issued lineage, corruption refusal, terminal local freeze and approved provider-copy semantics | Local freeze does not prove direct SharePoint immutability; P3/P6/provider claims remain separate |
| Backup and recovery | Operations/database/records owners | Custodially separated backup, recoverable application keys, database/immutable bytes/checkpoint consistency, restore to isolated environment and measured RPO/RTO | Targets are approved before acceptance; no numeric RPO/RTO invented here and no successful-backup log substitutes for a restore |
| Capacity and bounded workload | Operations + engineering lead | Representative client/engagement/data volumes; concurrent users; GL intake; statement/render/sign/archive throughput; memory/CPU/queue behavior; error/timeout rates | Approve workload, latency and resource thresholds first; a small local query sample is not production sizing |
| Observability and incident response | Operations/security owner | Redacted correlation/operation IDs, durable queue age/lease/dead-letter alerts, provider unknown states, auth anomalies, signing/cert expiry, archive integrity, restore failures and alert drills | Alerts need observed delivery and a responsible responder; logs must not expose keys/tokens/unrestricted financial evidence |
| Deployment rehearsal and operator handover | Release/operations owner | Approved OS/hosting topology, exact API/UI/worker artifacts, health/startup checks, smoke tests, migration/rollback/recovery procedure and materially current canonical Wiki | Image build/publication is not evidence that the configured product is operating correctly |
| Real-role user acceptance and pilot | Firm owner, practitioners, finance and client representatives | New-client, annual, full financial-reporting and exception journeys on an authorized pilot; recorded blockers, signed acceptance and retained evidence | All mandatory functional gates must be met; no automatic production claim from code/tests |

Existing authority for these boundaries is the repository's external gate and ADR model. Some mechanisms—production key protection, certificate-aware signing, local immutable archives and development SharePoint transfer—already exist. This section completes their approved use, not a speculative infrastructure rewrite. [R01] [R02] [R05] [R24]

## 6.2 Mandatory environment and recovery details

Before deployment, record the approved production values privately for environment identity, firm/tenant/installation boundaries, database connectivity/roles, API and isolated worker compositions, feature flags, selected sites/mailboxes, key directories/certificate references, output storage, checkpoint and backup targets. Do not embed credentials, private identifiers or preauthenticated URLs in this story, code, logs or public Wiki.

Validate certificate/key rotation using the existing current/previous decryption support. Prove whether already-issued signatures remain verifiable under the firm's chosen trust policy without re-signing historical bytes. API instances must use the intended shared/persisted key identity; a restart must not silently replace the installation boundary. Filesystem and service identities receive only the permissions needed for their role.

A failed render or storage publication retains prior issued history and does not produce a sealed partial set. A lost provider response is reconciled by the original receipt rather than blind retry. Test worker claim/lease expiry, duplicate delivery, cancellation and conflicting publication. Restore must reconcile exact source/decision/artifact/checkpoint identities, not just start a database successfully.

Retention, supplementary records, erasure exceptions and any jurisdiction-specific deadline are selected by authorized owners. This document introduces no legal retention duration or data-residency conclusion beyond the existing approved product policy. The configured signature-anchored archive workflow must be verified without asserting it proves every applicable legal obligation.

## 6.3 Verification commands and result reporting

Use the current `AGENTS.md` commands at execution time. The following remains the reviewed repository baseline; tests must target the isolated test database, never customer data:

```bash
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release --maxcpucount:1
dotnet test AuditSphereOps.slnx --no-build --configuration Release --maxcpucount:1

dotnet ef migrations has-pending-model-changes   --project src/AuditSphereOps.Infrastructure   --startup-project src/AuditSphereOps.Api   --no-build --configuration Release

npm --prefix src/AuditSphereOps.Ui ci
npm --prefix src/AuditSphereOps.Ui run build
npm --prefix src/AuditSphereOps.Ui run test:ci

bash scripts/contracts/verify-openapi.sh
python3 scripts/docs/validate-markdown-documentation.py
python3 scripts/docs/validate-markdown-filenames.py
python3 scripts/docs/validate-narrative-metrics.py
python3 scripts/ui/inventory.py --check
```

Use the current pinned SDK/tool versions, supported test-database configuration and browser setup from the repository; do not substitute an easier environment and call it parity. Focused new regressions may run during a slice, followed by the required candidate-wide checks. Record command, source/test identity, environment, passed/failed/skipped counts, warnings, explicit omissions and test artifacts. A compiled test did not execute; an unauthenticated shell is not a completed workflow.

Artifact acceptance additionally opens the actual PDF/DOCX/XLSX, validates required structure/sections/values/links, and inspects rendered pages for clipped material. All three formats must represent the same approved values and note references. Signature/restore/provider tests need their own observed evidence; a source-code comment is not a pass.

# 7. Dependency-ordered delivery and Definition of Done

## 7.1 Implement the missing delta in reviewable slices

| Increment | Scope | Exit evidence |
| --- | --- | --- |
| 0 — Current evidence and supported-profile contract | CP-00; preserve green CI and already-fixed behaviors; register F-01–F-12; resolve required owner decisions | Exact baseline and requirement matrix; no stale missing-feature claims; no pretend professional approval |
| 1 — Source correctness and decision safety | CP-11 with F-01/F-02, relevant CP-01 authority and CP-09 realistic inputs | Valid cash source projections, no false cash-event inference, intent-safe independently reviewed allocations and adverse tests |
| 2 — Current/prior source coexistence | CP-10/CP-11 F-03 | Two-period accepted-source test and narrowly authorized comparative snapshot, without broad prior-file access |
| 3 — Complete reporting preparation | CP-12/CP-13/CP-14 and corresponding UI/API | Complete reviewed indirect cash, equity, applicability and typed notes; assessed-zero policy; no permanent null fields for required inputs |
| 4 — Final readiness and exact report identity | CP-17/CP-18/CP-19 | All required policy rules, exact selected artifacts and real-person decisions; stale/race/replay tests |
| 5 — Audited deliverable integration | CP-16/CP-20/CP-21 and preserved CP-05 prerequisites | Actual human audit decision linked to the approved composition; verified signing, release and unchanged archive |
| 6 — Remaining firm-practice journeys | CP-02–CP-08/CP-15/CP-22/CP-23 | Close only current missing routing/consent/task/deadline/fieldwork/finance behavior and its end-to-end acceptance |
| 7 — Enabled optional service branches | CP-24, only when in the approved release scope | Native/group contract and isolation acceptance or explicitly approved, safely disabled exclusion |
| 8 — Candidate and operational acceptance | CP-25 and section 6 | Full regression, independent review, protected release process, measured recovery/load and authorized pilot sign-offs |

This is sequencing guidance, not permission to bypass existing task-card dependencies or postpone all UI work. Required API/Angular behavior accompanies each slice. Work with independent owners in the smallest coherent scope; do not use this broad plan to authorize uncontrolled parallel rewrites.

**First technical work:** write the realistic balanced-journal counterexamples and review-replay regression, then repair the existing CP-11 contracts. Do not start by rebuilding a GL picker, adding a second renderer, re-fixing signed equity or investigating the already-corrected calendar failure as though it still failed current CI.

## 7.2 Definition of Done per slice

- Identify the affected original CP/AC, FSR/R2R/AUD authority, current implementation and exact remaining delta.
- Preserve unchanged behavior, financial boundaries, immutable records and declared compatibility. No unused framework/configuration layer or new dependency without authorization.
- Deliver required Domain/Application/persistence/API/OpenAPI/Angular changes coherently; enforce decisions at authoritative commands, not merely buttons.
- Execute the meaningful positive/negative, role/scope, stale/replay/race and accounting tests. Cite actual candidate results; identify tests not run.
- Use additive upgrades and source-preserving legacy handling; no fabricated evidence or approvals. Prove historical issued bytes are unchanged.
- Obtain independent technical review and any qualified methodological approval. Record observed evidence in the existing authorities; do not manually advance generated board statuses.

## 7.3 Overall acceptance

**Functional complete:** every mandatory applicable criterion in the retained acceptance contract and its underlying source requirements has verified behavior, including realistic multi-period cash/equity/notes and complete role-separated practice/audit/reporting journeys. Unsupported optional profiles remain explicitly disabled under approved scope; mandatory gaps are not renamed optional.

**Release complete:** the exact chosen candidate passes the full required checks, artifact and migration integrity/recovery tests and independent review; approvals bind the exact intended formatted report and signed issued bytes. Filtered CI alone is not sufficient.

**Production accepted:** all applicable owner/professional/security/provider/operational/pilot gates in section 6 have accepted evidence for the approved deployment and service profile. Local success cannot change `BLOCKED_EXTERNAL` or `BLOCKED_PROFESSIONAL` to approved without the named evidence.

Until these conditions are met, keep the relevant parent story **PARTIAL**, while crediting the specific completed code and verified slices. Do not report 0% functionality simply because overall acceptance is open; do not report 100% because a package renders or a CI workflow is green.

## 7.4 Coding-agent handoff

```text
Work on the current authorized nirzaf/AuditSphere branch. This is a residual-work
revision of AS-PRACTICE-FR-COMPLETE-001, not a new state machine or task board.
Read AGENTS.md, current ADRs, the code map and applicable task contracts first.
Recheck HEAD and map the exact requested next slice to the original CP/AC IDs.

Preserve the Qatar-week analytics fix, signed equity checks, v3 reviewed source
manifest, 11-rule missing/failed/ambiguous evaluator, existing GL picker/source
allocations/independent review, durable build receipts and current passing CI.

Start with the realistic cash-sign and source-review original-intent regressions
(F-01/F-02), then current/prior source coexistence (F-03), subject to the existing
approved dependency order. Keep source ledger sign separate from projected cash
contribution and prove a real cash event. Do not fix this by indiscriminately
negating every amount or by seeding a debit revenue line as a receipt.

Complete reviewed schedules, assessed-zero states, policy-required disclosures,
exact composition and actual report decisions using existing services and stores.
Never hide missing required inputs with null/N/A, remove a draft banner to claim
approval, treat administrator roles as Partner authority, or copy old approvals.

Implement only the minimal coherent Application/API/Angular/persistence/test delta.
Use existing receipt, revision, authorization, transaction and durable-operation
patterns; handle stale sources and original-intent replay without duplicate history.
Preserve management/accounting/Partner order unless its separate approved decision
changes, and keep firm/client/group books distinct.

Run focused regressions and the repository-required complete release checks; record
exact source identities, results, warnings and omissions. Do not claim full product
acceptance from a synthetic subpath or a green filtered workflow. Professional,
provider, certificate, deployment and production gates remain named human decisions.
No production mutation, new external integration, key activation or merge is implied.
```

For a PR merge where the project workflow requires confirmation, obtain the explicit codeword **`AUDITSPHERE-MERGE-APPROVED`** for that intended merge. Its appearance here is not authorization. Production migrations, provider permission changes and real-client issuance require their own applicable authorization; no blanket approval is granted by this story.

# Appendix A. Original reporting acceptance criteria

The following **69 criteria** are copied unchanged from the previous consolidated story, which in turn retained `AS-FR-CLOSE-001`. Together with the **156 original CP criteria** above, they preserve the **225-statement acceptance contract**. Their original WP numbering and references to the original story's sections remain historical source references; current implementation deltas and the standalone fixture are in sections 2–5 of this document. This appendix is not a claim that all 69 were re-executed or are all still wholly missing. [U01] [U03]

CP/WP aliases: WP-00→CP-00; WP-01→CP-09; WP-02→CP-10; WP-03→CP-11; WP-04→CP-12; WP-05→CP-13; WP-06→CP-14; WP-07→CP-17; WP-08→CP-18; WP-09→CP-19; WP-10→CP-20/CP-21; WP-11→the relevant CP UI slices and CP-25.

## Original WP-00

- **AC-00.1:** Inventory includes existing implementation, missing delta, existing test evidence and unverified acceptance for every selected work package.

- **AC-00.2:** An unsupported/unapproved framework-period-profile cannot produce an approvable final statement set.

- **AC-00.3:** Decision order, delivery form, source mode and fixture provenance are explicit and versioned.

- **AC-00.4:** No production readiness or test pass is recorded from source inspection alone.

## Original WP-01

- **AC-01.1:** ABC unadjusted TB totals are QAR 515,000 per side; approved adjustments produce QAR 535,000 per side and QAR 120,000 profit.

- **AC-01.2:** Replaying the same source or finalized plan creates no duplicate balances or adjustments.

- **AC-01.3:** A journal already reflected in a replacement source is not applied again.

- **AC-01.4:** Any unmapped resulting account blocks final statement preparation with the exact offending account and permitted remediation.

- **AC-01.5:** A scoped user can trace QAR 47,000 net receivables to QAR 50,000 gross receivables and QAR 3,000 allowance; unauthorized underlying records remain protected.

- **AC-01.6:** Changing a relevant accepted source stales downstream pending output without modifying previously issued history.

- **AC-01.7:** Partial management acceptance cannot cause an unbalanced posting or application of amounts for which no explicit approved treatment exists.

## Original WP-02

- **AC-02.1:** Current/prior values throughout statements and notes use the same pinned comparative selection.

- **AC-02.2:** A comparative revision change invalidates the affected pending composition and decisions; original issued files remain unchanged.

- **AC-02.3:** A different client, incompatible basis or unauthorized prior engagement is denied without leaking identifiers or totals.

- **AC-02.4:** A reclassification preserves total prior assets/equity where appropriate while showing the approved line changes and rationale.

- **AC-02.5:** The synthetic prior extension in section 19 is clearly labeled as newly proposed test data, not reconstructed actual history.

## Original WP-03

- **AC-03.1:** A typed evidence link resolves to an authorized immutable record/version and its digest is verified.

- **AC-03.2:** A plain reference such as “bank statement December” cannot satisfy final source-lineage readiness by itself.

- **AC-03.3:** A reviewed `MANUAL_EVIDENCED` schedule is allowed only where the policy accepts that provenance and is displayed as manual.

- **AC-03.4:** The preparer cannot approve their own schedule by changing role or using another screen.

- **AC-03.5:** Approved schedule content cannot be mutated; rework preserves earlier content and decisions.

- **AC-03.6:** Invalid, deleted, replaced, cross-scope or unresolved source versions block the affected final output.

## Original WP-04

- **AC-04.1:** The approved ABC schedule produces operating cash 125,000, investing cash -25,000, financing cash 15,000 and closing cash 165,000 QAR.

- **AC-04.2:** Every calculation role has source/evidence lineage and independent classification review where required.

- **AC-04.3:** Noncash equipment acquired on credit is disclosed but does not create an investing cash outflow.

- **AC-04.4:** Missing FX information remains incomplete; an explicit approved no-FX decision produces zero without fabricated records.

- **AC-04.5:** A genuine zero-activity section is distinct from missing information and can satisfy an approved profile.

- **AC-04.6:** The incorrect mixed impairment/receivables method, duplicate transfer or unexplained residual blocks final acceptance.

## Original WP-05

- **AC-05.1:** ABC opening total equity 100,000 + profit 120,000 - distributions 15,000 = closing equity 205,000 QAR.

- **AC-05.2:** Share capital remains 70,000; retained earnings close at 135,000 QAR.

- **AC-05.3:** A negative owner component remains signed in the matrix and its SFP mapping.

- **AC-05.4:** A transfer between reserves/components does not change total equity or duplicate P&L.

- **AC-05.5:** Pre-closing and approved post-closing source fixtures produce the same final equity without a double profit transfer.

- **AC-05.6:** An opposite-sign closing total, omitted distribution, incomplete component population or stale opening basis blocks final acceptance.

## Original WP-06

- **AC-06.1:** ABC PPE note reconciles 100,000 opening cost + 25,000 additions - 0 disposals = 125,000 closing cost; accumulated depreciation 20,000 + 12,000 = 32,000; carrying amount 93,000 QAR.

- **AC-06.2:** Receivables note reconciles gross 50,000 less allowance 3,000 = net 47,000 QAR; cash note totals 165,000; payables/accrual notes reconcile to 30,000 and 5,000 respectively.

- **AC-06.3:** A required unanswered or unreviewed note prevents final-ready status. N/A is accepted only with an allowed reason/evidence and required independent decision.

- **AC-06.4:** A note’s numeric content and current/prior face reference remain aligned after reordering in all three output formats.

- **AC-06.5:** Unsupported or unavailable detail is not silently replaced by zero, a fabricated schedule or generic boilerplate.

- **AC-06.6:** Known NAQAHA note-number inconsistencies are registered as source exceptions; corrected expected references require recorded approval. Real signed customer evidence is not copied into public fixtures.

## Original WP-07

- **AC-07.1:** A draft can be previewed with incomplete notes while remaining unapprovable and unreleasable.

- **AC-07.2:** The complete fixture passes every required rule; the existing incomplete draft fixture still reports its intended failures.

- **AC-07.3:** Missing `CASH_FLOW_SOURCE_LINEAGE`, note-review evidence or any required validation is not treated as a pass.

- **AC-07.4:** Direct API, worker, seal, signing and release actions consistently refuse a failed or stale final-readiness manifest.

- **AC-07.5:** A source race after preview causes a stale/retry outcome rather than approval of the old selection as current.

## Original WP-08

- **AC-08.1:** Current/prior values, statement order, note numbers and critical totals agree across the three required formats.

- **AC-08.2:** A second draft layout or export does not silently change the selected composition or historical seal.

- **AC-08.3:** Missing required format, malformed bytes, wrong MIME, bad pointer, clipped critical content or missing approved font prevents final artifact acceptance.

- **AC-08.4:** Retrying a render reuses the appropriate immutable result or returns an explicit conflict; it never overwrites sealed bytes.

- **AC-08.5:** Source/policy/layout changes during rendering cannot publish a falsely current final set.

- **AC-08.6:** The selected final artifact manifest covers the actual intended formatted statements, not just the legacy canonical text.

## Original WP-09

- **AC-09.1:** Decisions bind the exact selected composition, sealed artifact manifest and required stage/person/evidence.

- **AC-09.2:** Editing a note or replacing a layout after approval requires a new eligible set and refreshed affected decisions.

- **AC-09.3:** Management approval is not an audit opinion, release or cryptographic signature.

- **AC-09.4:** Self-approval, wrong stage/order, stale revision, wrong client, unverified offline signatory and revoked actor are rejected.

- **AC-09.5:** A late response or repeated request does not create duplicate decisions or authorize newer content.

- **AC-09.6:** Historical canonical-text decisions remain readable and are not backfilled as approvals of artifacts they never covered.

## Original WP-10

- **AC-10.1:** The auditor report and financial statements identify the exact approved statement/composition version.

- **AC-10.2:** A selected combined profile produces the approved full page sequence before signing; the existing ZIP remains supported and historical files unchanged.

- **AC-10.3:** Missing audit evidence, stale report basis, wrong signer, unavailable required certificate or failed signature verification blocks issue.

- **AC-10.4:** A tampered artifact, swapped PDF, wrong release identity, stale approval or cross-client component cannot be bundled/issued.

- **AC-10.5:** Signature verification covers the issued bytes; a one-byte corruption is detected. Retry does not generate a replacement signature or duplicate release event.

- **AC-10.6:** Archive/download returns the exact issued bytes, with explicit historical/superseded status and fresh access checks.

- **AC-10.7:** Local certificate tests and synthetic audit actors do not clear real-firm legal, external checkpoint or production-tenant gates.

## Original WP-11

- **AC-11.1:** Distinct preparer, reviewer, management and Partner actors complete the approved ABC journey through actual API-hosted Angular screens without database status edits.

- **AC-11.2:** Reload preserves saved selections and clearly distinguishes draft, submitted, reviewed, stale, signing-pending, issued and historical states.

- **AC-11.3:** Wrong-role/cross-client/revoked actions are refused by the API even when sent manually.

- **AC-11.4:** Changing a period or source during a pending operation cannot display or publish the result under the wrong context.

- **AC-11.5:** Displayed progress reflects verified domain facts, not percentages inferred from routes or task names.

# Appendix B. Source register

All repository implementation links below are pinned to the audited source unless explicitly labeled as a live metadata endpoint. They support the stated source observations, not a claim that all associated behavior was independently executed. The test files show assertion scope; actual execution claims come from the named CI run/jobs. The historical top-level status snapshot is not substituted for the current branch.

<a id="source-u01"></a>
**U01 — Previous consolidated user story:** `AuditSphere_Practice_Management_and_Financial_Reporting_Completion_Story.md`; planning reference `AS-PRACTICE-FR-COMPLETE-001`; SHA-256 recorded in this document's metadata. Original CP organization and all 225 criterion lines were checked against its mounted bytes and preserved.

<a id="source-u02"></a>
**U02 — Previous implementation audit:** `AuditSphere_AS_FR_CLOSE_001_Implementation_Review_a1bd02d6.md`. It is a historical comparison at the earlier commit, not evidence that its old CI failure or all free-text-source observations remain current.

<a id="source-u03"></a>
**U03 — Original financial-reporting story and teaching context:** `AuditSphere_Trial_Balance_to_Audited_Report_Gap_Closure_Technical_Story.md`, `AS-FR-CLOSE-001`, the eight-stage learning image and the ABC teaching fixtures. The NAQAHA implementation-story reference is not a complete actual ledger or approved signed report fixture.

- **R01 — [Repository architecture, safety and verification instructions](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/AGENTS.md)**. `AGENTS.md`.

- **R02 — [Approved ADR register](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md)**. `docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md`.

- **R03 — [Current capability-to-code map](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/architecture/auditsphere-architecture-code-map.md)**. `docs/architecture/auditsphere-architecture-code-map.md`.

- **R04 — [STE v2.2 requirement verification map; baseline/date limitations apply](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md)**. `docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md`.

- **R05 — [Pending work and external gates; older narrative must be reconciled](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-pending-tasks.md)**. `docs/execution/auditsphere-execution-pending-tasks.md`.

- **R06 — [Newest implementation handoff; later sections supersede older observations](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-current-slice.md)**. `docs/execution/auditsphere-execution-current-slice.md`.

- **R07 — [Reporting-story delta tracker; early discovery-only rows are superseded in code](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-tracker-trial-balance-report-current.md)**. `docs/execution/auditsphere-execution-tracker-trial-balance-report-current.md`.

- **R08 — [Status ledger; top-level verification snapshot is historical](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/status.json)**. `docs/execution/status.json`.

- **R09 — [Cash-flow allocation shape, source resolution and sign constraints](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs`.

- **R10 — [Cash-flow source review, current accepted batch and independence](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSourceReview.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSourceReview.cs`.

- **R11 — [Cash-flow allocation migration and declared database constraints](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014123639_CashFlowSourceAllocations.cs)**. `src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014123639_CashFlowSourceAllocations.cs`.

- **R12 — [Cash-flow review migration and append-only history](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014133834_CashFlowSourceReviews.cs)**. `src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014133834_CashFlowSourceReviews.cs`.

- **R13 — [Presentation source manifest v3 and reviewed variant](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs`.

- **R14 — [Shared mandatory canonical validation population](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialPackageValidationReadiness.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialPackageValidationReadiness.cs`.

- **R15 — [Package construction, comparative restrictions and validation facts](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs`.

- **R16 — [Presentation builder: equity, cash, notes and readiness](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs`.

- **R17 — [Immutable presentation pins and draft status](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.PresentationPins.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.PresentationPins.cs`.

- **R18 — [Formal package review and current-release decision checks](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs)**. `src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs`.

- **R19 — [Audited deliverable bundle and exact released PDF selection](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs)**. `src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs`.

- **R20 — [New Angular financial-package build form and request](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts)**. `src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts`.

- **R21 — [Actual scope of the accepted-GL cash-flow browser test](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/tests/AuditSphereOps.E2E.Tests/AngularCashFlowPackageJourneyTests.cs)**. `tests/AuditSphereOps.E2E.Tests/AngularCashFlowPackageJourneyTests.cs`.

- **R22 — [Seeded financial-package browser fixture](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/tests/AuditSphereOps.E2E.Tests/FinancialPackageFixture.cs)**. `tests/AuditSphereOps.E2E.Tests/FinancialPackageFixture.cs`.

- **R23 — [Actual CI commands and filtered .NET cohorts](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/.github/workflows/ci.yml)**. `.github/workflows/ci.yml`.

- **R24 — [Existing production Data Protection key persistence and certificate guard](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Api/Authentication/ProductionDataProtection.cs)**. `src/AuditSphereOps.Api/Authentication/ProductionDataProtection.cs`.

- **R25 — Historical baseline evidence: [commercial offer fallback digest](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Practice/PracticeCrmService.Dispatch.cs)**. The cited implementation was later replaced; see R28 for current code.

- **R26 — [Common API command/session/antiforgery wrapper](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Api/Ui/UiEndpoints.Infrastructure.cs)**. `src/AuditSphereOps.Api/Ui/UiEndpoints.Infrastructure.cs`.

- **R27 — [Existing approved-chart GL import and sealed source records](https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.GeneralLedger.cs)**. `src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.GeneralLedger.cs`.

- **R28 — [Current rendered-offer dispatch and immutable proposal response provenance](https://github.com/nirzaf/AuditSphere/blob/d991ddc682cf3e657927d1a01542922911e174d3/src/AuditSphereOps.Application/Practice/PracticeCrmService.cs)**. Response-evidence model: `src/AuditSphereOps.Application/Practice/ProposalResponseEvidence.cs`.

- **H01 — [Observed branch metadata; live endpoint, observation is pinned by the recorded SHA](https://api.github.com/repos/nirzaf/AuditSphere/branches/master)**.

- **H02 — [Exact old-to-new comparison metadata](https://github.com/nirzaf/AuditSphere/compare/a1bd02d61aa4e3e432d04a7b95edbb98833e4f4b...bf30879adcec13c90ade353986151f997eedb450)**.

- **CI01 — [Successful exact-head configured CI run](https://github.com/nirzaf/AuditSphere/actions/runs/38609444242)**.

- **CI02 — [Observed job and step outcomes for that run](https://api.github.com/repos/nirzaf/AuditSphere/actions/runs/38609444242/jobs?per_page=10)**.

- **G01 — [Observed repository ruleset response including parent rulesets; live endpoint](https://api.github.com/repos/nirzaf/AuditSphere/rulesets?includes_parents=true&per_page=100)**.

## Preparation checks and limitations

The document's criterion IDs and 225 verbatim inherited acceptance lines were checked against the mounted previous story. The ABC arithmetic and simple sign-guard counterexamples were independently evaluated as document-consistency checks. These checks are not runs of AuditSphere's C# calculation engine or acceptance suite.

No repository source, database, workflow run, branch setting, provider permission, certificate or customer record was changed during this review. No new professional conclusion or live production acceptance is asserted. The complete absence of an arbitrary numeric completion estimate is intentional: a reliable percentage requires the current, complete, independently evidenced criterion matrix requested by CP-00.

<!-- Reference definitions -->

[R01]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/AGENTS.md

[R02]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/architecture/adr/auditsphere-architecture-index-adr-register-current.md

[R03]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/architecture/auditsphere-architecture-code-map.md

[R04]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md

[R05]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-pending-tasks.md

[R06]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-current-slice.md

[R07]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/auditsphere-execution-tracker-trial-balance-report-current.md

[R08]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/docs/execution/status.json

[R09]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSources.cs

[R10]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.CashFlowSourceReview.cs

[R11]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014123639_CashFlowSourceAllocations.cs

[R12]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Infrastructure/Persistence/Migrations/20261014133834_CashFlowSourceReviews.cs

[R13]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Presentation.cs

[R14]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialPackageValidationReadiness.cs

[R15]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.Package.cs

[R16]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs

[R17]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialStatements/FinancialStatementService.PresentationPins.cs

[R18]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/FinancialPackageReviewService.cs

[R19]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Completion/AuditDeliverableService.Bundle.cs

[R20]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Ui/src/app/features/engagements/financial-package-build.ts

[R21]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/tests/AuditSphereOps.E2E.Tests/AngularCashFlowPackageJourneyTests.cs

[R22]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/tests/AuditSphereOps.E2E.Tests/FinancialPackageFixture.cs

[R23]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/.github/workflows/ci.yml

[R24]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Api/Authentication/ProductionDataProtection.cs

[R25]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Practice/PracticeCrmService.Dispatch.cs

[R26]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Api/Ui/UiEndpoints.Infrastructure.cs

[R27]: https://github.com/nirzaf/AuditSphere/blob/bf30879adcec13c90ade353986151f997eedb450/src/AuditSphereOps.Application/Accounting/ClientAccounting/ClientAccountingService.GeneralLedger.cs

[R28]: https://github.com/nirzaf/AuditSphere/blob/d991ddc682cf3e657927d1a01542922911e174d3/src/AuditSphereOps.Application/Practice/PracticeCrmService.cs

[H01]: https://api.github.com/repos/nirzaf/AuditSphere/branches/master

[H02]: https://github.com/nirzaf/AuditSphere/compare/a1bd02d61aa4e3e432d04a7b95edbb98833e4f4b...bf30879adcec13c90ade353986151f997eedb450

[CI01]: https://github.com/nirzaf/AuditSphere/actions/runs/38609444242

[CI02]: https://api.github.com/repos/nirzaf/AuditSphere/actions/runs/38609444242/jobs?per_page=10

[G01]: https://api.github.com/repos/nirzaf/AuditSphere/rulesets?includes_parents=true&per_page=100

[U01]: #source-u01

[U02]: #source-u02

[U03]: #source-u03
