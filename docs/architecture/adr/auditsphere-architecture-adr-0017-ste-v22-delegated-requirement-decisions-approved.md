# ADR-0017: STE v2.2 delegated requirement decisions

**Status: APPROVED** (owner-delegated decision recorded 2026-10-12) · Date: 2026-10-12 · Decider: repository owner, who delegated these decisions to the implementing agent on 2026-10-12

## Context

The verified requirement map for STE Audit Management Requirements v2.2 (`docs/execution/auditsphere-execution-tracker-ste-v22-gap-verification-current.md`) left a group of rows open because each needed an owner decision. Three contradict a recorded decision. The rest needed a definition that the specification leaves open. The owner asked the implementing agent to decide them so the rows could be closed.

These are product decisions. They set how the platform behaves. They do not approve a professional accounting treatment on any engagement, and they claim nothing about live provider behaviour.

## Decision

### Where v2.2 conflicts with a recorded decision, the recorded decision stands

1. **WhatsApp.** WhatsApp is a lead-channel label only. Nothing is ingested from it or sent through it. Proposals are dispatched by email. This keeps the product-brief non-goal, and matches the owner's earlier instruction to remove WhatsApp from the dispatch row.
2. **Credentials by email.** ADR-0003 stands. Client users sign in through Microsoft. No password is issued, stored or emailed. The first-sign-in requirement meets the intent of the reset rule.
3. **Normalised profit before tax.** ADR-0011 stands. The benchmark is mapped balances excluding tax. Normalisation needs an approved list of adjustments, approvers and evidence rules, and none exists.

### New definitions

4. **Partner-only actions fail closed.** Clearing the Summary Review Memorandum, deciding the opinion, signing the report, verifying the signed representation letter and assembling the bundle need the engagement's staffed Engagement Partner. An engagement with no current Engagement Partner refuses these actions and says to staff one. The Partner role alone is not enough.
5. **Track B continuance questions.** The continuance bank gains two questions, both requiring evidence: whether a legal notice, claim or demand was received (`RV-031`, adverse when yes), and whether all prior-year professional fees are settled (`RV-032`, adverse when no). The existing questions are unchanged. The continuance template version moves from 1.0 to 1.1.
6. **Response channel.** A client acceptance records how the response arrived: `EMAIL`, `SIGNED_LETTER`, `IN_PERSON` or `PHONE`. The channel is required on acceptance and optional on a decline. There is no catch-all value. It labels the evidence and sends nothing.
7. **Working calendar.** The firm working week is Sunday to Thursday, with Friday and Saturday as the weekend. The week itself starts on Sunday and ends on Saturday, so the five working days fall in one allocation week. Capacity and utilisation read one definition. Public holidays are recorded as unavailability, because the dates are announced each year and no approved list exists. Milestone defaults stay relative to the statutory cutoff; the dates in the specification are an example.
8. **Materiality and risk definitions.**
   - *Planning approval* is the Partner's approval of the current materiality together with the risk routing. There is no separate planning memorandum.
   - *Critical accounting estimate* is a flag the assessor records on the risk assessment. A recorded flag bands the risk Red. Wording in the risk text stays as a floor, so nothing recorded earlier bands lower.
   - *High inherent risk* is a likelihood score of 3 with a magnitude score of 2 or more. Significant-risk and fraud flags band Red as before.
   - *Amber* requires a Senior Auditor or above to perform the work and an Audit Manager to review it, which is enforced. Whether the work uses sampling or tests the whole population is the senior's documented judgement. No gate requires a sample.

## Alternatives considered

- **Reverse ADR-0003 or ADR-0011 to match the v2.2 wording.** Rejected. Emailed passwords add a credential store and phishing exposure. A normalised benchmark with no approved adjustment list would be unsupported.
- **Keep the fallback that lets any Partner act on an unstaffed engagement.** Rejected. The completion screen already offers the opinion only to the staffed Engagement Partner, so the command was looser than the screen.
- **Reword the existing litigation and overdue-fee questions.** Rejected. Changing a prompt changes the meaning of answers already recorded against it. New questions leave history intact.
- **A catch-all response channel.** Rejected. It would let an acceptance be recorded with no stated route.
- **A firm holiday table.** Deferred. It needs a list the firm approves each year.
- **Require a sample for every Amber area.** Rejected. Small or fully tested populations would be forced into a sample that proves nothing.
- **Bump the risk rule version for the new flag.** Rejected. Completed results compare the captured rule version, so a new version string would mark every in-flight result stale. The rule is additive: an assessment without the flag bands exactly as before.

## Consequences

- One migration adds `proposals.response_channel` and `risk_band_assessments.critical_estimate`, and adds the two continuance questions where the continuance template exists.
- The band check constraint on `risk_band_assessments` now includes the recorded flag. Rolling the migration back fails while any assessment is Red only because of that flag; such a risk must be re-assessed first.
- An engagement with no staffed Engagement Partner cannot reach opinion, signing or bundle assembly until one is staffed.
- A continuance checklist in progress needs the two new answers before a decision can be recorded. Decisions already recorded keep the template version they pinned.
- Acceptances recorded before this change have no channel. The proposal page shows a channel only when one is recorded.
- Capacity and utilisation figures change for any range that includes a Friday or a Sunday.
- Allocation weeks start on Sunday. The owner asked for this on 2026-10-12, after the first version of this record kept a Monday start. Migration `QatarSundayWeekStart` moves each stored Monday-start allocation to the Sunday-start week that contains that Monday, one day earlier, and the database now refuses an allocation week that does not start on Sunday. Planning receipts committed before the change are historical evidence and keep the Monday they recorded.
- The requirements copy still carries the v2.2 wording for items 1 to 3. Those rows reference this ADR as recorded deviations.
- The owner can reverse any item with a later ADR that supersedes it.
