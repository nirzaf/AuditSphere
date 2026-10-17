# ADR-0002: Angular is the canonical UI; Blazor Web is a rollback host

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10) · Date recorded: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

> **Amendment (2026-10-17):** The owner authorized physical retirement of the Blazor rollback/reference host despite the canonical readiness gate remaining `NOT_READY`. The removal was merged to `master` as `e7ebe27c`. This ADR remains the historical decision; its parity and rollback conditions are not claimed as met.

## Context
The STE specification header names "Blazor Interactive Server" as the presentation technology. The owner later requested an Angular presentation layer served by `AuditSphereOps.Api`. The Web project was deleted prematurely in `59387b54`, then restored because source-action parity and retirement acceptance were still open (see `docs/architecture/auditsphere-angular-migration-current.md`).

## Decision
- `AuditSphereOps.Ui` (Angular 22, Material/CDK) owns canonical routes: staff under `/ui/app`, client portal under `/ui/portal`.
- `AuditSphereOps.Api` owns authorized contracts, authentication/consent and protected file transports; Angular calls same-origin `/api/ui/*` only.
- **Historical instruction, superseded by the 2026-10-17 retirement amendment:** `AuditSphereOps.Web` stays buildable as a rollback/reference host, enabled for Development and Test, otherwise only with `LegacyPresentation:Enabled=true`. It is excluded from API composition. The host is now retired; do not recreate it.
- **Historical removal condition, superseded by the owner's 2026-10-17 physical-retirement decision:** retirement follows discover → inventory → map → compare → close gaps → verify → cut over → observe → remove, with the canonical readiness gate deciding. The owner authorized removal despite `NOT_READY`; parity and operational rollback acceptance remain open.

## Alternatives considered
- Keep Blazor as the product UI: rejected by the owner.
- Delete Web as soon as Angular routes exist: tried and reverted; route existence is not parity.

## Consequences
- New screens are built in Angular only. The earlier instruction to change Razor pages only to keep rollback working is historical and superseded by the retirement amendment.
- The specification header is stale on this point; the requirements copy should cite this ADR.
- Parity work is tracked in `docs/migration/` and the `angular-*-parity.json` files.
