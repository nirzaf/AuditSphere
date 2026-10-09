# AuditSphere — Deduplicated Execution Backlog

**Status:** CURRENT  
**Reconciled source:** latest synchronized `master` (2026-10-09; exact SHA is in `status.json`).  
**Purpose:** One dependency-ordered queue for implementation, test evidence, owner decisions, and external acceptance.  
**Authority:** This document orders work. The task board and linked stories retain exact acceptance criteria; `status.json` owns volatile verification facts; the STE manifest owns the machine-readable acceptance verdict.

## How to use this queue

Each item is tagged by the work needed:

- **CODE** — a product capability is missing or incomplete.
- **TEST/EVIDENCE** — implementation exists, but required execution evidence is absent or stale.
- **OWNER DECISION** — an accounting, architecture, or provider choice must be recorded before implementation proceeds.
- **EXTERNAL** — acceptance requires a live authorized tenant/provider, independent review, or production operations.
- **GOVERNANCE** — reconcile task wording, traceability, or card status with the accepted architecture and evidence.

Do not count overlapping IDs as separate defects. A story, follow-up, migration review, and task card may all point to one underlying action. Keep the IDs below as traceability aliases and use the task board for each card's exact criteria. A card is complete only under the card's evidence, dependency, and independent-review rules.

## Ordered queue

| Order | Deduplicated work item | Type | Traceability IDs | Exit condition |
|---|---|---|---|---|
| 1 | Re-establish current-source verification and acceptance evidence | TEST/EVIDENCE | T006, STE-NXT-001, STE-GAP-010 | Run the full Domain, API, Angular, and E2E suites on one exact source/test snapshot; reproduce the 15 historical E2E failures before classifying them; record complete-suite results in `status.json`; regenerate the manifest from that snapshot. A build or successful CI build is not a test-suite pass. |
| 2 | Close local archive and lifecycle acceptance; preserve provider boundary | CODE + TEST/EVIDENCE + OWNER DECISION + EXTERNAL | STE-GAP-006–008, STE-NXT-005, STE-NXT-011–012, J48, N02 | Run the scheduled-freeze early-lock and joined proposal/lifecycle browser journeys; resolve the provider-protection design and implement only the approved local boundary; verify the required regulator/read-only export. Provider-enforced immutability, provider readback, and direct-write denials remain external until observed live. The permanent local freeze and mandatory advance changes are already implemented. |
| 3 | Finish application authorization parity and Blazor migration review | CODE + TEST/EVIDENCE + GOVERNANCE | AS-PAR-002, T007–T010, migration source/action and supporting-file registers | Close the remaining command, query, count, export, retry, and revoked-session scope cases; review every migration row and supporting file against its current Angular/API owner; mark parity only with behavior-level evidence. Keep the Web rollback host while the retirement gate is `NOT_READY`. Rewrite Blazor-first task wording to Angular where it conflicts with ADR-0002. |
| 4 | Complete commercial workflow and finance acceptance | CODE + TEST/EVIDENCE + EXTERNAL | AS-PAR-009, STE-GAP-001–003, STE-GAP-005, STE-GAP-009, STE-NXT-003–004, STE-NXT-006–008 | Approve the pricing/review policy; verify service-specific letter generation and supersession; exercise advance invoice preparation through independent approval, posting, payment, receipt, and activation; exercise rounding and second-person rate-card approval through their Angular screens. Prove holding-letter delivery/recovery with authorized mailbox evidence; local queue tests do not establish delivery. |
| 5 | Resolve upstream accounting and methodology decisions | OWNER DECISION + GOVERNANCE | T003, T014, G16, STE-NXT-002, STE-NXT-007, STE-NXT-009–010, STE-NXT-014 | Record accounting framework/edition, resource bounds, golden fixtures, reporting-context rules, open-item methodology, and end-of-service treatment. Reconcile agent-context and deviation records. ADR-0011's no-PBT-normalization decision is already recorded; reconcile its remaining card status. Do not implement end-of-service accrual until its accounting treatment is approved. |
| 6 | Execute the dependent R2R implementation sequence | CODE + TEST/EVIDENCE | T004–T005, T011–T013, T015–T047 | First reconcile command/query and DTO contracts with the current static-service architecture; do not add MediatR or generic UnitOfWork patterns, and require deliberate approval for any new dependency. Resolve T003/T014 before dependent reporting setup. Then proceed through setup and TB intake (T011–T013, T015–T017), GL/adjustments/reconciliations (T018–T026), statements/packages/close (T027–T040), and group setup/FX/consolidation (T041–T047). T047 group-package assembly remains a functional gap and follows its consolidation prerequisites. |
| 7 | Complete audit workpaper capabilities after shared controls | CODE + TEST/EVIDENCE | T055–T075, STE-NXT-013 | Work through audit foundations, fieldwork, and final audit work in dependency order, retaining professional conclusions and approvals as human decisions. Independently verify the implemented statement-analysis links. Use the board's task cards for criteria; no range is complete by virtue of the module existing. |
| 8 | Complete release readiness and live acceptance | TEST/EVIDENCE + EXTERNAL | T048–T054, P1–P3, P7–P10, P2b, M365-ADMIN | Complete cross-module security/race and release-candidate checks, rehearse schema restore and deployment, establish independent release checkpoint and recovery evidence, verify production secrets/telemetry/capacity, obtain independent professional review, then complete authorized real-tenant acceptance. Keep P4/P5 out of scope and P6 locally verified as recorded in `status.json`. |

