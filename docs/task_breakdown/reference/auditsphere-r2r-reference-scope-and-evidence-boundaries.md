# Scope and evidence boundaries







[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)







**Status:** HISTORICAL_REFERENCE



**Authority:** Preserved contract excerpts and design research. Does not override `AGENTS.md` or `docs/architecture/auditsphere-architecture-current-architecture.md`. Current implementation uses static capability services and modular monolith architecture (see [`auditsphere-r2r-reference-baseline-architecture-and-adrs.md` §2.3.1](auditsphere-r2r-reference-baseline-architecture-and-adrs.md#section-2-3-1)).







**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.







<!-- SOURCE-LINES: 35-77 -->



## 1. Scope and source precedence







<a id="section-1-1"></a>



### 1.1 In scope







Build the **production R2R subsystem** for the seven named modules. Account mapping is owned by Module 21 in this blueprint, consumes Module 20's approved chart/taxonomy identities, and supplies Module 24. This is an explicit ownership assignment; the earlier tracker associated VP-037 with Module 20. It does not introduce a second mapping engine.







The production application remains an **import-first accounting preparation and reporting platform**: receive client source books, validate them, record proposed corrections and reporting adjustments, reconcile, produce statements, assemble reviewed packages, and consolidate eligible component results. It does not become the source operational general ledger merely because it imports GL data.







Modules outside 20–26 supply defined contracts: client/engagement/acceptance, identity and grants, documents/PBC/evidence, audit findings/review, completion/release, archive, and firm administration. They are dependencies, not seven additional implementations hidden inside accounting.







Consolidation is a configurable capability. A standalone entity must complete R2R without a group. Once a group-reporting profile is selected, the relevant component, rate, elimination and review gates are mandatory. Optional capability does not mean optional controls within an enabled capability.







<a id="section-1-2"></a>



### 1.2 Explicit exclusions and preserved boundaries







No AI, semantic search, native mobile application, online payments, eSignature provider, tax-return/payroll service, recurring business tasks, automatic reminders, automatic journal approval, automatic intercompany matching, bank-feed connector, or non-M365 business integration. No Microsoft Purview integration, certification project or bespoke encryption-management project. Preserve ordinary authentication, authorization, platform protection, SHA-256 identities and immutable evidence.







Imported salary/tax accounts and professionally prepared financial-statement tax balances are not payroll execution or tax-return preparation. Preserve their data. Do not fabricate tax balances; unsupported accounting treatment remains an explicit policy/input dependency.







Keep three ledgers of responsibility distinct:







| Boundary | Owner | Must never be used as a substitute |



|---|---|---|



| Firm's own commercial books | Existing Practice/firm-ledger services | Client TB, client reporting journals or group elimination ledger |



| Client source and reporting adjustments | Modules 20–25 | Posting back to a client's external ERP or bank |



| Group-only calculations and eliminations | Module 26 | Mutating a component package or firm invoice/ledger |







The supplied tracker describes browser simulation. Retain its **business requirements**, but replace localStorage, synthetic persona authority and browser-only artifact persistence with production identity, PostgreSQL transactions and approved storage. A successful prototype test is not a production test result. [F1](auditsphere-r2r-reference-standards-and-source-register.md#source-f1)[R2](auditsphere-r2r-reference-standards-and-source-register.md#source-r2)







<a id="section-1-3"></a>



### 1.3 Approval and uncertainty boundaries







An implementation plan cannot establish defect-free or “100% interoperable” software by declaration. Here, full acceptance means **every approved requirement has executable evidence at one identified build**, all required cross-module/rework cases pass, and no unresolved blocking dependency is hidden. The final gate is measurable; the guarantee is not assumed.







The firm's accounting/methodology owner must approve the applicable framework, edition, jurisdictional overlay, accounting treatments, disclosures, materiality policy and golden financial fixtures. Software checks must not choose these professional conclusions. This document does not assert that all clients use full IFRS or that every industry/accounting method is supported.







<a id="section-1-4"></a>



### 1.4 Evidence labels







- **EXISTING:** symbol or behavior directly inspected in the pinned source.



- **EXTEND:** retain that identity/table/service and add the stated contract.



- **NEW:** proposed only; search the current repository for an equivalent before creation.



- **DECISION:** architecture or methodology approval required; a coding agent cannot silently decide.







Resolve instructions in this order: approved scope decision for this implementation → current repository instructions and preserved invariants → exact business criteria → approved standards/policy edition → this proposed design. A conflict stops the affected slice for a recorded decision; it never authorizes deleting evidence or bypassing a guard.
