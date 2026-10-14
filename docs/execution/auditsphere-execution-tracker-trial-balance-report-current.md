# Trial balance to audited report — intake and delta review

**Status: CURRENT — partial technical work; professional and external acceptance unresolved.**

This maps the supplied `AS-FR-CLOSE-001` story to existing reporting authorities. It is a traceability review, not a second task board or approval ledger. Exact source identity, candidate fingerprints, executed checks and omissions belong in [status.json](status.json). Completion follows the [agent task board](auditsphere-execution-index-task-board-current.md), [M24](../task_breakdown/modules/auditsphere-r2r-module-24-financial-statements-contract.md) and [M25](../task_breakdown/modules/auditsphere-r2r-module-25-financial-packages-contract.md).

## Current requirement classification

No whole work package is classified `EXISTING_AND_VERIFIED` from a focused arithmetic cohort. Source-confirmed seams below were reinspected; end-to-end acceptance remains unverified.

| Attachment package | Classification | Existing implementation and missing delta |
| --- | --- | --- |
| WP-00 profile and acceptance contract | BLOCKED_PROFESSIONAL | [T003](../task_breakdown/tasks/00_Baseline/auditsphere-r2r-task-t003-accounting-policies-resource-bounds-golden-fixtures.md) explicitly retains qualified methodology and independent-review gates. Attachment D-01–D-10 are decisions, not approvals. Technical inventory and negative tests can proceed. |
| WP-01 sources, adjustments, mapping | PARTIAL | `TrialBalanceCalculator`, `AdjustmentJournalService` and `FinancialStatementService.Package` exist. Complete adjusted-account contribution drill-down and selected-source acceptance require verification; the ABC fixture has not been executed or professionally approved here. |
| WP-02 annual comparatives | PARTIAL | Package construction still enforces the same client and engagement. A narrowly authorized prior-year reporting snapshot is needed; do not remove the engagement guard. |
| WP-03 reviewed source-backed schedules | PARTIAL | Supplementary schedule inputs and persisted package data exist. Presentation explicitly reports unresolved cash/equity source lineage; free-text evidence is not resolved source identity. |
| WP-04 indirect cash flow | PARTIAL | Movement/FX/bridge checks exist. The complete indirect method, reviewed source allocations and explicit assessed-zero states remain outstanding. No zero activity or fictitious currency pair is inferred. |
| WP-05 signed equity controls | PARTIAL | Roll-forward, profit/OCI and SFP checks exist. Sign-sensitive current/comparative cross-casts are corrected; source-backed component review and opening continuity remain outstanding under [T031](../task_breakdown/tasks/09_M24_Supplements/auditsphere-r2r-task-t031-equity-movement-schedules.md). |
| WP-06 governed disclosures | PARTIAL | Notes are numbered and checked, but `NOTE_CONTENT` explicitly remains unreviewed. Approved applicability, independent review and specialist schedules remain outstanding. |
| WP-07 complete readiness authority | PARTIAL | Draft presentation checks and package release controls exist separately. A complete supported-profile finalization contract has not been established. |
| WP-08 exact report composition | PARTIAL | Typed rendering, immutable presentation pins and package seals exist. Presentation pins remain `UnapprovedDraft`; final composition and artifact-set selection must reuse these services. |
| WP-09 exact-report decisions | PARTIAL | Existing management → accounting → Partner ordering is retained. `FinancialPackageReviewService` currently binds canonical text; intended formatted-report membership needs explicit decision binding. |
| WP-10 signing, release, archive | PARTIAL | Existing certificate-aware bundles and immutable release controls remain. Bundle statement selection still uses the released package PDF. Exact approved composition integration and live/professional acceptance remain separate. |
| WP-11 guided Angular journey | PARTIAL | Package/presentation screens exist. The complete source/schedule/note/composition acceptance journey has not been executed. |

## Implemented technical correction

