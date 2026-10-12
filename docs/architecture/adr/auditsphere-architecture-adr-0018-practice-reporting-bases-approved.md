# ADR-0018: Practice reporting bases for time value, utilization and receivables

**Status: APPROVED** (owner-delegated decision recorded 2026-10-12) · Date: 2026-10-12 · Decider: repository owner, who told the implementing agent on 2026-10-12 to complete the practice management program after being shown the open decisions

## Context

The practice management completion program (`docs/execution/auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md`) lists decisions DEC-01 to DEC-11 that gate its stories. Some set how a firm report is calculated. The rest are professional, legal or commercial judgments.

This record decides only the first kind. Each item below changes how a figure is computed or which records a report includes. None approves an accounting treatment on an engagement, a letter clause, a sampling method or a consent rule.

## Decision

1. **Non-billable and no-charge time (part of DEC-05).** Approved non-billable and no-charge time counts in actual hours and in actual staff cost. It has no charge-out value, so it needs no rate and does not decide a report's currency. Standard value, realization and contract contribution are computed from approved billable time only. Billable time with no captured rate or currency still makes those figures unavailable. The non-chargeable hours are shown beside actual hours so the difference is visible.
2. **One utilization basis (part of DEC-05).** Utilization is approved billable minutes divided by capacity net of recorded unavailability. Department analytics already used this basis; the weekly resource grid now uses it too. The grid still shows all approved hours as a separate figure.
3. **Receivables scope.** The firm receivables report ages every posted firm invoice. An invoice bound to one fee milestone is typed as advance or final. An invoice built from approved time is typed as time and attributed to an engagement only when all of its time belongs to that engagement. Any other firm invoice is typed as other. An invoice bound to more than one milestone still refuses the report. Client bookkeeping invoices are a different record in the client accounting workspace and are never included.
4. **Monthly profit and loss.** "Monthly" means one row per accounting month in the selected range, with the same month one year earlier as the comparator. A formatted statement layout is not part of this decision.
5. **Accountable person on the dashboard.** The engagement dashboard names the users currently staffed at the level that owns the step. It is a display of live staffing and grants nothing.
6. **Rate aliases.** The STE aliases (Senior, Supervisor, Junior, Associate) apply wherever a rate is looked up: time capture, budgets and quotations.

## Not decided here

These stay open because they need a qualified practitioner, the firm's legal position, or a commercial choice:

- **DEC-01, DEC-03:** what counts as current client consent on renewal, and when an acceptance or continuance decision expires.
- **DEC-02:** engagement letter clauses, representation-letter figures and the management letter narrative.
- **DEC-04:** accounting framework editions, FX and consolidation policy, and end-of-service measurement.
- **DEC-05, remainder:** staff-level performance measures and who may see them; the facility and benefit expense taxonomy.
- **DEC-06:** the receipt policy for partial payments and any broader fee model.
- **DEC-07:** procedure assignment policy, confirmation alternatives, the significant-estimates register and the sampling method.
- **DEC-08:** whether a firm certificate is required in production, and the countdown semantics where the specification and the projection differ.
- **DEC-09:** contact-purpose routing precedence.
- **DEC-10, DEC-11:** recovery, residency and performance targets; multi-firm service, public API, connected inbox and AI.

## Alternatives considered

- **Value non-billable time at the staff member's standard rate.** Rejected. It would report charge-out value for work the firm chose not to charge, and inflate the denominator of realization.
- **Keep fee-milestone-only receivables.** Rejected. A posted time invoice is a receivable of the firm and was missing from the only ageing report.
- **Decide the professional items by analogy with ADR-0017.** Rejected. ADR-0017 set product definitions. The items listed above bind the firm's professional or legal position and are not the implementing agent's to set.

## Consequences

- Realization and contract contribution become available for engagements that carry approved non-billable time. Their values exclude that time.
- The weekly grid's utilization percentage falls for anyone with approved non-billable time, because it now counts billable time only.
- The receivables report shows more invoices and higher totals wherever posted non-milestone invoices exist. Totals from before this change are not comparable.
- No schema changes.
- The owner can reverse any item with a later ADR that supersedes it.
