# Audit Workflow Consolidation Clarifications

[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Original Audit Workflow Source](../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md) · [Traceability](../tracking/auditsphere-audit-tracker-workflow-traceability.md)

**Prepared:** 24 September 2026  
**Document status:** Architectural and product-scope clarification for the consolidated R2R and Audit Workflow task breakdown.

---

## 1. Purpose and Relationship to Original Sources

This reference document records architectural, scoping, and boundary determinations made when integrating the 28-story, 165-procedure audit workflow backlog ([auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md](../source/auditsphere-audit-source-workflow-gap-closure-user-stories-historical.md)) into the canonical AuditSphere R2R task pack ([auditsphere-r2r-index-task-breakdown.md](../auditsphere-r2r-index-task-breakdown.md)).

The source document in `source/` is preserved byte-for-byte as historical requirement provenance. The clarifications below govern implementation interpretation without altering original requirement text or procedure IDs.

---

## 2. Complementary R2R and Audit-Plan Boundary

This backlog is the professional audit-execution contract for the 20 audit sections. It is complementary to, and does not replace, the R2R Modules 20–26 task breakdown. R2R owns the accounting/reporting engine (context, TB/GL, journals, reconciliations, statements, packages and consolidation); this backlog owns audit programs, populations, sampling, evidence, findings, audit-area conclusions, review and completion.

Accounting objects are audit inputs, not substitutes for audit work:
- An accounting reconciliation review is not an auditor's reconciliation testing.
- An accounting package approval is not audit acceptance.
- An accounting journal decision is not an audit finding conclusion.
- Audit schedules and populations must bind to the exact accepted R2R source identities (`AcceptedSourceRevisionId`, dataset digest, reporting context, period, book, currency and revision) where applicable.
- Do not create duplicate audit TB, chart-of-accounts or reporting-period masters.

The current 165-procedure programme is a **single-entity audit programme**. R2R Module 26 remains supported for group consolidation, but this backlog does not define group-audit methodology, group scoping, component-audit coordination or group opinion requirements. Those require a separate approved group-audit backlog.

---

## 3. Product-Scope Reconciliation and Provider Fences

The current STEAuditSphere product scope supersedes provider-specific requirements in this backlog:

- **No Purview integration:** do not require Purview labels, protection, readback or provider acceptance. Logical archive, holds, retention metadata, exact bytes and SHA-256 identities remain in scope.
- **No eSignature provider integration:** do not require DocuSign, Adobe Sign, cryptographic signing workflow or provider live acceptance. Management representations and signed artifacts may be uploaded or linked as exact documents, with signatory/date evidence, SHA-256, human review and release-manifest decisions retained. Uploaded signed evidence is not proof that a signing provider was integrated or that live provider acceptance passed.
- **ReleaseService gate evaluation:** Extend existing completion projections and ReleaseService gate evaluation. Reuse exact snapshots, approvals, uploaded signed-document evidence, release manifests, records protection and recovery safeguards; do not build a second release path or an eSignature/Purview provider integration.

These are requirement supersessions, not test waivers. A live provider gate may remain `BLOCKED_EXTERNAL`; local simulation and uploaded evidence must not be presented as provider acceptance.

---

## 4. Payroll and Tax Audit Boundaries

AS-AUD-015 Payroll Audit and AS-AUD-019 Tax & Statutory Liabilities Audit are **audit workpaper capabilities only**:
- They may import and reconcile client-provided payroll/tax schedules, inspect evidence, select items, perform authorized recalculations, test payments and review disclosures.
- They must not implement payroll runs, payslips, employee master/HR operations, salary disbursement, tax preparation, tax filing, tax-authority submission or a universal statutory tax engine.
- Missing jurisdiction, rates, legal rules or source evidence remains an explicit methodology/input dependency and is never defaulted.

---

## 5. Professional and Human Decision Boundaries

The software provides calculations, exceptions, lineage, immutable evidence, and workflow gating. It does not replace professional audit judgment:
- Materiality benchmarks, going-concern conclusions, final audit opinion selection, and EQCR concurrence must be recorded as explicit human decisions by authorized practitioners.
- The system computes differences, evaluates risk indicators, and blocks release when required conditions are unmet; it never fabricates an automated audit conclusion.
