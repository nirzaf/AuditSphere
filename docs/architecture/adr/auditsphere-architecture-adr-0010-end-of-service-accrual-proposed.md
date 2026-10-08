# ADR-0010: End-of-service benefits are accrued as a provision entered by a person

**Status: PROPOSED** (owner-delegated decision of 2026-10-09; owner confirmation pending, as for the other ADRs) · Date: 2026-10-09 · Decider: repository owner, by delegation

## Context

- Specification §3.5 lists "Staff Salaries, End of Service, & Benefits" among firm expenses. `FirmExpenseCategories` holds `RENT`, `SALARIES`, `PETTY_CASH`, `UTILITIES` and `OTHER`. No end-of-service accrual exists (STE-NXT-009).
- End-of-service benefits are earned as staff work. An expense recognised only when paid understates the firm's liability and its period expense.
- AGENTS.md forbids autonomous professional conclusions. The platform must not compute an actuarial or statutory estimate.

## Decision

- Record the obligation as a monthly accrual journal to a provision account, through the existing ledger maker and checker (option b of STE-NXT-009).
- A person enters the amount, with its basis: the method, the inputs and the date of the calculation. The platform stores and displays that basis; it does not calculate it.
- Corrections are reversing journals. Posted accruals stay append-only.
- The follow-up story STE-NXT-014 holds the acceptance criteria. No code is part of this ADR.

## Alternatives considered

- **Option (a), an END_OF_SERVICE expense posted when paid.** Rejected: it recognises the cost only at payment, so the balance sheet omits the obligation.
- **Option (c), leave it under SALARIES or OTHER.** Rejected: it hides the obligation and gives no evidence trail.

## Consequences

- The firm needs a provision account in its chart before the first accrual.
- The owner confirms the accounting treatment for the firm's reporting framework before the first posting. This ADR does not decide that treatment.
- The amount is only as reliable as its entered basis. Reviewers check the basis; the platform does not check the estimate.
