# ADR-0011: Materiality uses profit before tax without normalization

**Status: APPROVED**, confirmed by the repository owner on 2026-10-09 · Date: 2026-10-09 · Decider: repository owner

## Context

- Specification §4.2.4 refers to "Normalized Profit Before Tax".
- `MaterialityEngineService` labels its PBT benchmark "mapped balances, excluding tax; no normalization applied".
- Spike SPK-03 (`docs/execution/auditsphere-audit-report-normalized-pbt-spk-03-proposed.md`) recommends no normalization now (STE-NXT-010, Option A).

## Decision

- The PBT benchmark is mapped balances excluding tax. No normalization adjustments are applied.
- The manager keeps the existing controls: the mapped-line benchmark, the rate policy, and practical rounding within ±5 %.
- Normalization (Option B) becomes a separate story if the owner later wants it. That story must define the adjustments, who approves them and how they are evidenced.

## Alternatives considered

- **Option B now.** Rejected: no approved list of normalization adjustments, approvers or evidence rules exists, so any normalized figure would be unsupported.

## Consequences

- The spec's "normalized" wording is a recorded deviation. It is listed in the approved-deviations section of the requirements copy.
- Auditors see the unnormalized benchmark and can challenge it through the existing review gates.
