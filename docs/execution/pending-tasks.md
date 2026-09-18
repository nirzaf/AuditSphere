# AuditSphereOps — pending tasks

Paused after the capability-bound PBC chunk transport slice on 2026-09-18. The implementation head is `6e31ab5298f40f1ded4b27007e82b7751708b72d`; hosted CI run [35377533259](https://github.com/nirzaf/AuditSphere/actions/runs/35377533259) passed.

Full context: [authoritative v5.0 specification](../../AuditSphereOps_NET_Codex_Implementation_Specification.md), [execution status](status.json), [current verified slice](current-slice.md), and [implementation checklist](implementation-checklist.md).

## Implementation backlog

- [ ] Complete the remaining dependency-ordered staff and client route catalog, keeping commands scope-checked and UI projections non-authoritative.
- [ ] Add the remaining scoped forms and workflows required by the specification, with command-level authorization and immutable evidence.
- [ ] Complete generated financial-statement artifact creation and rendering, including deterministic package bytes and review visibility.
- [ ] Complete authorized operator recovery screens/actions for blocked, dead-letter, uncertain, and quarantined operations; preserve append-only evidence and recovery fences.
- [ ] Add the trusted final-completion boundary after staged PBC chunks, including durable worker/provider handoff and explicit failure/reconciliation states.
- [ ] Implement live Entra/Graph/SharePoint/Purview adapters only behind the external-effects fence and selected-resource grants; never add tenant-wide scopes.
- [ ] Finish remaining entity foreign keys and data-model integrity gaps identified by the specification and migration preflight rules.
- [ ] Produce signing, protection, methodology, retention, and external-checkpoint evidence without inventing professional conclusions or provider success.
- [ ] Prove production-grade cross-store backup/recovery and RPO/RTO with owner-authorized non-production resources.

## Acceptance and external gates

- [ ] Obtain independent human review evidence for the outstanding merged slices, including the current #14 follow-up, before calling the implementation fully accepted.
- [ ] Obtain owner-authorized Entra OIDC, selected SharePoint resource grants, Purview records-profile/retention behavior, and professional signing/methodology evidence.
- [ ] Run the complete real-tenant acceptance cycle required by specification §47; record observed results and blockers in `status.json`.
- [ ] Re-run local locked restore, build, PostgreSQL-backed tests, migration/readiness checks, and hosted CI after each implementation slice.
- [ ] Do not enable production effects, claim provider delivery, or mark the project complete until the evidence gates above are actually observed.
