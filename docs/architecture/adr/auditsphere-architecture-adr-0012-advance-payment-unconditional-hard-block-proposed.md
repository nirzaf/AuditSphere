# ADR-0012: The 50% advance is an unconditional activation hard block with one recorded opt-out

**Status: PROPOSED** · Date: 2026-10-09 · Decider: repository owner

## Context

STE v2.1 §4.1.5 and mandatory control C-02 require the recorded 50% advance to gate the transition into
`PORTAL_ACTIVE_PLANNING` ("50% paid and recorded before portal-active planning transition", enforcement *Hard block*).
The source fixes the split at 50/50 (`M1.02-R01` Brief Quotation payment terms, `M1.05-R01` 50% Advance Invoice).

The implemented Partner activation gate enforced the advance **only when an `EngagementFeeAgreement` was already
linked** to the engagement (`EngagementLifecycleService.ActivateAsync`). An engagement with no linked agreement
activated with no advance recorded at all. At the same time the read-only lifecycle projection
(`EngagementLifecycleQuery`, stage 4 `ADVANCE_BILLING`) always reported the advance as required, and the
requirements-aligned architecture suite stated "no optional linked-fee bypass". The code, not the requirement, was the
outlier.

## Decision

1. The advance is an **unconditional hard block by default**. Activation refuses with `GateBlocked` unless a linked
   engagement fee agreement carries a `FeeMilestone` of kind `ADVANCE` in state `PAID`.
2. Enforcement is **configurable** through `EngagementActivation:AdvanceGateMode`:
   - `ALWAYS` (default) — control C-02; the advance is required whether or not an agreement is linked.
   - `WHEN_FEE_AGREEMENT_LINKED` — recorded deviation; the advance is required only when an agreement is linked. A
     linked agreement always gates, so the deviation narrows the rule and never disables it.
   An unset, empty or unrecognised value enforces `ALWAYS`, and `EngagementActivationOptions.Validate()` refuses
   startup on an unrecognised value, so the gate is never weakened by a typo or a missing key.
3. The activation workspace reports the same rule as an explicit blocker (`advance.fee-agreement-missing`,
   `advance.unpaid`), so the Partner-facing review never implies eligibility the command would refuse, and the
   review-basis digest includes the fee-agreement and advance state.

## Alternatives considered

- **Keep the linked-only behaviour and record *it* as the deviation.** Rejected: it contradicts a mandatory control, and
  the projection and the architecture suite already asserted the stronger rule.
- **A boolean `RequireAdvance`.** Rejected: a boolean cannot express the existing linked-only behaviour, so the opt-out
  would have disabled the requirement outright instead of narrowing it.
- **No configuration at all.** Rejected by the owner: the enforcement point must be adjustable for engagements whose
  commercial route is decided separately.

## Consequences

- New deployments enforce control C-02 by default; the previous linked-only behaviour is available only by configuring
  the deviation explicitly.
- `EngagementActivationWorkspace.StateAsync`/`PreviewAsync`/`ExecuteAsync` take `EngagementActivationOptions`, and
  `EngagementLifecycleService.ActivateAsync` takes it as an optional trailing parameter.
- Activation fixtures must supply a linked agreement with a paid advance, or configure the deviation mode explicitly.
- The architecture suite's Flow 1 statement ("no optional linked-fee bypass") is true of the default configuration.
- This aligns code with an existing requirement; it creates no new capability and changes no external scope.
