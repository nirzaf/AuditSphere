# AuditSphereOps — pending tasks

Paused after the audit planning scope integrity, database immutability, and in-command authorization slice on 2026-09-19. Advancing past base `8a0f035` with 23 applied migrations and all 152 tests passing on PostgreSQL 18.6 with 0 skipped.

Full context: [authoritative v5.0 specification](../../AuditSphereOps_NET_Codex_Implementation_Specification.md), [execution status](status.json), [current verified slice](current-slice.md), and [implementation checklist](implementation-checklist.md).

## Implementation backlog

- [x] Complete generated financial-statement artifact creation and rendering, including deterministic package bytes and review visibility.
- [x] Complete authorized operator recovery screens/actions for blocked, dead-letter, uncertain, and quarantined operations; preserve append-only evidence and recovery fences.
- [x] Add the trusted final-completion boundary after staged PBC chunks, including durable worker/provider handoff and explicit failure/reconciliation states.
- [x] Complete the remaining dependency-ordered staff and client route catalog, keeping commands scope-checked and UI projections non-authoritative.
- [x] Add the remaining scoped forms and workflows required by the specification, with command-level authorization and immutable evidence.
- [ ] Implement live Entra/Graph/SharePoint/Purview adapters only behind the external-effects fence and selected-resource grants; never add tenant-wide scopes.
- [x] Finish remaining entity foreign keys and data-model integrity gaps identified by the specification and migration preflight rules. (Audit-planning domain closed and locally verified in the uncommitted working tree on base `8a0f035`: 152/152 tests, composite scope FKs, every foreign key `ON DELETE RESTRICT`, append-only triggers on frozen planning evidence, in-command authorization. Remaining modules still need the same sweep.)
- [ ] Produce signing, protection, methodology, retention, and external-checkpoint evidence without inventing professional conclusions or provider success.
- [ ] Prove production-grade cross-store backup/recovery and RPO/RTO with owner-authorized non-production resources.

## Acceptance and external gates

- [ ] Obtain independent human review evidence for the outstanding merged slices, including the current #14 follow-up, before calling the implementation fully accepted.
- [ ] Obtain owner-authorized Entra OIDC, selected SharePoint resource grants, Purview records-profile/retention behavior, and professional signing/methodology evidence.
- [ ] Run the complete real-tenant acceptance cycle required by specification §47; record observed results and blockers in `status.json`.
- [ ] Re-run local locked restore, build, PostgreSQL-backed tests, migration/readiness checks, and hosted CI after each implementation slice.
- [ ] Do not enable production effects, claim provider delivery, or mark the project complete until the evidence gates above are actually observed.
