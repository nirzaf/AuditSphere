# Axelor-inspired practice UX — implementation tracker

**Status:** CURRENT

**Purpose:** UX-00 parity inventory and story dispositions for the owner-assigned [Axelor-inspired practice UX specification](../design/auditsphere-ux-spec-axelor-inspired-practice-workspaces-proposed.md). Axelor is a pattern reference only; no Axelor code, assets or runtime are used.

Implementation baseline: `81873f58` (the specification inspected `89fa2f22`; the only intervening changes are the SharePoint-primary storage slices, which this work does not touch). Observed test and CI results belong in `status.json`; this tracker records scope, preserved behavior and remaining evidence.

## Non-regression rules applied to every story

- Existing routes, deep links, write contracts, command/recovery flows and server authorization are unchanged; new query parameters and response fields are optional and additive.
- Summaries state their scope. A count derived from loaded rows is labelled page-only; complete counts come from the server for the same filters and read snapshot.
- No change to Microsoft Entra, SharePoint, mail, storage, archive, financial formulas, fee policy, task lifecycle or reviewer independence.

## Parity inventory and dispositions

| Story | Screen / owner | Existing actions and links (all preserved) | Change | Disposition |
| --- | --- | --- | --- | --- |
| UX-02 | `/app/practice/leads` · `features/commercial/leads.ts` · `PracticeLeadQuery` · `GET /api/ui/leads` | Search; refresh; record lead with saved-request recovery; qualify with confirm/cancel and focus return; paging; lead detail link; commercial settings link | Optional `status`, `source` (canonical channels), `owner` (`me`, `unassigned` or a user ID) filters validated server-side; owner column with UNASSIGNED/ACTIVE/DISABLED/MISSING states; complete `summary` counts (matching, awaiting qualification, qualified) read in the same RepeatableRead snapshot as the page; creation moved into a panel that opens for an empty list or a saved draft. A server without `summary` falls back to an explicit “New on this page” label. | Implemented; focused `WorkspaceQueryTests` and `leads.spec.ts` pass. Browser review pending (UX-13). |
| UX-12 | `/app/practice/analytics` · `analytics.ts` · `PracticeAnalyticsQuery` | — | — | Pending |
| UX-03 | Commercial pipeline (new read-only view) | — | — | Pending |
| UX-08 / UX-09 | Finance invoices · `invoice.ts` · billing queries | — | — | Pending |
| UX-05 / UX-06 / UX-07 | Work queue, resources, templates and time | — | — | Pending |
| UX-10 / UX-11 / UX-04 | Receivables, unbilled time, firm books, proposal detail | — | — | Pending |
| UX-13 | Integrated parity | — | — | Pending: browser review, exact-source CI |
