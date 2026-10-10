# AuditSphere — ADR Register

**Status: CURRENT** (register). Individual records carry their own status.
**Rule for agents:** a feature task never reverses an ADR. If a task appears to need that, stop and report the conflict.

ADR-0001 to ADR-0007, ADR-0009 to ADR-0011, and ADR-0013 to ADR-0015 are approved under the repository owner's delegated project decision dated 2026-10-10. ADR-0012 is accepted as the unconditional activation control. These decisions do not approve professional accounting treatments, live provider behavior, external consent, or independent release acceptance. ADR-0008 is superseded.

| ID | Decision | Status | Supersedes / relates to |
| --- | --- | --- | --- |
| [ADR-0001](auditsphere-architecture-adr-0001-modular-monolith-static-capability-services-approved.md) | One modular monolith; static capability services returning `CommandResult`, no mediator layer | APPROVED | Records the variation from R2R-ADR-02 |
| [ADR-0002](auditsphere-architecture-adr-0002-angular-canonical-ui-blazor-rollback-approved.md) | Angular 22 is the canonical UI; Blazor Web kept as rollback until the readiness gate | APPROVED | Specification header "Blazor Interactive Server" |
| [ADR-0003](auditsphere-architecture-adr-0003-client-portal-microsoft-sign-in-approved.md) | Client portal uses Microsoft sign-in, not emailed temporary passwords | APPROVED | Deviates from specification §3.1 / §4.1.5; live tenant gates remain separate |
| [ADR-0004](auditsphere-architecture-adr-0004-engagement-lifecycle-derived-projection-approved.md) | Specification §5 lifecycle is a derived projection over gate records | APPROVED | Specification §5 |
| [ADR-0005](auditsphere-architecture-adr-0005-visual-credentials-no-esignature-purview-approved.md) | PNG signature/seal as visual credentials; eSignature and Purview out of scope | APPROVED | R2R-ADR-08; gates P4, P5; PKI-signing exclusion superseded by ADR-0016 |
| [ADR-0006](auditsphere-architecture-adr-0006-client-sharepoint-site-staff-full-control-approved.md) | One SharePoint site per new client; assigned staff get Full Control | APPROVED | Provider acceptance and exact-site grants remain separately gated |
| [ADR-0007](auditsphere-architecture-adr-0007-fee-invoice-automation-drafts-only-approved.md) | Automatic 50/50 invoicing creates drafts only | APPROVED | Specification §4.1.5, §4.4.2 |
| [ADR-0008](auditsphere-architecture-adr-0008-automated-test-suites-temporarily-removed-superseded.md) | Automated test suites removed temporarily, then restored (STE-NXT-001) | SUPERSEDED | Restored by STE-NXT-001; the Definition of Done follows the restored suites |
| [ADR-0009](auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md) | Issued evidence is authoritative in the release and archive store; the SharePoint archive is a working copy | APPROVED | Relates to ADR-0005, ADR-0006; STE-GAP-007 stays BLOCKED_EXTERNAL |
| [ADR-0010](auditsphere-architecture-adr-0010-end-of-service-accrual-approved.md) | End-of-service benefits are accrued as a provision entered by a person through the ledger maker and checker (STE-NXT-009, option b) | APPROVED | Posting waits for the accounting treatment, which is not yet confirmed; the platform computes no estimate |
| [ADR-0011](auditsphere-architecture-adr-0011-materiality-pbt-without-normalization-approved.md) | Materiality uses profit before tax without normalization (STE-NXT-010, Option A) | APPROVED | Recorded deviation from specification §4.2.4 |
| [ADR-0012](auditsphere-architecture-adr-0012-unconditional-advance-activation-hard-block-current.md) | The 50% advance is an unconditional `PORTAL_ACTIVE_PLANNING` hard block with no configuration bypass | ACCEPTED | Implements specification §4.1.5 / control C-02; relates to ADR-0007 |
| [ADR-0013](auditsphere-architecture-adr-0013-approved-mapping-lineage-for-practice-time-approved.md) | Engagement tasks and time entries pin the exact approved FSLI mapping and destination | APPROVED | Technical attribution only; no accounting classification or posting decision |
| [ADR-0014](auditsphere-architecture-adr-0014-monetary-scale-and-rounding-approved.md) | Preserve six-decimal monetary arithmetic, explicit scale changes and ToEven rounding | APPROVED | Resolves R2R-ADR-03's core numeric convention; FX methodology and professional policy approval remain under T003 |
| [ADR-0015](auditsphere-architecture-adr-0015-reporting-context-identity-pins-approved.md) | Bind exact reporting contexts to existing scoped identities and approved revisions | APPROVED | Resolves T014's technical identity decision; implementation follows T013 and T007 |
| [ADR-0016](auditsphere-architecture-adr-0016-certificate-signed-final-report-approved.md) | The final signed report is certificate-signed with a firm-held certificate; fails closed when required | APPROVED | Supersedes only the PKI-signing exclusion in ADR-0005; eSignature providers and Purview stay out of scope |

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
