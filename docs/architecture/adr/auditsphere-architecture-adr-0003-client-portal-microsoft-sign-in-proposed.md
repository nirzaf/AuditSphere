# ADR-0003: Client portal uses Microsoft sign-in instead of emailed temporary passwords

**Status: PROPOSED** (retroactive record) · Date recorded: 2026-10-08 · Decider: repository owner

## Context
Specification §3.1 and §4.1.5 say the system emails temporary credentials to the client audit liaison and forces a password reset on first login before uploads unlock. The implementation authenticates client users through Microsoft Entra (optionally via a separately consented guest invitation) and withholds PBC and signed-LOR uploads until the identity path's first-sign-in requirement is met (`docs/execution/auditsphere-ste-specification-coverage-current.md`, rows 4.1.5 and "Deliverable assembly and portal safety").

## Decision
- No local passwords are issued, stored or emailed for client users.
- Portal access for a newly converted client requires both dual-key records, engagement activation and a paid advance.
- The specification's "mandatory reset before upload" intent is met by the first-sign-in requirement of the Microsoft identity path.
- Guest invitation is its own capability (`User.Invite.All`), off by default, usable only after verified consent.

## Alternatives considered
- Local accounts with emailed temporary passwords, as written: adds a credential store, reset flows and phishing exposure that Entra already handles.

## Consequences
- Live behaviour depends on gates P1 and M365-ADMIN (`BLOCKED_EXTERNAL`).
- Existing clients without conversion records keep their earlier onboarding contract.
- The requirements copy still contains the password wording; it should reference this ADR instead.
