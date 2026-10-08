# AuditSphere — STE Closure Roadmap

**Status: PROPOSED.** Orders the existing backlog and the new stories into milestones. Each milestone leaves `master` buildable and deployable. Dates are not set; the owner sets them.

## M0 — Trustworthy ground (first)

| Item | Why first |
| --- | --- |
| STE-NXT-001 Restore test suites (owner go-ahead) | Every later acceptance criterion depends on executable tests. |
| STE-NXT-002 Agent context hygiene + SPK-04 | Agents currently burn context on padding and follow dead references. |
| SPK-02 Recover STE-GAP definitions | Prevents duplicate or contradictory stories. |
| Owner confirms ADR-0001 to ADR-0008 | Stops agents reverting deliberate deviations. |

**Exit:** suites green in CI; `status.json` slim; ADRs approved or corrected.

## M1 — Wire the built-but-hidden STE features

| Item | Depends on |
| --- | --- |
| STE-NXT-003 Rate-card administration and QAR baseline | M0 tests (or manual evidence per DoD §2) |
| STE-NXT-004 Practical rounding UI | — |
| STE-NXT-005 Early compliance lock UI | — |
| STE-NXT-006 Advance-preparation display | — |
| STE-NXT-008 Verify unchecked rows | — |

**Exit:** every specification row in the verification table is `PRESENT`, `DEVIATION`, `EXTERNAL` or `DECISION` — no `GAP` except AS-PAR-009.

## M2 — Commercial completion

| Item | Depends on |
| --- | --- |
| AS-PAR-009 Pricing review, evidence-bound dispatch, client response | Firm-approved pricing basis/limit policy (owner) |
| STE-NXT-007 Requirements copy reconciliation | ADR approval (M0) |
| STE-NXT-009, STE-NXT-010 Owner decisions, then their follow-up stories | SPK-03 |

**Exit:** a lead can go to a signed, dispatched, client-accepted proposal entirely in Angular.

## M3 — Authorization audit and Blazor retirement

| Item | Source |
| --- | --- |
| AS-PAR-002 Remaining scope and revocation coverage | `status.json` `remainingLocalWork` |
| Angular source/action parity rows to parity-verified | `docs/migration/auditsphere-migration-blazor-feature-parity.md`, `auditsphere-migration-blazor-removal-readiness.md` |
| Canonical readiness gate → owner retirement decision | ADR-0002 |

**Exit:** readiness gate `READY`; owner accepts retirement; Web project removed in a separate change.

## M4 — External acceptance (owner-led, not agent work)

P1 Entra OIDC · P2/P2b SharePoint and isolation · SPK-01 outcome for the archive · P3 release checkpoint store · P7 recovery RPO/RTO · P8 secrets, observability, capacity (and the open NFR targets) · P9 independent review and protected merge · P10 real-tenant acceptance · M365-ADMIN.

Agents may prepare runbooks and evidence templates for these; they never mark them closed (`AGENTS.md` §5).
