# Normalized profit before tax: methodology note (SPK-03)

**Status: PROPOSED.** Methodology note for the owner's decision in STE-NXT-010. It records no decision. Time box: 1 day. It ships no production code.

## 1. The question

Specification §4.2.4 names "Normalized Profit Before Tax" as a materiality benchmark (5 % to 10 %). The current code computes PBT from mapped balances excluding tax and states "no normalization applied" (`MaterialityEngineService`, and the requirements copy). Three questions follow:

1. What should "normalized" mean for STE: which items are excluded or adjusted?
2. Who identifies each adjustment, and who approves it?
3. How is each adjustment evidenced, so that the benchmark can be reproduced from sealed data?

## 2. What normalization could include

| Adjustment class | Typical content | Risk if inferred rather than recorded |
| --- | --- | --- |
| One-off items | Gains or losses on disposal, restructuring, litigation settlements, impairment reversals | High: judgement about what is "one-off" changes the benchmark |
| Discontinued operations | Results of a component that is sold or closed | Medium: needs the discontinuation evidence and the component's mapping |
| Owner or key-person remuneration | Remuneration above or below a market rate for owner-managers | High: a judgement with a related-party dimension |
| Other recurring-versus-non-recurring items | Recurring accruals that management treats as exceptional | High: easy to use to flatter the benchmark |

Each class needs an identified source and a named approver. None of them can be derived from the trial balance alone.

## 3. Options for the owner

**Option A: no normalization.** The benchmark stays "mapped PBT excluding tax". The Audit Manager uses a mapped-line benchmark or adjusts the rate within the policy range. This matches the current code and needs no new data model. It changes the requirements copy (STE-NXT-007) to say the deviation is intentional.

**Option B: recorded adjustments.** Normalized PBT = mapped PBT + the sum of recorded adjustments. Each adjustment is an input with its own record: class, amount, source document identity, reason, preparer, and an approval by a person who is not the preparer. The calculator stays pure: it receives the adjusted total as an input and never looks up or infers an adjustment.

**Option C: inferred normalization.** Rejected. It violates the rule that adjustments are never inferred, and it would make the benchmark impossible to reproduce from sealed data.

## 4. Recommendation

Option A now, with Option B recorded as the follow-up if the owner wants normalized PBT. The reasons:

- Option B needs a new record type, an approval gate and evidence rules before any figure can be used. Nothing in the repository supplies them today.
- Option A is what the code already does and is reproducible from sealed data.
- If Option B is chosen later, the calculator change is small: one additional input, with no change to the rate ranges.

## 5. Worked example using the specification's figures

The specification gives a planning materiality of **QAR 53,421** and a practical rounding example of 53,421 to 53,000 (§4.2.4).

Step 1: the benchmark implied by that planning materiality, at the ends of the PBT range (5 % to 10 %):

| Rate applied | Implied mapped PBT (QAR) |
| --- | --- |
| 5 % | 1,068,420 |
| 7.5 % (mid-point, illustrative) | 712,280 |
| 10 % | 534,210 |

Step 2: the practical rounding. 53,000 is 0.79 % below 53,421, inside the ±5 % limit. Tolerable error and SAD follow the same rule.

Step 3 (Option B only, illustrative): assume the mapped PBT is 712,280 and two adjustments are recorded and approved: a one-off loss added back (+40,000) and owner remuneration above market removed (−15,000). Normalized PBT is 737,280. At the same 7.5 % rate, planning materiality becomes 55,296. The change is +3.5 % against 53,421, so Option B would need the same ±5 % rounding and Partner approval as any other change. These adjustment amounts are invented for the example. They are not specification figures.

The example shows the core constraint: an adjustment moves the benchmark, so each adjustment needs its own evidence and approval before the benchmark can be relied on.

## 6. Constraints the design must keep

- The calculator is pure: no clock, no network, no EF, and no lookup of adjustments. Adjustments arrive as recorded inputs.
- Each adjustment is an append-only record with its own approval. Changing one creates a new revision.
- A change to the adjusted PBT makes the materiality stale (`MaterialityEngineService` already refuses approval of a stale calculation).
- The record must identify the sealed trial balance and mapping version it adjusts.

## 7. Decision needed

The owner chooses Option A or Option B (STE-NXT-010). Option A is recorded as an ADR when chosen. Option B needs the follow-up story in STE-NXT-010 with its acceptance criteria before any code.
