# AuditSphere architecture guide

**Status: CURRENT · Consolidated architecture navigation · Reconciled 2026-11-07**

This page is a category guide, not an approval source. Follow [`AGENTS.md`](../../AGENTS.md), the current architecture, and each applicable ADR's own status. The [documentation index](../auditsphere-docs-index.md) defines document authority and the wider category map.

## Runtime and code ownership

AuditSphere is a .NET modular monolith with an ASP.NET Core API, Angular 22 UI, PostgreSQL persistence, and a .NET worker. Business rules, authorization, and state transitions remain in Domain/Application; Infrastructure owns EF Core and providers; API owns authenticated HTTP/OpenAPI boundaries; Angular presents server-authorized projections; Worker owns durable background processing. See the [current architecture](auditsphere-architecture-current-architecture.md) and [capability code map](auditsphere-architecture-code-map.md) for implementation ownership.

Preserve three separate financial domains: the firm's own ledger, each client's accounting books, and group consolidation. Consolidation does not post to component books; client books do not post into the firm's ledger. VAT/tax is separately scoped. Payments, external filing, payroll execution, procurement, and inventory are not implied by a practice-management feature.

## Architecture and contracts

| Concern | Canonical source |
|---|---|
| Application structure, scope, locks, revisions, and immutable data | [Current architecture](auditsphere-architecture-current-architecture.md) · [data model contract](auditsphere-architecture-contract-data-model-current.md) |
| Domain-to-Application-to-API-to-UI ownership | [Code map](auditsphere-architecture-code-map.md) |
| HTTP, authentication, error shapes, OpenAPI and drift | [HTTP boundary](auditsphere-architecture-http-boundary-and-contract-current.md) · [`contracts/auditsphere-openapi.json`](../../contracts/auditsphere-openapi.json) |
| UI conventions, decoders, server `can*` states | [Angular conventions](auditsphere-angular-conventions-current.md) |
| User, role, grant, and scope semantics | [Roles and privileges](auditsphere-architecture-user-roles-and-privileges-current.md) |
| Microsoft permissions and selected-client-site exception | [M365 permission model](auditsphere-m365-tenant-administration-permissions.md) · [client SharePoint sites](auditsphere-client-sharepoint-sites-current.md) |
| Non-functional limits | [NFR register](auditsphere-architecture-policy-non-functional-requirements-current.md) |
| Decisions | [ADR register](adr/auditsphere-architecture-index-adr-register-current.md) and the linked individual ADRs |
| Requirements and task authority | [Requirements index](../requirements/auditsphere-requirements-index-current.md) · [current task board](../execution/auditsphere-execution-index-task-board-current.md) |

## Authority and evidence boundary

Individual `APPROVED` or `ACCEPTED` ADRs govern only their stated decisions. `PROPOSED` documents and pinned historical task sources are context, not authorization. The consolidated [task library](../task_breakdown/auditsphere-r2r-index-task-breakdown.md) links to the exact source revision and does not assert current completion. Live implementation, CI, browser, professional review, and external acceptance are recorded separately in [execution status](../execution/status.json).

Never widen firm/client/engagement/group grants, bypass session freshness or professional holds, mutate sealed evidence or posted accounting history, or infer approval from Angular flags. External providers and tenant effects remain separately gated.
