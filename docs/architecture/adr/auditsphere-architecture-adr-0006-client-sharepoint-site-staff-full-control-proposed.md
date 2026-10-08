# ADR-0006: One SharePoint site per new client, with Full Control for assigned staff

**Status: PROPOSED** (retroactive record of an owner-approved scope change) · Date recorded: 2026-10-08 · Decider: repository owner

## Context
The specification provisions a five-folder directory per engagement (§4.2.3). The owner added one dedicated SharePoint site per new client and approved Full Control over the whole site for every assigned staff member, including engagement-only staff. Detail: `docs/architecture/auditsphere-client-sharepoint-sites-current.md`.

## Decision
- An isolated `client-sites` worker, holding `Sites.FullControl.All` for SharePoint and Graph under its own certificate, creates non-group-connected team sites, grants the document worker exact-site `write`, and reconciles assigned staff into an AuditSphere-managed site group.
- A persisted rollout boundary (`ClientSites:ClientsCreatedAfter`) leaves earlier clients on their existing repositories; newer clients never fall back to the shared site.
- Local `RoleGrant` scopes are unchanged; SharePoint access is separate external authority.

## Alternatives considered
- Shared site with per-engagement folders and `Sites.Selected` only: rejected by the owner for working convenience.

## Consequences
- **ISA 230 risk:** staff with Full Control can edit or delete working-site content after AuditSphere's local freeze. The local freeze, refused-write log and "05_Final Signed Archive" folder name do not prove immutability. Immutable issued evidence must stay in the independently controlled archive/release store; SharePoint read-only enforcement is `BLOCKED_EXTERNAL` (STE package 6). Spike SPK-01 evaluates options.
- Revocation is asynchronous Microsoft reconciliation, not instantaneous.
- Live use requires separate consent, certificate and controlled create/reconcile acceptance.
