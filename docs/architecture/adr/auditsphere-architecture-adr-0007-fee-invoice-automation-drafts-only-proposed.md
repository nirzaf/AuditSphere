# ADR-0007: Automatic advance and balance invoicing creates drafts only

**Status: PROPOSED** (retroactive record) · Date recorded: 2026-10-08 · Decider: repository owner

## Context
The specification issues the 50 % advance invoice with the engagement letter and releases the 50 % balance invoice with the deliverables (§4.1.5, §4.4.2). Firm finance keeps maker/checker separation: FinanceReviewer approves, FinanceManager posts.

## Decision
- Issuing the gated engagement letter creates the reviewed fee agreement and both milestones in the same transaction.
- An optional general-worker policy (`AutomaticFeeInvoices:Enabled`, `FinanceUserId`, `ApprovingAdministratorId`), **off by default**, creates draft invoices under captured FinanceManager and Administrator authority. The advance becomes eligible after the letter; the balance after advance payment and financial-package release.
- Automation never approves, posts, sends or collects. Withdrawal of either captured authority blocks execution.
- The fee workspace exposes an explicit advance-invoice preparation state (`AdvanceInvoicePreparationStates`), including "pending — automation disabled".

## Alternatives considered
- Fully automatic issue and posting: removes the finance maker/checker control.
- Impersonating the Partner as finance staff: rejected; identities must be real and current.

## Consequences
- "Automated release of the remaining 50 % bill" in the specification means a draft ready for review, not a sent invoice.
- Deployment must configure the policy explicitly to get any automation.
