# AuditSphere documentation index

**Status: CURRENT · Navigation only · Reconciled 2026-11-07**

This index helps developers find the current source of authority. It does not create requirements, approve proposals, or certify release readiness.

## Authority and status

Follow [`AGENTS.md`](../AGENTS.md) first. Within its scope, use the current architecture and applicable approved ADRs, then current approved requirements. `PROPOSED`, `HISTORICAL_SOURCE`, and `SUPERSEDED` documents are reference material and do not authorize implementation. Execution ledgers and CI records describe observed work; they do not change policy or supply human, professional, tenant, provider, or deployment approval.

When documents differ, use this order:

1. Repository engineering and safety rules in `AGENTS.md`.
2. Current architecture and the specific `APPROVED` or `ACCEPTED` ADR that applies.
3. Applicable current approved product or technical requirements.
4. Proposed specifications and historical sources, for context only.
5. Execution status and test evidence, for observations only.

## Category entry points

| Category | Consolidated entry point | Detailed sources retained where they carry distinct authority or procedures |
|---|---|---|
| Architecture and decisions | [Architecture guide](architecture/auditsphere-architecture-index-agent-context-current.md) · [ADR register](architecture/adr/auditsphere-architecture-index-adr-register-current.md) | [Current architecture](architecture/auditsphere-architecture-current-architecture.md), [code map](architecture/auditsphere-architecture-code-map.md), data model, HTTP/OpenAPI, permissions, and individual ADRs |
| Requirements | [Requirements index](requirements/auditsphere-requirements-index-current.md) | Current requirements, proposed practice-management specification, and preserved v2.2 source |
| Design | [Design index](design/auditsphere-design-index-current.md) | Current visual foundation and progress tracker; proposed UX work remains proposed |
| Operations | [Operations index](operations/auditsphere-operations-index-current.md) | Restore, M365, telemetry, reminders, evidence, and capacity procedures |
| Testing | [Testing strategy and Definition of Done](testing/auditsphere-testing-strategy-definition-of-done-current.md) | `AGENTS.md` controls local versus CI test scope; this file consolidates the test and E2E guidance |
| Execution | [Task board](execution/auditsphere-execution-index-task-board-current.md) · [current-slice handoff](execution/auditsphere-execution-current-slice.md) · [machine-readable status](execution/status.json) | The ignored `.local/autonomous/` continuation ledger remains the active detailed task state |
| Task breakdown and source provenance | [Consolidated task library](auditsphere-task-library-index.md) | One human entry point; immutable task-card and Word-source snapshots remain only because the progress API and source-manifest check consume them. The September snapshot is stale and is not current completion evidence. |
| Evidence | [Evidence index](evidence/auditsphere-evidence-index-current.md) | Approved evidence-methodology records remain separate, unchanged approval artifacts |

## Stable technical references

- [Product brief](requirements/auditsphere-requirements-specification-product-brief-current.md)
- [Current accounting requirements copy](requirements/auditsphere-accounting-module-requirements-current.md)
- [Domain glossary](requirements/auditsphere-requirements-catalog-domain-glossary-current.md)
- [HTTP and OpenAPI boundary](architecture/auditsphere-architecture-http-boundary-and-contract-current.md) · [`contracts/auditsphere-openapi.json`](../contracts/auditsphere-openapi.json)
- [Angular development guide](../src/AuditSphereOps.Ui/auditsphere-angular-development.md)
- [Restore drill](operations/auditsphere-operations-runbook-restore-drill.md)
- [Autonomous continuation rules](architecture/auditsphere-autonomous-completion-remediation-spec.md)

## Consolidation policy

Category entry points are the maintained summaries. Keep a separate detailed file when it is an approved decision, a normative requirement, a proposed source, or an operator procedure with its own scope. Do not copy volatile test counts, run IDs, or changing task status into these summaries; record those in `docs/execution/status.json`. Historical material is referenced at its source commit so consolidation does not rewrite or silently discard its provenance.