## Work-item crosswalk

The following crosswalk makes the overlapping registers explicit without copying their full acceptance text.

| Register | Where its open items land |
|---|---|
| STE-GAP-001–003, 005, 009 | Queue 4: implementation exists in part; missing execution and authorized delivery evidence remain. |
| STE-GAP-006 | Queue 2: scheduled-freeze early-lock browser acceptance. |
| STE-GAP-004, 007 | Queue 8 external tenant/provider acceptance; Queue 2 holds the local design and implementation prerequisite for GAP-007. |
| STE-GAP-008 | Queue 2: local projection is partly corrected; joined browser evidence and provider-state acceptance remain. |
| STE-GAP-010 | Queue 1: complete suite runs, current-snapshot evidence, regenerated manifest, and final acceptance. |
| STE-NXT-001 | Queue 1: current full-suite evidence. |
| STE-NXT-002, 007, 009–010, 014 | Queue 5: decisions and record reconciliation. |
| STE-NXT-003–004, 006, 008 | Queue 4: commercial, rounding, and rate-card acceptance. |
| STE-NXT-005, 011–012 | Queue 2: early lock, provider/archive design, and state acceptance. |
| STE-NXT-013 | Queue 7: independently verify and close statement-analysis links. |
| T003–T075 | Queues 1, 3, and 5–8 by dependency; all detailed card criteria remain on the generated task board. |
| AS-PAR-002 and migration gate | Queue 3: authorization parity and behavior-level Angular/API migration review. |
| AS-PAR-009 | Queue 4: commercial pricing, review, dispatch, client response, and durable evidence. |
| External gates | Queue 8, except the provider-protection design and local archive work ordered in Queue 2. |

## Reconciliation notes

- The review attachment predates the latest evidence-only commit. That commit added tests and citations for 34 STE journey steps, but did not run the full suites; those citations do not by themselves qualify as executed acceptance evidence.
- The 15 E2E failures in the ledger are from an older snapshot. They are historical until reproduced on the current source; do not classify them as current defects or silently retire them.
- The local terminal archive and unconditional paid-advance activation gate are implemented. Their local targeted checks passed, but the full acceptance matrix and provider-level archive protection remain open.
- The task board's audit record and completion rules remain the source for individual T-card status. This queue does not mark cards complete or convert local evidence into external acceptance.
- The recorded full-suite runs predate the current source/test tree. Keep affected steps `NOT_EXECUTED` until complete current-snapshot suites are recorded; the manifest must remain `FAIL` or `BLOCKED_EXTERNAL` unless its result rule is satisfied.
