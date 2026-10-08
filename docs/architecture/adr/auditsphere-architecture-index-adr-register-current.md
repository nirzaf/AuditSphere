# AuditSphere — ADR Register

**Status: CURRENT** (register). Individual records carry their own status.
**Rule for agents:** a feature task never reverses an ADR. If a task appears to need that, stop and report the conflict.

ADR-0001 to ADR-0008 were written on 2026-10-08 as **retroactive records of decisions already implemented**, using the commit history and the narrative docs cited in each. They are `PROPOSED` until the owner confirms them; on confirmation rename the suffix to `-approved` and set the status line.

| ID | Decision | Status | Supersedes / relates to |
| --- | --- | --- | --- |
| [ADR-0001](auditsphere-architecture-adr-0001-modular-monolith-static-capability-services-proposed.md) | One modular monolith; static capability services returning `CommandResult`, no mediator layer | PROPOSED | Records the variation from R2R-ADR-02 |
| [ADR-0002](auditsphere-architecture-adr-0002-angular-canonical-ui-blazor-rollback-proposed.md) | Angular 22 is the canonical UI; Blazor Web kept as rollback until the readiness gate | PROPOSED | Specification header "Blazor Interactive Server" |
| [ADR-0003](auditsphere-architecture-adr-0003-client-portal-microsoft-sign-in-proposed.md) | Client portal uses Microsoft sign-in, not emailed temporary passwords | PROPOSED | Deviates from specification §3.1 / §4.1.5 |
| [ADR-0004](auditsphere-architecture-adr-0004-engagement-lifecycle-derived-projection-proposed.md) | Specification §5 lifecycle is a derived projection over gate records | PROPOSED | Specification §5 |
| [ADR-0005](auditsphere-architecture-adr-0005-visual-credentials-no-esignature-purview-proposed.md) | PNG signature/seal as visual credentials; eSignature and Purview out of scope | PROPOSED | R2R-ADR-08; gates P4, P5 |
| [ADR-0006](auditsphere-architecture-adr-0006-client-sharepoint-site-staff-full-control-proposed.md) | One SharePoint site per new client; assigned staff get Full Control | PROPOSED | Consequence for ISA 230 archive |
| [ADR-0007](auditsphere-architecture-adr-0007-fee-invoice-automation-drafts-only-proposed.md) | Automatic 50/50 invoicing creates drafts only | PROPOSED | Specification §4.1.5, §4.4.2 |
| [ADR-0008](auditsphere-architecture-adr-0008-automated-test-suites-temporarily-removed-superseded.md) | Automated test suites removed temporarily, then restored (STE-NXT-001) | SUPERSEDED | Restored by STE-NXT-001; the Definition of Done follows the restored suites |
| [ADR-0009](auditsphere-architecture-adr-0009-archive-authority-release-store-proposed.md) | Issued evidence is authoritative in the release and archive store; the SharePoint archive is a working copy (draft from SPK-01) | PROPOSED | Relates to ADR-0005, ADR-0006; STE-GAP-007 stays BLOCKED_EXTERNAL |

## Earlier decision records (R2R)

R2R-ADR-01 to R2R-ADR-10 live in §2.4 of [baseline architecture and ADRs](../../task_breakdown/reference/auditsphere-r2r-reference-baseline-architecture-and-adrs.md): module ownership, mediator/bUnit (varied by ADR-0001), money/FX precision (`numeric(19,6)`, `decimal`, banker's rounding), framework editions, context identity and stale propagation, source/artifact storage, presentation/package split, release/records exclusions, consolidation methods, capability-to-role mappings.

## Template for new ADRs

File: `auditsphere-architecture-adr-<nnnn>-<slug>-proposed.md`

```markdown
# ADR-<nnnn>: <decision in one line>

**Status: PROPOSED** · Date: <yyyy-mm-dd> · Decider: <owner>

## Context
<the problem and the forces; cite files and commits>

## Decision
<what was chosen, stated so an agent can check code against it>

## Alternatives considered
<each option and why it lost>

## Consequences
<what becomes easier, harder, or forbidden; which gates or docs change>
```
