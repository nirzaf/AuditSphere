# AuditSphereOps — Markdown Document Naming Policy

**Status: CURRENT.** Authoritative policy governing Markdown (`.md`) filenames across the repository.

---

## 1. Mandatory Naming Scheme

Every Markdown document (with the exception of conventional repository root files) must adhere to the deterministic, semantic naming format:

```text
auditsphere-<area>-<document-type>-<subject>[-<stable-id>][-<status>].md
```

### Formatting Invariants
- **Case:** Strict lowercase ASCII characters only.
- **Delimiter:** Hyphens (`-`) only; no spaces, no underscores, no camelCase.
- **Self-Contained Semantics:** The filename must clearly communicate its business domain, document type, and subject without reference to its parent folder.
- **Globally Unique Basename:** Every Markdown basename across the entire repository must be unique. Two documents in different folders must never share the same basename.
- **Target Length:** Roughly 40–100 characters where practical. Avoid excessive repetition.

---

## 2. Conventional Exceptions

The following root files are preserved conventional exceptions recognized across standard ecosystems:
- `README.md`: Primary public project introduction and entry point.
- `AGENTS.md`: Mandatory engineering instructions and invariants for coding agents.

Nested `README.md` or `AGENTS.md` files are prohibited unless explicitly justified by external tooling conventions.

---

## 3. Controlled Vocabulary

### `<area>` Vocabulary
Use the narrowest matching business capability or operational area:
- `architecture`: Monolith structure, system boundaries, code map, naming policies, ADRs.
- `accounting`: Client accounting, TB/GL intake, chart of accounts, taxonomy mappings, journals.
- `audit`: Audit planning, programs, sampling, confirmations, lead schedules, workpapers, completion.
- `r2r`: Record-to-Report integration framework, contracts, and task breakdown packs.
- `practice`: Firm CRM, engagements, time tracking, billing, firm financial ledger.
- `consolidation`: Multi-entity group consolidation, elimination journals, FX translation.
- `m365`: Microsoft 365 onboarding, Entra ID authentication, SharePoint integration.
- `security`: Authentication, authorization, `RoleGrant` enforcement, session isolation.
- `documents`: Document store, PBC client upload requests, signed document intake.
- `records`: Immutable records archive, retention policies, release manifests.
- `reviews`: Multi-stage independent reviews, sign-offs, review notes.
- `completion`: Audit and accounting period close, lock gates, financial statements release.
- `operations`: Database connection capacity, pooling, background workers, telemetry.
- `execution`: Active slice runbooks, pending work packages, verification ledgers.
- `testing`: E2E strategies, test catalogs, golden fixtures, regression suites.
- `deployment`: Staged deployment rehearsals, migrations, operator handover.
- `requirements`: Master specification, functional user stories, system requirements.
- `evidence`: Methodology approvals, restore rehearsals, verified runtime evidence.

### `<document-type>` Vocabulary
Use the precise document type:
- `specification`: Complete build or system specification.
- `requirements`: Functional requirement backlog or module roadmap.
- `architecture`: Architectural design or authority description.
- `code-map`: Capability-to-file map.
- `contract`: Technical interface or module contract specification.
- `user-stories`: Agile user stories and acceptance criteria backlog.
- `task`: Numbered implementation task card.
- `decision` / `adr`: Architectural or operational decision record.
- `index`: Index or catalog of tasks or packages.
- `tracker`: Traceability matrix or criteria tracking ledger.
- `checklist`: Verification or pre-release checklist.
- `strategy`: Testing, verification, or deployment strategy.
- `runbook` / `guide`: Step-by-step operational or maintenance procedure.
- `catalog`: Catalog of test cases or fixtures.
- `workflow`: Process or lifecycle workflow specification.
- `evidence` / `report`: Signed evidence record or validation report.
- `source`: Preserved historical requirement provenance.
- `handover`: Operational or acceptance handover record.
- `policy`: Binding standard or regulatory rule.

### `<status>` Vocabulary
Status suffixes are applied when documentation authority is materially significant:
- `current`: Actively implemented, authoritative reality.
- `approved`: Formally approved human decision binding implementation.
- `proposed`: Backlog or design not yet implemented; requires approved change.
- `historical`: Preserved requirement source text; not implementation authority.
- `superseded`: Replaced by a subsequent decision; kept for audit trail only.

---

## 4. Stable Identifiers

Stable identifiers (e.g. task IDs `T001`–`T075`, module IDs `M20`–`M26`, ADR IDs, approval codes `STE-METH-APP-001`, story IDs `AS-AUD-028`) must remain in the filename:
- Task cards: `auditsphere-<area>-task-<stable-id>-<slug>.md`
  - Example: `auditsphere-r2r-task-t001-baseline-current-state-inventory.md`
  - Example: `auditsphere-audit-task-t060-opening-balance-verification.md`
- Module contracts: `auditsphere-r2r-module-<id>-<slug>-contract.md`
  - Example: `auditsphere-r2r-module-20-accounting-workspace-contract.md`
- Evidence approvals: `auditsphere-evidence-approval-<subject>-<stable-id>-approved.md`
  - Example: `auditsphere-evidence-approval-methodology-ste-meth-app-001-approved.md`

---

## 5. Prohibited Naming Patterns

The following patterns are strictly forbidden:
1. **Generic or Vague Names:** `new.md`, `final.md`, `misc.md`, `notes.md`, `document.md`, `file.md`, `001.md`, `task1.md`, `phase2.md`.
2. **Ambiguous Abbreviations:** `acct.md`, `spec.md`, `doc.md`, `cfg.md`.
3. **Date-Based Primary Names:** `requirements-2026-09-25.md` or `report-20260925.md` (use Git history for revisions; use ISO date suffixes only for immutable evidence snapshots).
4. **Version Chains:** `spec-v1.md`, `spec-v2.md`, `spec-v3-final.md`.
5. **Folder-Dependent Duplicate Names:** Having multiple files named `index.md`, `guide.md`, or `overview.md` across different subdirectories. Every basename must be globally unique.
6. **Underscores or Mixed Case:** `CODE_MAP.md`, `Task_Breakdown.md`, `user_stories.md`.
