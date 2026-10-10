# ADR-0002: Angular is the canonical UI; Blazor Web is a rollback host

**Status: APPROVED** (owner-delegated approval recorded 2026-10-10) · Date recorded: 2026-10-08 · Decider: repository owner (authority delegated 2026-10-10)

## Context
The STE specification header names "Blazor Interactive Server" as the presentation technology. The owner later requested an Angular presentation layer served by `AuditSphereOps.Api`. The Web project was deleted prematurely in `59387b54`, then restored because source-action parity and retirement acceptance were still open (see `docs/architecture/auditsphere-angular-migration-current.md`).

## Decision
- `AuditSphereOps.Ui` (Angular 22, Material/CDK) owns canonical routes: staff under `/ui/app`, client portal under `/ui/portal`.
- `AuditSphereOps.Api` owns authorized contracts, authentication/consent and protected file transports; Angular calls same-origin `/api/ui/*` only.
- `AuditSphereOps.Web` stays buildable as a rollback/reference host, enabled for Development and Test, otherwise only with `LegacyPresentation:Enabled=true`. It is excluded from API composition.
- Retirement follows discover → inventory → map → compare → close gaps → verify → cut over → observe → remove, with the canonical readiness gate deciding.

## Alternatives considered
- Keep Blazor as the product UI: rejected by the owner.
- Delete Web as soon as Angular routes exist: tried and reverted; route existence is not parity.

## Consequences
- New screens are built in Angular only. Razor pages change only to keep rollback working.
- The specification header is stale on this point; the requirements copy should cite this ADR.
- Parity work is tracked in `docs/migration/` and the `angular-*-parity.json` files.