`FinancialStatementPresentationBuilder` previously compared absolute closing-equity values. That allowed a schedule with the opposite sign to satisfy the SFP check. Current and comparative checks now require the schedule to offset the existing signed debit/credit control total at canonical precision.

Regression cases cover positive equity and deficits, with both correct and reversed schedule signs. The deliberately reversed schedule still balances its own roll-forward, proving that an internally consistent schedule cannot conceal the SFP sign mismatch. Legitimate negative equity remains supported. Draft status and unresolved source-lineage checks remain unchanged.

Files: [presentation builder](../../src/AuditSphereOps.Application/Accounting/FinancialStatementPresentation.cs), [presentation tests](../../tests/AuditSphereOps.Domain.Tests/FinancialStatementPresentationTests.cs). This correction chooses no new accounting policy, tolerance, review order, source mode or delivery format.

## Next dependencies and acceptance

Read the existing T024–T034 contracts before extending source bindings, schedules or composition. Reuse accepted source identities and existing review/revision services. Do not add a second engine, renderer or completion ledger.

The next substantive reporting increment is the WP-03 source-binding/review foundation plus the smallest approved WP-04 indirect cash-flow path. Final-grade behavior requires the supported profile, source/method/classification decisions and independent methodology acceptance demanded by T003. Draft-only negative tests and technical discovery remain eligible while that evidence is pending.

The full attachment remains open: source-to-report lineage, reviewed cash/equity/disclosures, annual comparative scope, exact formatted-report decisions, final artifact selection and issuance, guided browser acceptance and external/professional sign-off. Synthetic arithmetic or a green draft renderer cannot close those requirements.

### Supplementary schedule identity in pinned reporting

The existing presentation-source manifest now includes persisted cash-flow, FX and equity rows, cash populations, opening/closing evidence references and the supplementary-input digest for both periods. Pin creation, pinned reads and source-current status use the same versioned computation. Signed amounts, movement dates, classification rationale, scope and currency participate in identity; query/reference ordering does not.

The existing `NoteSourceManifestHash` contract/storage field retains its name for compatibility but now carries `auditsphere.statement-source-manifest.v2`. Previously pinned drafts require an explicit new pin to meet this stronger source-current check; their stored artifacts remain historical evidence. No approved package or stored artifact is rewritten.

This closes a supplementary identity omission only. It does not resolve a text reference to an immutable accepted source, validate allocations, establish independent review or make `CASH_FLOW_SOURCE_LINEAGE`/`EQUITY_SOURCE_LINEAGE` pass. WP-03 remains partial. Actual checks and omissions are recorded in `status.json`.

## Consolidated practice/reporting handoff

The supplied `AS-PRACTICE-FR-COMPLETE-001` expands the execution scope through CP-00–CP-25 and preserves the earlier reporting criteria. These are consolidation aliases over the existing task board, not replacement authority. Source fingerprint and the labelled-criterion inventory are recorded in `status.json`. The document is proposed; its business fixtures and methodology are not silently professionally approved by implementation.

The immediate CP-00 baseline inspection found the analytics Qatar-week fixture correction and audit procedure submission fence already committed after the supplied baseline. Preserve them and assess current evidence rather than repeating either change.

### CP-17 / WP-07: mandatory canonical validation population

`FinancialPackageValidationReadiness` now evaluates the existing canonical package rule population consistently. Each mandatory code needs exactly one passing result in the exact firm/package scope. Missing, failed and ambiguous results refuse readiness. Build demotion, review, client management view, staff queue, current-release review checks and seal status/action consume it. Authorization precedes review diagnostics. Draft rendering and historical artifacts are unchanged.

This is a partial CP-17 increment, not complete final-accounting readiness: versioned reporting profiles, resolved source-lineage and reviewed notes, exact composition decisions, transaction-time authority rechecks and the integrated browser journey remain open. No mandatory criterion is marked complete merely because the canonical-rule guard passes. Existing source-lineage/note warnings remain in place.
