# ADR-0014: Retain six-decimal monetary precision and explicit ToEven rounding

**Status: APPROVED** · Date: 2026-10-10 · Decider: repository owner (delegated project decision)

## Context

R2R-ADR-03 asks for a recorded numeric representation and rounding rule. The code already uses `decimal`, limits `MoneyPolicy.Normalize` to six decimal places, defaults midpoint handling to `ToEven`, stores core monetary values in PostgreSQL `numeric(19,6)`, and uses explicit per-property precision where a value needs a different range or scale. Some commercial presentation calculations deliberately request a two-decimal scale. Changing these conventions globally would silently alter existing amounts and persisted evidence.

## Decision

- Preserve the existing six-decimal maximum and `MidpointRounding.ToEven` default in `MoneyPolicy`. A named, versioned module policy may specify different scale or midpoint behavior; callers must not introduce an implicit override.
- Preserve PostgreSQL `numeric(19,6)` as the default core monetary column type. Entity-specific precision overrides remain explicit and require a reviewed migration when they change.
- Keep currency conversion and translation rules in their named calculators; never use binary floating point, mix currencies implicitly, or invent a missing rate.
- This decision records an implementation-level numeric convention only. It does not approve an accounting standard edition, a measurement method, a market-rate source, an exchange-rate effective period, presentation rounding for financial statements, or any of the GOLD-R2R expected outcomes. Those remain under qualified methodology-owner and independent-review approval in T003.

## Alternatives considered

- Reduce all amounts to two decimal places: rejected because it discards supported six-decimal source precision and would change persisted arithmetic.
- Use binary floating point: rejected because it cannot preserve the exact decimal values required by the accounting workflows.
- Infer FX rates or rounding from a currency code: rejected because a currency label is not an approved rate or method.

## Consequences

- No schema migration is required; the decision records and protects the existing implementation contract.
- Callers must select a smaller scale or alternate midpoint explicitly at a named business boundary rather than applying a global display rule to stored amounts.
- R2R-ADR-03 is resolved for core numeric representation and computational rounding only. FX methodology and professional policy approval remain blocked under T003.
