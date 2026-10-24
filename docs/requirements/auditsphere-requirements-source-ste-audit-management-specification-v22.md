# AuditSphere — Preserved STE Audit Management Specification v2.2

| Attribute | Value |
| --- | --- |
| Status of this file | Historical source snapshot with a new provenance wrapper |
| Source version | 2.2 — not rewritten as 2.3 |
| Original attachment | Pasted markdown(5).md |
| Original body bytes | 71,130 |
| Original body SHA-256 | `678d6edea7a82a390a1a6cd78c7961814682b1e52dedc1986fd1877459b1f48b` |
| Package prepared | 24 October 2026 |

> **Read this status before the source body.** The text below is the full supplied v2.2 Markdown, unchanged, including its formatting escapes, historical `CURRENT` label, earlier code claims and original relative references. Those labels describe the supplied snapshot; they do not verify today's implementation. Its source-reported approved ADR overlays remain controlling until an authorized change is adopted. The [complete proposed revision 2.3](auditsphere-requirements-specification-complete-practice-management-proposed.md) records proposed improvements separately and does not silently amend this source.

The six screenshot filenames establish the requested output naming. This body is recovered from the actual supplied attachment; the other five documents are review-derived candidates rather than recovered repository originals. Original references outside this six-file package are retained as source history and are not represented as available package files.

The SHA-256 above applies only to the exact original body following the marker. The wrapper and marker are new. No v2.3 correction has been applied to that body.

<a id="preserved-source-body"></a>

<!-- BEGIN EXACT SUPPLIED V2.2 SOURCE BODY -->
**# STE Audit Management Tool**



**## Functional Requirements & End-to-End System Workflow Specification**



\| Attribute | Specification Details |

\| :--- | :--- |

\| **\*\*Document Version\*\*** | 2.2 |

\| **\*\*Source Baseline\*\*** | Audit Management Tool Specification v2.2 (Google Drive source). v2.2 keeps the five modules, the eleven canonical states and all v2.1 requirements, and adds the seven-phase workflow layer (Workflow Experience Addendum, §4.1.3 Enhanced Dual-Key Acceptance, §5.3, §6 and §7). Delivery status of the v2.2 additions is recorded in the [v2.2 workflow-experience delta]\(../execution/auditsphere-execution-tracker-ste-v21-gap-verification-current.md#v22-workflow-experience-delta), not here. |

\| **\*\*Document Status\*\*** | \`CURRENT\` |

\| **\*\*Target Focus\*\*** | Business Logic, Functional Requirements, Task Sequences & Module Workflows |

\| **\*\*Auditing Standards Context\*\*** | International Standards on Auditing (ISA) & International Financial Reporting Standards (IFRS) |

\| **\*\*Primary Currency\*\*** | Qatari Riyal (QAR) |

\| **\*\*System Architecture\*\*** | Modular Monolith (ASP.NET Core / Angular 22 canonical UI / PostgreSQL). Original wording: \~\~Blazor Interactive Server\~\~, superseded by ADR-0002. The former Blazor host was retired and merged as \`e7ebe27c\`; historical parity and rollback were not accepted. |



\---



**## Approved deviations**



The entries below are operative decision overlays, and the source requirements that follow are preserved for provenance. The approved or accepted decision records here control current interpretation where source prose differs; a decision does not prove implementation, live-provider acceptance or qualified professional treatment. Do not rewrite an executed client contract or infer a professional decision from this specification.



\- **\*\*ADR-0002 (Angular canonical UI):\*\*** Angular 22 is the canonical user interface. The Blazor rollback host was retired by owner decision and merged as \`e7ebe27c\`; retirement does not mean migration parity or rollback acceptance was established. Affects the header architecture row.

\- **\*\*ADR-0003 (Microsoft sign-in for the client portal):\*\*** no temporary passwords are emailed or stored; the first-sign-in requirement of the Microsoft identity path applies. For newly converted clients, portal access also requires both keys, activation and the paid advance. Live tenant acceptance remains separate. Affects the §3.1 portal box and the §4.1.5 portal bullets.

\- **\*\*ADR-0004 (lifecycle as a derived projection):\*\*** the §5 stages are computed from gate records and never stored or set directly. Affects §5.

\- **\*\*ADR-0005 and ADR-0016 (visual appearance and certificate signing):\*\*** the PNG signature and seal remain the visible appearance. A firm-held X.509 certificate can sign the final report; when certificate signing is required, missing or invalid certificate material fails closed with no image-only fallback. eSignature providers and Purview remain out of scope. The firm's real-certificate and legal acceptance, and whether production must require certificate signing, remain separate gates. Affects §4.1.4 and §§4.4.1–4.4.2.

\- **\*\*ADR-0006 (one SharePoint site per client; Full Control for assigned staff):\*\*** the site and staff-access decision does not establish archive immutability or provider read-only protection. The archive-copy rule is controlled by ADR-0009 below.

\- **\*\*ADR-0007 (approved 50/50 fee milestones and draft-only automation):\*\*** the gated engagement letter creates the reviewed fee agreement and its advance and balance milestones. Optional automation is off by default and creates invoice drafts only: the advance draft is eligible after the letter; the balance draft is eligible only after the advance is paid and the linked financial package is released. A draft is not approved, posted, sent or collected. A draft-report date alone is not the balance-invoice trigger. Preserve the terms and evidence of already accepted or executed contracts. Affects §§3.1, 3.4, 4.1.2, 4.1.5 and 4.4.2.

\- **\*\*ADR-0009 (archive authority, from spike SPK-01):\*\*** the append-only release and archive store is authoritative for issued evidence. A separate read-only SharePoint archive library is a secondary copy. Its state is \`OBSERVED\` only after both exact file hashes and expected grants are read back; until then it remains \`BLOCKED_EXTERNAL\`. A local freeze does not protect the SharePoint copy, and the UI or regulator export must not claim provider immutability. Affects §4.4.3.

\- **\*\*ADR-0010 (end-of-service accrual, from STE-NXT-009):\*\*** end-of-service benefits are accrued as a provision entered by a person, not expensed only when paid. Affects §3.5.

\- **\*\*ADR-0011 and ADR-0017 (materiality without normalization):\*\*** the PBT benchmark is mapped balances excluding tax; no normalization adjustments are applied. Affects §4.2.4.

\- **\*\*ADR-0012 (unconditional advance activation gate; ACCEPTED):\*\*** activation into \`PORTAL_ACTIVE_PLANNING\` requires a linked fee agreement and its 50% \`ADVANCE\` milestone in \`PAID\` state, representing full payment and allocation. No configuration bypass is permitted. An invoice, partial payment or missing agreement is insufficient. This does not retroactively change an executed contract. Affects §§4.1.5 and 5.1.

\- **\*\*ADR-0017 (STE v2.2 delegated product decisions):\*\*** WhatsApp is a lead-channel label only; nothing is ingested from or sent through it, and proposals are dispatched by email. Acceptance records \`EMAIL\`, \`SIGNED_LETTER\`, \`IN_PERSON\` or \`PHONE\` (required for acceptance, optional for decline); the value labels evidence and sends nothing. Add continuance questions RV-031 (legal notice, claim or demand received; adverse when yes) and RV-032 (prior-year professional fees settled; adverse when no), preserving existing questions and historical answers. The firm week is Sunday–Thursday with Friday/Saturday as weekend and Sunday–Saturday allocation weeks; public holidays are recorded as unavailability, with no inferred holiday table, and statutory-relative milestone dates remain the rule (the dates in the source are examples). The staffed Engagement Partner is required for SRM clearance, opinion, report signing, signed-representation verification and bundle assembly. Planning approval covers current materiality and risk routing; a recorded critical-estimate flag bands Red; high inherent risk is likelihood 3 with magnitude at least 2; Amber work is performed by Senior Auditor or above and reviewed by an Audit Manager. Sampling versus full-population testing is documented senior judgment; no sample is universally required. Affects §§3.1–3.5, 4.1–4.3, 5.1–5.2 and 6.3.

\- **\*\*ADR-0018 (practice reporting bases):\*\*** approved non-billable/no-charge time counts in actual hours and cost but has no charge-out value; standard value, realization and contract contribution use approved billable time only, and missing rate/currency makes those figures unavailable. Utilization is approved billable minutes divided by capacity net of recorded unavailability; show all approved hours separately. Age every posted firm invoice (advance, final, time or other); do not include client-bookkeeping invoices, and refuse a report for an invoice bound to multiple milestones. Monthly P&L uses one row per accounting month and the same month one year earlier as comparator. The dashboard names currently staffed users at the level owning each step but grants no permissions. Apply Senior/Supervisor/Junior/Associate rate aliases wherever rates are looked up. Staff-level performance measures and visibility, facility/benefit expense taxonomy, broader fee/partial-payment policy and the professional/legal items listed in ADR-0018 remain undecided. Affects §§3.5 and 4.5.1–4.5.2.



**## Executive Table of Contents**



\- [Workflow Experience Addendum — Version 2.2]\(#workflow-experience-addendum--version-22)

&#x20; \- [Visual overview — seven user-facing phases]\(#visual-overview--seven-user-facing-phases)

&#x20; \- [Two-layer authority model]\(#two-layer-authority-model)

\- [1. Requirement Gathering & Operational Context]\(#1-requirement-gathering--operational-context)

&#x20; \- [1.1 Business Objectives & Problem Statement]\(#11-business-objectives--problem-statement)

&#x20; \- [1.2 User Personas & Responsibility Matrix]\(#12-user-personas--responsibility-matrix)

\- [2. Project Modules Connectivity & Architecture]\(#2-project-modules-connectivity--architecture)

\- [3. End-to-End Project Task Flows & Sequence Maps]\(#3-end-to-end-project-task-flows--sequence-maps)

&#x20; \- [3.1 Flow 1: Lead Ingestion to Dual-Key Client Onboarding]\(#31-flow-1-lead-ingestion-to-dual-key-client-onboarding)

&#x20; \- [3.2 Flow 2: Planning, Resource Scheduling & Materiality Formulation]\(#32-flow-2-planning-resource-scheduling--materiality-formulation)

&#x20; \- [3.3 Flow 3: Technical Fieldwork Execution & Multi-Tier Review Matrix]\(#33-flow-3-technical-fieldwork-execution--multi-tier-review-matrix)

&#x20; \- [3.4 Flow 4: Reporting, 5-Part Deliverable Release & Compliance Archive]\(#34-flow-4-reporting-5-part-deliverable-release--compliance-archive)

&#x20; \- [3.5 Flow 5: Practice Management, Time Realization & Firm Bookkeeping]\(#35-flow-5-practice-management-time-realization--firm-bookkeeping)

\- [4. Module-by-Module Functional Requirements]\(#4-module-by-module-functional-requirements)

&#x20; \- [4.1 Module 1: Commercial & CRM Pipeline]\(#41-module-1-commercial--crm-pipeline)

&#x20; \- [4.2 Module 2: Administration, Governance & Planning]\(#42-module-2-administration-governance--planning)

&#x20; \- [4.3 Module 3: Technical Execution & Audit Fieldwork]\(#43-module-3-technical-execution--audit-fieldwork)

&#x20; \- [4.4 Module 4: Reporting & Final Deliverables]\(#44-module-4-reporting--final-deliverables)

&#x20; \- [4.5 Module 5: Practice Analytics & Internal Bookkeeping]\(#45-module-5-practice-analytics--internal-bookkeeping)

\- [5. System State Machine & Lifecycle Transitions]\(#5-system-state-machine--lifecycle-transitions)

&#x20; \- [5.1 State Machine Transition Matrix]\(#51-state-machine-transition-matrix)

&#x20; \- [5.2 State Machine Visual Workflow]\(#52-state-machine-visual-workflow)

&#x20; \- [5.3 Display Phases and Canonical States]\(#53-display-phases-and-canonical-states)

\- [6. Simplified Seven-Phase Engagement Workflow — New Requirements]\(#6-simplified-seven-phase-engagement-workflow--new-requirements)

&#x20; \- [6.1 Purpose and mapping]\(#61-purpose-and-mapping)

&#x20; \- [6.2 Engagement dashboard]\(#62-engagement-dashboard)

&#x20; \- [6.3 Phase detail requirements]\(#63-phase-detail-requirements)

&#x20; \- [6.4 Reusable practice templates]\(#64-reusable-practice-templates)

&#x20; \- [6.5 Recurring audit engagements]\(#65-recurring-audit-engagements)

&#x20; \- [6.6 Safe workflow automation]\(#66-safe-workflow-automation)

&#x20; \- [6.7 User role, security and progress rules]\(#67-user-role-security-and-progress-rules)

&#x20; \- [6.8 Acceptance criteria]\(#68-acceptance-criteria)

\- [7. Architectural Non-Regression Requirements — New]\(#7-architectural-non-regression-requirements--new)



\---



**# Workflow Experience Addendum — Version 2.2**



The product SHALL present a Karbon-inspired, easy-to-follow seven-phase engagement workflow while preserving all five functional modules and the eleven mandatory backend lifecycle states in Section 5. The display phases are a projection of audited business records; they are NOT a second state machine. Completing a UI task never substitutes for a verified acceptance, finance, audit review, Partner, release or archive command.



Seven visible phases: 1. Client Onboarding; 2. Audit Planning; 3. Audit Fieldwork; 4. Manager Review; 5. Partner Sign-off; 6. Report & Delivery; 7. Archive & Close.



Design principles: show current phase, responsible role, next action, due date, completion progress and exact blockers; expand advanced checks inside a phase; display Waiting for Client, Under Review, Rework Required and Blocked clearly. Keep internal lifecycle codes away from ordinary navigation. The existing detailed requirements remain authoritative.



**## Visual overview — seven user-facing phases**



The diagram below is a **\*\*simplified user-facing projection\*\***, not a new backend state machine. In particular, Dual-Key Acceptance is a subsection of Client Onboarding, and the advance-payment gate remains distinct.



\`\`\`mermaid

flowchart LR

&#x20;   ON["1. Client Onboarding"] --> PLAN["2. Audit Planning"]

&#x20;   PLAN --> FIELD["3. Audit Fieldwork"]

&#x20;   FIELD --> REVIEW["4. Manager Review"]

&#x20;   REVIEW --> DEC{"Review accepted?"}

&#x20;   DEC -->|"No: documented rework"| FIELD

&#x20;   DEC -->|"Yes"| PARTNER["5. Partner Sign-off"]

&#x20;   PARTNER --> REPORT["6. Report and Delivery"]

&#x20;   REPORT --> ARCHIVE["7. Archive and Close"]

&#x20;   classDef decision fill:#fff1d6,stroke:#b88b2c,color:#5d4316

&#x20;   classDef archived fill:#e0f4e5,stroke:#21844a,color:#165d34

&#x20;   class DEC decision

&#x20;   class ARCHIVE archived

\`\`\`



**## Two-layer authority model**



\`\`\`mermaid

flowchart TB

&#x20;   subgraph UI["Presentation and orchestration layer"]

&#x20;       PHASE["Seven visible phases"] --> TASK["Tasks, templates, work queues and reminders"]

&#x20;   end

&#x20;   subgraph CORE["Authoritative business and compliance layer"]

&#x20;       EVIDENCE["Versioned business evidence"] --> PROJECTION["Eleven-state derived lifecycle"]

&#x20;       COMMAND["Authorized domain commands and gates"] --> EVIDENCE

&#x20;   end

&#x20;   TASK -->|"Request authorized action"| COMMAND

&#x20;   PROJECTION -->|"Project current phase, blockers and permitted next action"| PHASE

&#x20;   TASK -.->|"May not directly set stage or forge approval"| PROJECTION

\`\`\`



\---



**# 1. Requirement Gathering & Operational Context**



**## 1.1 Business Objectives & Problem Statement**



\* **\*\*Elimination of Per-File Licensing Penalties:\*\*** Existing commercial platforms enforce rigid licensing tiers (e.g., limits of 130 client files) that charge steep incremental fees for additional files. The in-house platform must support an unlimited number of client entities, historical engagements, and working papers with zero subscription penalties.

\* **\*\*Unified Engagement Lifecycle:\*\*** Centralize commercial sales, administrative governance, compliance clearance, audit fieldwork execution, multi-tier quality reviews, client deliverable generation, and internal firm practice management into a single, cohesive workflow.

\* **\*\*Standardized Auditing Governance:\*\*** Standardize firm-wide execution in strict alignment with International Standards on Auditing (ISA), including:

&#x20; \* **\*\*ISA 210:\*\*** Agreeing the Terms of Audit Engagements (Engagement Letters).

&#x20; \* **\*\*ISA 220 & ISQC 1:\*\*** Quality Control for an Audit of Financial Statements (Client Acceptance & Continuance).

&#x20; \* **\*\*ISA 230:\*\*** Audit Documentation (60-day assembly and regulatory locking).

&#x20; \* **\*\*ISA 320:\*\*** Materiality in Planning and Performing an Audit.

&#x20; \* **\*\*ISA 505:\*\*** External Confirmations.

&#x20; \* **\*\*ISA 570:\*\*** Going Concern.

&#x20; \* **\*\*ISA 700 & 705:\*\*** Forming an Opinion and Reporting on Financial Statements.



**### Client accounting service boundary**



AuditSphere continues to support import-first accounting preparation, audit, and consolidation. For a specifically authorized client accounting service, the product may additionally support native client-owned double-entry bookkeeping, client sales and purchase invoices and credit notes, GL/TB, and financial statements. VAT/tax and other ancillary modules are optional and independently gated; they do not block the core bookkeeping path. This does not authorize inventory, procurement operations, payroll execution, payment initiation, external tax filing, or changes to the firm's ledger or group-consolidation boundaries. Existing clients retain their current external-source behavior unless an explicit, reviewed cutover changes it. Firm invoices remain firm revenue; client invoices and proposed audit adjustments must never be represented as firm ledger activity or client-book postings respectively. Client isolation, balanced postings, review, and immutable history are mandatory across all modes. These new capabilities are delivered incrementally and are not considered available until their native API/UI paths and controls are verified.



\---



**## 1.2 User Personas & Responsibility Matrix**



\| User Role | Target Persona | Functional Scope & Core Responsibilities | Key Access Boundary |

\| :--- | :--- | :--- | :--- |

\| **\*\*PREPARER\*\*** | Audit Associate / Junior Auditor | • Executes assigned financial statement line item (FSLI) audit test procedures.\<br/>• Uploads digital working papers and inputs physical binder index codes (\`X-1, Box 3\`).\<br/>• Submits completed testing packages for managerial review.\<br/>• Logs daily operational hours against assigned engagement tasks. | Limited to assigned workprograms; no sign-off or external communication rights. |

\| **\*\*REVIEWER\*\*** | Audit Senior / Audit Manager | • Verifies substantive testing and recalculated schedules.\<br/>• Issues inline review notes and initiates the rework loop for incomplete tests.\<br/>• Determines sampling parameters and calculates engagement materiality.\<br/>• Prepares the Summary Review Memorandum (SRM) for the partner.\<br/>• Tracks engagement budgets, team hours, and delivery milestones. | Engagement-wide review; can return tests for rework; cannot sign final audit opinion. |

\| **\*\*APPROVER\*\*** | Engagement Partner | • Evaluates and signs off on the Dual-Key Acceptance Gate (AML/KYC).\<br/>• Authorizes commercial proposals and executes Engagement Letters.\<br/>• Clears high-risk (Red) audit areas and formally signs off on the SRM.\<br/>• Selects the final Audit Opinion, applies digital signatures and firm seals.\<br/>• Authorizes final deliverable bundles and enforces regulatory file locks. | Full firm and engagement sign-off authority; final gatekeeper for releases. |

\| **\*\*CLIENT\*\*** | Client Coordinator / CFO / MD | • Accesses an isolated, tokenized external workspace (PBC Portal).\<br/>• Views requested audit documentation with real-time review status badges.\<br/>• Uploads requested financial schedules, trial balances, and voucher evidence.\<br/>• Receives invoices, receipts, holding letters, and final deliverables.\<br/>• Access freezes automatically upon engagement sign-off. | Isolated tenant portal; read/upload only; access terminates on engagement closure. |



\---



**# 2. Project Modules Connectivity & Architecture**



The five core functional modules communicate across strict data boundaries. An engagement progresses sequentially through gates, ensuring administrative and compliance requirements are met before field testing begins.



\`\`\`mermaid

flowchart TD

&#x20;   subgraph M1["Module 1: Commercial & CRM Pipeline"]

&#x20;       direction TB

&#x20;       M1_Lead["Lead Ingestion (Multi-channel)"]

&#x20;       M1_Prop["Commercial Proposal (Quote / RFP)"]

&#x20;       M1_Gate{"Dual-Key Gate (Client + Partner)"}

&#x20;       M1_EL["Engagement Letter (ISA 210)"]

&#x20;       M1_Adv["50% Advance Invoice"]

&#x20;       M1_Lead --> M1_Prop --> M1_Gate

&#x20;       M1_Gate -->|"Both Approved"| M1_EL --> M1_Adv

&#x20;   end



&#x20;   subgraph M2["Module 2: Administration, Governance & Planning"]

&#x20;       direction TB

&#x20;       M2_Gov["Acceptance & Continuance (ISA 220)"]

&#x20;       M2_Tax["Taxonomy Provisioning (5-Folder Tree)"]

&#x20;       M2_Sched["Resource Scheduling & Milestones"]

&#x20;       M2_Mat["3-Tier Materiality Engine (PM, TE, SAD)"]

&#x20;       M2_Gov --> M2_Tax --> M2_Sched --> M2_Mat

&#x20;   end



&#x20;   subgraph M3["Module 3: Technical Execution & Fieldwork"]

&#x20;       direction TB

&#x20;       M3_TB["TB Ingestion & Auto-Mapping"]

&#x20;       M3_Dash["Split Dashboard (P&L & B/S)"]

&#x20;       M3_WP["Workprograms & Hybrid Evidence"]

&#x20;       M3_Conf["Confirmations Gatekeeper (ISA 505)"]

&#x20;       M3_Review["Three-Tier Review & SRM (ISA 220)"]

&#x20;       M3_TB --> M3_Dash --> M3_WP --> M3_Conf --> M3_Review

&#x20;   end



&#x20;   subgraph M4["Module 4: Reporting, Deliverables & File Archive"]

&#x20;       direction TB

&#x20;       M4_Op["4-Way Opinion Dropdown (ISA 700/705)"]

&#x20;       M4_Sig["Digital Signature & Official Firm Seal"]

&#x20;       M4_Bundle["5-Part Deliverable Release Bundle"]

&#x20;       M4_Inv["50% Final Fee Invoice"]

&#x20;       M4_Lock["60-Day Compliance Lock (ISA 230)"]

&#x20;       M4_Op --> M4_Sig --> M4_Bundle --> M4_Inv --> M4_Lock

&#x20;   end



&#x20;   subgraph M5["Module 5: Practice Management & Internal Bookkeeping"]

&#x20;       direction TB

&#x20;       M5_Time["Daily Time Tracking & Role Charge-Out Rates"]

&#x20;       M5_Real["Engagement Profitability & Realization"]

&#x20;       M5_Ledger["Internal Practice Ledger & Operating Expenses"]

&#x20;       M5_Report["Firm Trial Balance, P&L & AR Aging"]

&#x20;       M5_Time --> M5_Real

&#x20;       M5_Ledger --> M5_Report

&#x20;   end



&#x20;   M1_Adv ==>|"Handshake 1: Risk Clearance & 50% Deposit Paid"| M2_Gov

&#x20;   M2_Mat ==>|"Handshake 2: Planning Approved & TB Ingested"| M3_TB

&#x20;   M3_Review ==>|"Handshake 3: Partner Cleared SRM & Confirmations"| M4_Op

&#x20;   M3_WP -.->|"Labor Hours Actuals"| M5_Time

&#x20;   M4_Inv -.->|"50% Final Fee Note"| M5_Ledger

&#x20;   M1_Adv -.->|"50% Advance Receipt"| M5_Ledger

\`\`\`



\---



**# 3. End-to-End Project Task Flows & Sequence Maps**



**## 3.1 Flow 1: Lead Ingestion to Dual-Key Client Onboarding**



The onboarding path enforces an automated state machine. An Engagement Letter cannot be generated until commercial terms are accepted by the client and risk clearance is formally approved by the Engagement Partner.



\`\`\`mermaid

flowchart TD

&#x20;   Start(["Client Contact Initiated\<br/>(Phone / WhatsApp / Email / Referral)"]) --> Profiling["Entity & Contact Profiling\<br/>(Group Structure, Tax ID, Signatories)"]

&#x20;   Profiling --> Scope["Generate Commercial Scope"]

&#x20;   Scope --> Quote["Brief Quotation\<br/>(1–2 Pages, 50/50 Fee Terms)"]

&#x20;   Scope --> Proposal["Comprehensive Technical Proposal\<br/>(Team CVs, Methodology, Credentials)"]

&#x20;   Quote --> Submit["Submit Proposal / Quote to Client"]

&#x20;   Proposal --> Submit



&#x20;   Submit --> GateEval{"Dual-Key Acceptance Gatekeeper"}



&#x20;   subgraph DualKey["Dual-Key Authorization (Parallel Requirements)"]

&#x20;       Key1["Key 1: Client Commercial Approval\<br/>(Client digitally confirms quote fee)"]

&#x20;       Key2["Key 2: Partner Risk Clearance (ISA 220)\<br/>(Partner signs AML/KYC background check)"]

&#x20;   end



&#x20;   GateEval -.-> Key1

&#x20;   GateEval -.-> Key2



&#x20;   Key1 --> CheckCondition{"Both Conditions Met?"}

&#x20;   Key2 --> CheckCondition



&#x20;   CheckCondition -->|"No: Hard Block"| Halt["Halt Pipeline\<br/>(Engagement Letter blocked)"]

&#x20;   CheckCondition -->|"Yes: Cleared"| GenEL["Auto-Generate Engagement Letter (ISA 210)\<br/>& Issue 50% Advance Commercial Invoice"]



&#x20;   GenEL --> Payment["Client Settles 50% Advance Payment"]

&#x20;   Payment --> PostPayment{"Post-Payment Automated Handshake"}



&#x20;   PostPayment --> Receipt["Issue Official Receipt Voucher\<br/>(Automated receipt dispatch)"]

&#x20;   PostPayment --> Portal["Provision Client Portal Workspace\<br/>• System emails temporary credentials (superseded, ADR-0003)\<br/>• Mandatory password reset on first login (superseded, ADR-0003)\<br/>• Document upload window activates\<br/>• Implemented: Microsoft sign-in with a first-sign-in requirement (ADR-0003)"]

\`\`\`



\> [!IMPORTANT]

\> **\*\*Dual-Key Invariant:\*\*** Under no circumstances can an Engagement Letter or Client Portal be provisioned without both Key 1 (Client Commercial Acceptance) and Key 2 (Partner AML/KYC Clearance) recorded in the database.



\---



**## 3.2 Flow 2: Planning, Resource Scheduling & Materiality Formulation**



Once an engagement is onboarded, the system initializes project administration, provisions the document structure, assigns staff, and calculates materiality thresholds from Trial Balance benchmarks.



\`\`\`mermaid

flowchart TD

&#x20;   DualKeyClear(["Partner Clearance of Dual-Key Gate"]) --> ProvisionDir["Automated Engagement Directory Provisioning\<br/>├── 01_Administration & Planning\<br/>├── 02_Trial Balance & Schedules\<br/>├── 03_Fieldwork & Testing\<br/>├── 04_Drafts & Deliverables\<br/>└── 05_Final Signed Archive"]



&#x20;   ProvisionDir --> Scheduling["Resource Scheduling & Assignment\<br/>• Allocate Partner (Approver), Manager (Reviewer), Associates (Preparers)\<br/>• Configure milestones based on statutory reporting cutoff"]



&#x20;   Scheduling --> IngestTB["Ingest Client Trial Balance (TB)\<br/>(Excel / CSV upload with automated account code mapping)"]



&#x20;   IngestTB --> MatEngine["3-Tier Materiality Calculation Engine"]



&#x20;   subgraph MatDetails["Materiality Calculation Engine Steps"]

&#x20;       direction TB

&#x20;       BaseSel["1. Benchmark Base Selection\<br/>• Profit Before Tax: 5%–10%\<br/>• Revenue: 0.5%–2%\<br/>• Total Assets: 0.5%–1%\<br/>• Equity / Net Assets: 1%–2%"]

&#x20;       Metrics["2. Compute Core Metrics\<br/>• Planning Materiality (PM) = Base × Selected %\<br/>• Tolerable Error (TE) = 50%–75% of PM\<br/>• SAD Threshold (Trivial) = 3%–5% of PM"]

&#x20;       Rounding["3. Practical Rounding Tolerance\<br/>• Manager may round within strict ±5% limit (e.g., 53,421 → 53,000)"]

&#x20;       RiskColor["4. Account Risk Stratification\<br/>• GREEN = Balance < TE (Low Risk: Junior Auditor)\<br/>• AMBER = TE < Balance < PM (Moderate Risk: Senior)\<br/>• RED = Balance > PM or High Inherent Risk (Critical: Manager/Partner)"]

&#x20;       BaseSel --> Metrics --> Rounding --> RiskColor

&#x20;   end



&#x20;   MatEngine --> MatDetails

&#x20;   MatDetails --> SignOff(["Formal Partner Sign-Off on Planning & Materiality"])

\`\`\`



**### 3-Tier Materiality Thresholds & Stratification Guide**



\| Materiality Metric | Benchmark Formula / Percentage | Operational Purpose |

\| :--- | :--- | :--- |

\| **\*\*Planning Materiality (PM)\*\*** | Benchmark Base × Chosen % (e.g., PBT 5–10%, Revenue 0.5–2%, Assets 0.5–1%) | Maximum misstatement threshold before financial statements are considered materially misstated. |

\| **\*\*Tolerable Error (TE) / Performance Materiality\*\*** | 50% (High Inherent Risk) to 75% (Low Inherent Risk) of PM | Working threshold used to determine sample sizes and identify individual line items requiring substantive testing. |

\| **\*\*Summary of Audit Differences (SAD)\*\*** | 3% to 5% of PM | Trivial threshold below which misstatements do not need to be accumulated on the audit difference schedule. |

\| **\*\*Practical Rounding Rule\*\*** | Strict ±5% adjustment limit | Managers may round raw values for operational practicality (e.g., QAR 53,421 rounded to QAR 53,000) before Partner sign-off. |



\---



**## 3.3 Flow 3: Technical Fieldwork Execution & Multi-Tier Review Matrix**



This phase constitutes the operational testing core. Line items on the Financial Statement Dashboard launch dedicated workprograms with row-level concurrency, allowing team members to execute testing in parallel without file lockout.



\`\`\`mermaid

flowchart TD

&#x20;   PlanApproved(["Partner Sign-Off on Planning & Materiality"]) --> SplitView["Split Financial Statement Dashboard View"]



&#x20;   subgraph DashboardView["Split Dashboard Interface (Row-Level Concurrency)"]

&#x20;       direction TB

&#x20;       PL["PROFIT & LOSS (P/L) STATEMENT\<br/>• Revenue / Sales ......... [AR Test] [Audit Workprogram]\<br/>• Cost of Goods Sold ..... [AR Test] [Audit Workprogram]\<br/>• Operating Expenses ..... [AR Test] [Audit Workprogram]"]

&#x20;       BS["BALANCE SHEET (B/S) STATEMENT\<br/>• PPE & Intangibles ...... [AR Test] [Audit Workprogram]\<br/>• Inventory .............. [AR Test] [Audit Workprogram]\<br/>• Accounts Receivable .... [AR Test] [Audit Workprogram]\<br/>• Cash & Bank Equivalents  [AR Test] [Audit Workprogram]"]

&#x20;   end



&#x20;   SplitView --> DashboardView



&#x20;   DashboardView --> LaunchAR["[Launch Analytical Review (AR)]\<br/>• Multi-period variance calculation\<br/>• Plausibility & ratio assessment\<br/>• ISA 570 Going Concern evaluation"]

&#x20;   DashboardView --> LaunchWP["[Launch Substantive Workprogram]\<br/>• Pre-configured procedural checklist\<br/>• Ad-hoc custom step injection\<br/>• Population & sampling calculators (MUS / Stratified)\<br/>• Hybrid evidence cross-referencing:\<br/>  - Digital file attachment\<br/>  - Physical binder index: [X-1, Box 3]"]



&#x20;   LaunchAR --> Preparer["PREPARER (Junior Auditor)\<br/>Executes test steps, attaches evidence, submits workpackage"]

&#x20;   LaunchWP --> Preparer



&#x20;   Preparer --> SubmitReview["Submit for Review"]



&#x20;   subgraph ReviewMatrix["Three-Tier Review Matrix & Rejection Loop"]

&#x20;       direction TB

&#x20;       MgrReview["REVIEWER (Audit Senior / Manager)\<br/>Verifies substantive testing and recalculations"]

&#x20;       Decision{"Review Assessment"}

&#x20;       ReworkAction["Return with Mandatory Comments\<br/>(Status reverts to 'Under Rework')"]

&#x20;       PromoteAction["Promote & Clear Procedures"]



&#x20;       MgrReview --> Decision

&#x20;       Decision -->|"Exceptions / Incomplete"| ReworkAction

&#x20;       Decision -->|"Passed"| PromoteAction

&#x20;   end



&#x20;   SubmitReview --> MgrReview

&#x20;   ReworkAction -->|"Reassigned for Rework"| Preparer



&#x20;   PromoteAction --> SRM["Compile Summary Review Memorandum (SRM)\<br/>• Aggregates unadjusted audit differences against SAD\<br/>• Compiles open risks, AJEs, and critical estimates\<br/>• Manager recommendation for sign-off"]



&#x20;   SRM --> ConfGate{"External Confirmations Gatekeeper (ISA 505)\<br/>Check Bank, AR, AP, and Legal Statuses"}



&#x20;   ConfGate -->|"Critical Confirmation Missing"| HoldingBlock["BLOCK Final Report Release\<br/>Trigger Automated 'Holding Letter' to Client"]

&#x20;   ConfGate -->|"All Critical Confirmations Cleared"| PartnerReview["APPROVER (Engagement Partner)\<br/>• Inspects SRM and evaluates unadjusted differences\<br/>• Mandatory direct review of RED risk areas\<br/>• Clears fieldwork completion"]

\`\`\`



**### Split Financial Statement Dashboard Schematic**



\`\`\`

┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐

│                                 SPLIT FINANCIAL STATEMENT DASHBOARD                                    │

├────────────────────────────────────────────────────────────────────────────────────────────────────────┤

│ PROFIT & LOSS (P/L) STATEMENT                               CY (QAR)     PY (QAR)   Var (%)   Actions  │

│ • Revenue / Sales .......................................  4,520,000    3,890,000   +16.2%    [AR] [WP]│

│ • Cost of Goods Sold .................................... (2,810,000)  (2,450,000)  +14.7%    [AR] [WP]│

│ • Operating Expenses ....................................   (940,000)    (810,000)  +16.0%    [AR] [WP]│

├────────────────────────────────────────────────────────────────────────────────────────────────────────┤

│ BALANCE SHEET (B/S) STATEMENT                               CY (QAR)     PY (QAR)   Var (%)   Actions  │

│ • Property, Plant & Equipment (PPE) .....................  1,850,000    1,920,000    -3.6%    [AR] [WP]│

│ • Inventory .............................................    720,000      610,000   +18.0%    [AR] [WP]│

│ • Accounts Receivable ...................................  1,140,000      980,000   +16.3%    [AR] [WP]│

│ • Cash & Bank Equivalents ...............................    890,000      740,000   +20.3%    [AR] [WP]│

└────────────────────────────────────────────────────────────────────────────────────────────────────────┘

&#x20; Legend: [AR] = Launch Analytical Review Interface | [WP] = Launch Substantive Workprogram

\`\`\`



\---



**## 3.4 Flow 4: Reporting, 5-Part Deliverable Release & Compliance Archive**



Following Partner clearance of the SRM and confirmations, the engagement moves to formal closure. The Partner selects the opinion, embeds credentials, and releases the deliverables bundle alongside the final invoice.



\`\`\`mermaid

flowchart TD

&#x20;   ClearedSRM(["Partner Clearance of SRM & External Confirmations"]) --> OpinionSelect{"Partner Selects Audit Opinion Category\<br/>(ISA 700 / 705)"}



&#x20;   OpinionSelect -->|"1. Clean / Unqualified"| CleanPath["Standard Unqualified Opinion"]

&#x20;   OpinionSelect -->|"2. Qualified Opinion"| ModPath["Modified Opinion Workflow"]

&#x20;   OpinionSelect -->|"3. Disclaimer of Opinion"| ModPath

&#x20;   OpinionSelect -->|"4. Adverse Opinion"| ModPath



&#x20;   subgraph ModBuilder["Conditional Qualification Builder (ISA 705)"]

&#x20;       direction TB

&#x20;       ForceFSLI["Force selection of affected FSLI"]

&#x20;       MandatoryText["Expose mandatory text box for quantitative/qualitative rationale"]

&#x20;       InjectBasis["Inject rationale into 'Basis for Qualified/Modified Opinion' paragraph"]

&#x20;       ForceFSLI --> MandatoryText --> InjectBasis

&#x20;   end



&#x20;   ModPath --> ModBuilder

&#x20;   CleanPath --> SignSeal

&#x20;   InjectBasis --> SignSeal["Apply Partner Digital Signature & Official Firm Seal PNG"]



&#x20;   SignSeal --> BundleComp["Auto-Compile Mandatory 5-Part Commercial Deliverables Bundle"]



&#x20;   subgraph DeliverablesBundle["Mandatory 5-Part Deliverables Bundle"]

&#x20;       direction TB

&#x20;       D1["Deliverable 1: Independent Auditor's Report & Certified Financial Statements"]

&#x20;       D2["Deliverable 2: Management Letter (Deficiency → Impact → Recommendation)"]

&#x20;       D3["Deliverable 3: Letter of Representation (LOR formatted for client letterhead)"]

&#x20;       D4["Deliverable 4: Management Correspondences Audit Trail (Confirmation records)"]

&#x20;       D5["Deliverable 5: Final Balance Fee Note (Automated release of remaining 50% bill)"]

&#x20;   end



&#x20;   BundleComp --> DeliverablesBundle



&#x20;   DeliverablesBundle --> FreezePortal["Client Portal Upload Privileges Frozen\<br/>(Workspace switched to Read-Only; client downloads certified bundle)"]



&#x20;   FreezePortal --> ArchivalTimer["60-Day Compliance Archival Countdown (ISA 230)\<br/>• Initiates 60-day timer from signature date\<br/>• Early manual lock available to Partner"]



&#x20;   ArchivalTimer --> PermanentArchive(["Permanent Regulatory Lock\<br/>• Entire engagement folder converted to Read-Only\<br/>• Deletions and modifications permanently blocked\<br/>• Immutable audit trail preserved for regulator inspection"])

\`\`\`



\---



**## 3.5 Flow 5: Practice Management, Time Realization & Firm Bookkeeping**



In parallel with client engagements, the platform captures operational actuals, calculates realization metrics, and maintains the firm's internal practice ledger.



\`\`\`mermaid

flowchart TD

&#x20;   DailyHours["Staff Logs Daily Hours\<br/>(Tagged by Engagement, Phase & FSLI)"] --> RealEngine["Tiered Charge-Out Rates & Realization Engine"]



&#x20;   subgraph RateCards["Role Charge-Out Rates (QAR / Hour)"]

&#x20;       direction TB

&#x20;       R_Part["Engagement Partner: 1,000 QAR / h"]

&#x20;       R_Mgr["Audit Manager: 750 QAR / h"]

&#x20;       R_Sup["Audit Supervisor / Senior: 500 QAR / h"]

&#x20;       R_Assoc["Audit Associate / Junior: 200 QAR / h"]

&#x20;   end



&#x20;   RealEngine --> RateCards



&#x20;   subgraph CalcBlock["Realization & Variance Calculations"]

&#x20;       direction TB

&#x20;       CostCalc["Total Engagement Cost = Σ (Logged Hours × Role Charge-Out Rate)"]

&#x20;       ProfitCalc["Engagement Profitability = Contracted Audit Fee − Total Cost"]

&#x20;       VarCalc["Variance Analysis = Budgeted Phase Hours vs. Actual Hours Logged"]

&#x20;       CostCalc --> ProfitCalc

&#x20;       CostCalc --> VarCalc

&#x20;   end



&#x20;   RateCards --> CalcBlock



&#x20;   CalcBlock --> PracticeLedger["Practice Ledger & Internal Bookkeeping"]



&#x20;   subgraph Expenses["Firm Operational Expenses"]

&#x20;       direction TB

&#x20;       E1["Office Rent & Facility Costs"]

&#x20;       E2["Staff Salaries, End of Service & Benefits"]

&#x20;       E3["Operational Overhead & Administrative Expenses"]

&#x20;       E4["Petty Cash Disbursals"]

&#x20;   end



&#x20;   PracticeLedger --> Expenses



&#x20;   subgraph FirmOutputs["Internal Financial Reporting Outputs"]

&#x20;       direction TB

&#x20;       O1["Internal Firm Monthly Trial Balance"]

&#x20;       O2["Internal Firm Profit & Loss (P&L) Statement"]

&#x20;       O3["Client Accounts Receivable Aging Schedule\<br/>(50% Advance / 50% Final Fee)"]

&#x20;   end



&#x20;   Expenses --> FirmOutputs

\`\`\`



\---



**# 4. Module-by-Module Functional Requirements**



**## 4.1 Module 1: Commercial & CRM Pipeline**



**### 4.1.1 Lead Capture & Client Profiles**



\* **\*\*Multi-Channel Ingestion:\*\*** Ingest leads across Phone, WhatsApp, Email, Web Forms, and In-Person Referrals.

\* **\*\*Corporate Hierarchy:\*\*** Maintain organizational trees covering Holdings, Subsidiaries, and Affiliated Entities.

\* **\*\*Role-Based Communication Routing:\*\*** Maintain a Multi-Contact Directory with explicit notification routing:

&#x20; \* **\*\*Managing Director / General Manager:\*\*** Recipient of Commercial Proposals, Engagement Letters, and final audit deliverable packages.

&#x20; \* **\*\*Chief Financial Officer / Finance Director:\*\*** Recipient of Commercial Invoices, Payment Receipts, and Fee Notes.

&#x20; \* **\*\*Chief Accountant / Audit Liaison:\*\*** Recipient of operational PBC (Provided by Client) document requests and confirmation tracking.



**### 4.1.2 Commercial Proposal Engine**



\* **\*\*Brief Quotation (RFQ/RFP):\*\*** Output a 1–2 page standardized summary showing engagement scope, statutory period, professional fees, payment terms (50% advance / 50% upon draft report), and estimated execution timeline.

\* **\*\*Comprehensive Proposal:\*\*** Auto-compile a multi-page professional presentation incorporating:

&#x20; 1\. Firm Profile, History, and Commercial Registrations.

&#x20; 2\. Assigned Engagement Partner & Audit Team CVs.

&#x20; 3\. Industry-specific Credentials and Portfolio Evidence.

&#x20; 4\. Audit Methodology Overview (ISA compliance framework).

&#x20; 5\. Fee Schedule, Milestone Deliverables, and Execution Timeline.



**### 4.1.3 Dual-Key Onboarding Gatekeeper**



The system shall strictly disallow the generation of an Engagement Letter until two independent authorization keys are marked active in the database:

\* **\*\*Key 1 (Commercial Approval):\*\*** Recorded confirmation of client quote acceptance.

\* **\*\*Key 2 (Risk Clearance):\*\*** Partner completion and digital sign-off of the Client Acceptance/Continuance Checklist (ISA 220).



**#### Enhanced Dual-Key Acceptance — mandatory workflow requirements for version 2.2**



**\*\*Approval decision matrix\*\***



\| Key 1: client commercial acceptance | Key 2: Partner risk clearance | Engagement Letter | Planning activation |

\| --- | --- | --- | --- |

\| Missing | Missing | Blocked | Blocked |

\| Approved | Missing or stale | Blocked | Blocked |

\| Missing or stale | Approved | Blocked | Blocked |

\| Current and valid | Current and unconditional | Eligible for separately authorized issuance | Blocked until 50% advance is fully paid and allocated |

\| Current and valid | Current and unconditional; 50% advance allocated | Eligible for separately authorized issuance | Eligible, subject to other holds and authorization checks |



**\*\*Actor sequence\*\***



\`\`\`mermaid

sequenceDiagram

&#x20;   participant Client

&#x20;   participant CRM as Commercial Workspace

&#x20;   participant Partner

&#x20;   participant Risk as Acceptance Records

&#x20;   participant Docs as Engagement Documents

&#x20;   participant Finance

&#x20;   participant Planning

&#x20;   Client->>CRM: Accept exact dispatched quotation revision

&#x20;   CRM->>Risk: Persist commercial response evidence

&#x20;   Partner->>Risk: Review AML, KYC, independence and continuance

&#x20;   Partner->>Risk: Record current risk decision

&#x20;   CRM->>Risk: Recheck both independent approval keys

&#x20;   alt Both keys current and approved

&#x20;       Risk-->>Docs: Engagement Letter generation eligible

&#x20;       Docs->>Finance: Initiate 50 percent advance fee workflow

&#x20;       Finance->>Finance: Record and allocate payment

&#x20;       alt Advance fully paid and no outstanding hold

&#x20;           Finance-->>Planning: Activation may proceed through authorized command

&#x20;       else Unpaid, partial, stale or held

&#x20;           Finance-->>Planning: Activation blocked with exact reasons

&#x20;       end

&#x20;   else Missing or invalid approval

&#x20;       Risk-->>CRM: Block document — show missing or stale key

&#x20;   end

\`\`\`



\- **\*\*Key 1 — Client Commercial Acceptance:\*\*** Capture the client's acceptance of the exact dispatched quotation revision, scope, legal entity, service route, period, currency, fee and 50/50 payment terms. Record authorized contact, response channel, timestamp and linked acceptance evidence. A changed offer requires a renewed acceptance.

\- **\*\*Key 2 — Partner Risk Clearance:\*\*** Independently require the Engagement Partner's current, unconditional client acceptance/continuance decision against the current due-diligence generation. Require AML/KYC, beneficial ownership, independence, conflicts, management integrity and other relevant risk checks. Conditional, rejected, superseded or stale decisions never clear this key.

\- **\*\*Gate outcomes:\*\*** neither key ⇒ blocked, two approvals needed; only Key 1 ⇒ awaiting Partner risk clearance; only Key 2 ⇒ awaiting client commercial acceptance; both current keys ⇒ eligible for Engagement Letter, NOT automatically activated.

\- The full 50% advance payment is a separate gate after the two keys: generate the Engagement Letter and advance invoice under their existing authorization rules; activate engagement and portal-enabled planning only after the linked 50% advance is fully paid and allocated. A partially paid or merely invoiced advance is not sufficient.

\- **\*\*Changes and exceptions:\*\*** a declined proposal returns to revision/re-dispatch or closure; a conditional or refused Partner decision returns to risk remediation and fresh unconditional review; open holds, stale commercial revisions and mismatched client/service scopes block protected operations. Never let task completion, workflow rules or staff overrides bypass these guards.

\- **\*\*Onboarding interface:\*\*** provide two independent approval cards, each showing Pending, Awaiting Evidence, Approved, Rejected or Stale as applicable, owner, time, evidence link, revision and authorized next action. Show a separate Engagement Letter readiness row and a third 50% Advance Payment card. Hide sensitive AML/KYC findings from the client portal.

\- **\*\*Audit history:\*\*** preserve the authentic approving actor, decision generation, source evidence, timestamps and reason for rejection; re-evaluate both keys at document generation and activation, and reject stale or concurrently changed inputs.

\- **\*\*Worked example:\*\*** on an accepted QAR 20,000 quotation, Partner acceptance is still mandatory before an Engagement Letter may be issued. Both keys permit letter issuance and the QAR 10,000 advance invoice, but activation waits until the full QAR 10,000 is received and allocated.



*\*Implementation note:\** the unconditional 50% advance activation gate is recorded in ADR-0012. The matrix describes required behaviour; it does not by itself prove it. Verification status is in the v2.2 delta of the gap-verification tracker.



**### 4.1.4 Engagement Letter Generation (ISA 210)**



\* **\*\*Template Selection:\*\*** Automatically pull standardized templates based on engagement type:

&#x20; \* External Statutory Audit Template (ISA 210).

&#x20; \* Internal Audit / Agreed-Upon Procedures Template (ISRS 4400).

\* **\*\*Minimal Manual Input:\*\*** Pre-populate from entity profile; require minimal manual field input (Client Legal Name, Period Covered, Agreed Fee, Submission Deadlines).

\* **\*\*Partner Authorization:\*\*** Apply Partner digital stamp and signature upon generation.



**### 4.1.5 Advance Invoicing, Receipting & Portal Onboarding**



\* **\*\*Advance Invoicing:\*\*** Generate the 50% Advance Invoice concurrently with the Engagement Letter.

\* **\*\*Payment Recording:\*\*** Record payment settlement with transaction reference data (cheque number, bank transfer reference code).

\* **\*\*Automated Receipt Generation:\*\*** Instantly compile and dispatch an official payment receipt voucher to the client upon recording payment.

\* **\*\*Automated Client Portal Provisioning:\*\***

&#x20; \* Auto-generate an isolated workspace for the client.

&#x20; \* Email temporary access credentials to the designated Client Audit Liaison.

&#x20; \* Enforce mandatory password reset on first login before document submission features are unlocked.

&#x20; \* *\*Superseded by approved ADR-0003: the client portal signs in with Microsoft and a first-sign-in requirement. No local password is issued, stored or emailed. The two bullets above are the original wording, kept for provenance. Live tenant acceptance remains a separate gate.\**

&#x20; \* Surface real-time status badges on requested items: \`Pending Upload\`, \`Under Review\`, \`Approved\`, \`Rejected / Re-upload Required\`.

&#x20; \* **\*\*Mandatory Rejection Reason:\*\*** If an item is rejected, the auditor must enter a mandatory rejection reason that surfaces immediately on the client's screen.

&#x20; \* **\*\*Temporal Lock:\*\*** Client document upload privileges automatically freeze when the final audit report is released.



\---



**## 4.2 Module 2: Administration, Governance & Planning**



**### 4.2.1 Dual-Track Acceptance & Continuance Risk Gatekeeper**



\* **\*\*Track A: New Client Acceptance Path:\*\***

&#x20; \* Mandatory questionnaires: Ultimate Beneficial Ownership (UBO), Anti-Money Laundering (AML) background check, Know Your Customer (KYC) documentation, assessment of management integrity, financial viability evaluation, independence and conflict of interest checks.

\* **\*\*Track B: Recurring Client Continuance Path:\*\***

&#x20; \* Delta review checklist: Settlement of prior-year professional fees, key management or shareholding changes, substantial new credit facilities or loans, ongoing litigation or legal notices, reported fraud or regulatory investigations.

\* **\*\*Mandatory Partner Sign-Off:\*\*** Block project transition to operational planning until the Partner executes the digital acceptance gate.



**### 4.2.2 Resource Allocation & Scheduling**



\* **\*\*Visual Capacity Calendar:\*\*** Track team availability, target utilization, and leave schedules.

\* **\*\*Explicit Role Assignment:\*\*** Assign explicit roles per engagement: Engagement Partner (Approver), Audit Manager/Senior (Reviewer), and Associates (Preparers).

\* **\*\*Statutory Milestones:\*\*** Configure operational milestones relative to statutory cutoffs (e.g., December 31 year-end → fieldwork commences January Week 1 → draft report by February 15 → final signed report by March 15).



**### 4.2.3 Engagement Directory Provisioning**



Upon Partner risk acceptance, the system automatically provisions the standard 5-folder engagement taxonomy:

\`\`\`

[Engagement Root Directory]

├── 01_Administration & Planning

├── 02_Trial Balance & Schedules

├── 03_Fieldwork & Testing

├── 04_Drafts & Deliverables

└── 05_Final Signed Archive

\`\`\`



**### 4.2.4 3-Tier Materiality Calculation Engine**



\* **\*\*Dynamic Benchmarking:\*\*** Link calculation directly to ingested Trial Balance balances:

&#x20; \* **\*\*Normalized Profit Before Tax (PBT):\*\*** 5.0% – 10.0%

&#x20; \* **\*\*Total Revenue:\*\*** 0.5% – 2.0%

&#x20; \* **\*\*Total Assets:\*\*** 0.5% – 1.0%

&#x20; \* **\*\*Equity / Net Assets:\*\*** 1.0% – 2.0%

\* **\*\*Core Output Calculations:\*\***

&#x20; \* **\*\*Planning Materiality (PM):\*\*** Benchmark Base × percentage.

&#x20; \* **\*\*Tolerable Error (TE) / Performance Materiality:\*\*** 50% – 75% of PM.

&#x20; \* **\*\*Summary of Audit Differences (SAD) Threshold:\*\*** 3% – 5% of PM (trivial error cutoff).

\* **\*\*Rounding Rule:\*\*** Allow managers to apply practical rounding to computed materiality values within a strict ±5.0% maximum limit prior to Partner approval.

\* **\*\*Visual Color-Coded Risk Stratification:\*\***

&#x20; \* **\*\*Green (Low Risk):\*\*** Balance below TE; standard automated audit programs; assignable to junior staff.

&#x20; \* **\*\*Amber (Moderate Risk):\*\*** Balance between TE and PM; requires senior substantive testing and sampling.

&#x20; \* **\*\*Red (Critical / High Risk):\*\*** Balance exceeds PM, involves critical accounting estimates, or carries high inherent risk; requires mandatory Manager-level execution and Partner review.



\---



**## 4.3 Module 3: Technical Execution & Audit Fieldwork**



**### 4.3.1 Trial Balance Ingestion & Split Dashboard Interface**



\* **\*\*Trial Balance Ingestion:\*\*** Ingest Trial Balance files via Excel/CSV (exported from QuickBooks, Tally, Zoho, SAP, etc.).

\* **\*\*Automated FSLI Mapping:\*\*** Automated mapping to standardized Financial Statement Line Items (FSLI) with historical memory.

\* **\*\*Split Dashboard Interface:\*\***

&#x20; \* Displays **\*\*Profit & Loss (P/L) Statement\*\*** on the upper half and **\*\*Balance Sheet (B/S)\*\*** on the lower half.

&#x20; \* Displays Current Year Balance, Prior Year Comparative, and Percentage Variance per row.

&#x20; \* Every line item features two interactive action triggers:

&#x20;   \* \`[AR Test]\`: Opens the Analytical Review Interface.

&#x20;   \* \`[Audit Workprogram]\`: Opens substantive audit procedures.

\* **\*\*Row-Level Concurrency:\*\*** Ensure multiple auditors can simultaneously work on different line items (e.g., Auditor A on Sales, Auditor B on Fixed Assets) without file lockouts or overwrite conflicts.



**### 4.3.2 Workprogram Execution & Evidence Cross-Referencing**



\* **\*\*Pre-Loaded Standard Checklists:\*\*** Standard procedural checklists per FSLI (Ownership, Valuation, Completeness, Existence, Cut-off).

\* **\*\*Ad-Hoc Step Insertion:\*\*** Allow field auditors to insert custom, editable procedural rows into active workprograms to address unique engagement risks.

\* **\*\*Sampling Engine:\*\*** Built-in calculators for Monetary Unit Sampling (MUS), Systematic Random Sampling, and Stratified Attribute Sampling.

\* **\*\*Hybrid Evidence Linking:\*\***

&#x20; \* **\*\*Digital Evidence:\*\*** Direct link to electronic spreadsheets, PDFs, and PBC uploads.

&#x20; \* **\*\*Physical Evidence Reference:\*\*** Dedicated text field for physical file index tracking (e.g., \`File Index: X-1, Box 3, Shelf B\`).

\* **\*\*Analytical Review & Going Concern:\*\*** Standard templates for comparative financial analysis and mandatory ISA 570 Going Concern compliance checklists.



**### 4.3.3 Three-Tier Review Matrix & Workflow Governance**



\* **\*\*Preparer Execution:\*\*** Executes procedures, cross-references digital/physical evidence, and clicks \`[Submit for Review]\`.

\* **\*\*Reviewer Rejection Loop:\*\***

&#x20; \* Manager reviews completed tests.

&#x20; \* Can flag individual steps, input mandatory review notes, and click \`[Return with Comments]\`, automatically reverting the status to \`Under Rework\` and reassigning the Preparer.

\* **\*\*Summary Review Memorandum (SRM):\*\***

&#x20; \* Auto-compiled upon Manager approval of all workprograms.

&#x20; \* Summarizes high-level audit variances, adjustments (AJEs), unadjusted differences against SAD/PM, and significant accounting estimates.

\* **\*\*Approver Clearance:\*\*** Partner conducts targeted reviews of high-risk (Red) areas and the SRM, formally signing off to unlock reporting.



**### 4.3.4 Third-Party Confirmations Dashboard & Gatekeeper**



\* **\*\*Central Tracking Grid:\*\*** Track Bank, Accounts Receivable, Accounts Payable, Inventory, and Legal confirmations.

\* **\*\*Holding Letter Blocker:\*\*** If any confirmation marked as Critical remains unreturned, the system blocks the release of the final audit report and auto-generates a "Pending Confirmation / Holding Letter" to client management.



\---



**## 4.4 Module 4: Reporting & Final Deliverables**



**### 4.4.1 Audit Opinion Selection Engine (ISA 700 / 705)**



\* **\*\*Partner-Exclusive Opinion Selector:\*\*** Dedicated dropdown selector accessible exclusively by the Engagement Partner:

&#x20; 1\. Clean / Unqualified Opinion

&#x20; 2\. Qualified Opinion

&#x20; 3\. Disclaimer of Opinion

&#x20; 4\. Adverse Opinion

\* **\*\*Conditional Qualification Builder:\*\*** Selecting Qualified, Disclaimer, or Adverse dynamically prompts the Partner to select the affected FSLI and input a mandatory textual justification. The system injects this text directly into the "Basis for Qualified/Modified Opinion" paragraph in compliance with ISA 705.

\* **\*\*Digital Credentials:\*\*** Embeds the Partner's digital signature and official firm seal onto the final certified document.



**### 4.4.2 Mandatory 5-Part Commercial Deliverables Bundle**



Once the Partner authorizes the file, the system compiles the final package:

1\. **\*\*Deliverable 1: Independent Auditor's Report & Audited Financial Statements:\*\*** Certified, sealed, and digitally signed PDF.

2\. **\*\*Deliverable 2: Management Letter:\*\*** Structured internal control observations report (Deficiency → Impact → Auditor Recommendation).

3\. **\*\*Deliverable 3: Letter of Representation (LOR):\*\*** Formatted representation template populated with engagement figures, ready to be exported for printing on client letterhead, signed by executive management, and re-uploaded.

4\. **\*\*Deliverable 4: Management Correspondences Audit Trail:\*\*** Summary of all formal audit inquiries, confirmation results, and cleared queries.

5\. **\*\*Deliverable 5: Final Balance Fee Note:\*\*** Automated trigger generating the invoice for the remaining 50% professional fee balance.



**### 4.4.3 Regulatory File Lock & Compliance Archival (ISA 230)**



\* **\*\*60-Day Archival Timer:\*\*** Enforce an automated 60-day regulatory file completion countdown timer starting from the date of the Partner's signature.

\* **\*\*Permanent Read-Only Lock:\*\*** Upon timer expiration (or manual Partner command), convert the entire engagement archive to Read-Only status. Deletions, modifications, and overwrites are permanently blocked.

\* **\*\*Immutable Audit Trail:\*\*** Maintain an immutable, timestamped audit log of all system actions, reviews, and sign-offs.



\---



**## 4.5 Module 5: Practice Analytics & Internal Bookkeeping**



**### 4.5.1 Real-Time Profitability & Utilization Analytics**



\* **\*\*Tiered Charge-Out Rates Engine:\*\***

&#x20; \* Engagement Partner: **\*\*1,000 QAR / hour\*\***

&#x20; \* Audit Manager: **\*\*750 QAR / hour\*\***

&#x20; \* Audit Supervisor / Senior: **\*\*500 QAR / hour\*\***

&#x20; \* Audit Associate / Junior: **\*\*200 QAR / hour\*\***

\* **\*\*Engagement Profitability Calculation:\*\***



$$\text{Engagement Profitability} = \text{Contracted Audit Fee} - \sum (\text{Staff Hours Logged} \times \text{Charge-Out Rate})$$



\* **\*\*Variance Analysis:\*\*** Track budget vs. actual hours variance per engagement phase to evaluate realization rates and staff performance.



**### 4.5.2 Practice Ledger & Internal Bookkeeping**



\* **\*\*Dedicated Operational Accounting Ledger:\*\*** Record firm internal operations:

&#x20; \* Office Rent & Facility Costs

&#x20; \* Staff Salaries, End of Service & Benefits

&#x20; \* Partner Withdrawals

&#x20; \* Petty Cash Disbursals

\* **\*\*Internal Financial Reporting:\*\*** Generate an internal firm Trial Balance, Monthly Profit & Loss Statement, and Client Accounts Receivable Aging Schedule (tracking 50% Advance and 50% Final Fee payments).



\---



**# 5. System State Machine & Lifecycle Transitions**



**## 5.1 State Machine Transition Matrix**



\| # | Current State | Allowed Actions | Gate / Condition to Advance | Next State | Boundary / Enforced Rule |

\| :-: | :--- | :--- | :--- | :--- | :--- |

\| **\*\*1\*\*** | \`LEAD_INGESTION\` | Log inquiry, capture company and contact data | Minimum entity and primary contact data validated | \`PROPOSAL_GENERATION\` | Lead stage only; no client workspace created. |

\| **\*\*2\*\*** | \`PROPOSAL_GENERATION\` | Build Brief Quote or Comprehensive Proposal, dispatch to client | Proposal dispatched via Email / WhatsApp | \`DUAL_KEY_PENDING\` | Quotes enforce standard 50/50 fee terms. |

\| **\*\*3\*\*** | \`DUAL_KEY_PENDING\` | Complete Client Acceptance Checklist (AML/KYC), record client commercial approval | **\*\*Dual-Key Clearance:\*\*** Both Client Acceptance AND Partner AML Approval confirmed | \`ADVANCE_BILLING\` | Hard block: EL cannot generate without both keys. |

\| **\*\*4\*\*** | \`ADVANCE_BILLING\` | Generate Engagement Letter & 50% Advance Invoice | 50% advance payment confirmed and recorded | \`PORTAL_ACTIVE_PLANNING\` | Portal remains inactive until advance payment clears. |

\| **\*\*5\*\*** | \`PORTAL_ACTIVE_PLANNING\` | Provision Client Portal, schedule team, ingest Trial Balance, calculate materiality | Planning signed off by Partner, TB mapped to FSLIs | \`FIELDWORK_EXECUTION\` | Materiality rounding capped at ±5.0%. |

\| **\*\*6\*\*** | \`FIELDWORK_EXECUTION\` | Execute workprograms, attach digital/physical evidence, log confirmation requests | All assigned FSLI procedures submitted by Preparers | \`MANAGERIAL_REVIEW\` | Row-level concurrency active on Split Dashboard. |

\| **\*\*7\*\*** | \`MANAGERIAL_REVIEW\` | Review workpapers, issue review notes/rework, compile SRM | Zero open review notes, SRM compiled, critical confirmations returned | \`PARTNER_APPROVAL\` | Any open review note reverts status to Under Rework. |

\| **\*\*8\*\*** | \`PARTNER_APPROVAL\` | Partner inspects SRM, reviews Red-risk areas, selects Audit Opinion | Partner applies digital signature and firm seal | \`DELIVERABLE_RELEASE\` | Modified opinions require mandatory justification. |

\| **\*\*9\*\*** | \`DELIVERABLE_RELEASE\` | Generate 5-part deliverables package, issue 50% balance invoice, freeze client portal uploads | Final package generated and delivered to client | \`COMPLIANCE_COUNTDOWN\` | Client portal upload privileges immediately freeze. |

\| **\*\*10\*\*** | \`COMPLIANCE_COUNTDOWN\` | Review final archive; Partner may trigger early lock | 60 calendar days elapsed since signature date OR manual lock triggered | \`ARCHIVED_READ_ONLY\` | ISA 230 regulatory file assembly timer. |

\| **\*\*11\*\*** | \`ARCHIVED_READ_ONLY\` | Read-only viewing and regulator inspection export | File is permanently locked; modifications strictly disallowed | *\*Terminal State\** | Immutable, tamper-evident audit archive. |



\---



**\*\*Implementation note for the dispatch-channel requirement:\*\*** the source requirement above names Email and WhatsApp. The current product can queue email through its configured server-side mail provider; WhatsApp is a lead-source value only and has no outbound dispatch integration. Provider acceptance is not proof of recipient delivery or reading.



**## 5.2 State Machine Visual Workflow**



\`\`\`mermaid

stateDiagram-v2

&#x20;   [\*] --> LEAD_INGESTION: Client Ingestion (Phone / WhatsApp / Email / Web)

&#x20;   LEAD_INGESTION --> PROPOSAL_GENERATION: Minimum Entity Profile Validated

&#x20;   PROPOSAL_GENERATION --> DUAL_KEY_PENDING: Quote or Comprehensive Proposal Dispatched

&#x20;   DUAL_KEY_PENDING --> ADVANCE_BILLING: Dual-Key Cleared (Client Approval + Partner AML/KYC)

&#x20;   DUAL_KEY_PENDING --> [\*]: Hard Block / Prospect Rejected



&#x20;   ADVANCE_BILLING --> PORTAL_ACTIVE_PLANNING: 50% Advance Payment Recorded & Receipt Issued

&#x20;   PORTAL_ACTIVE_PLANNING --> FIELDWORK_EXECUTION: Directory Provisioned, Staff Scheduled, TB Mapped & Materiality Signed Off

&#x20;   FIELDWORK_EXECUTION --> MANAGERIAL_REVIEW: Preparer Submits All Assigned Procedures

&#x20;   MANAGERIAL_REVIEW --> FIELDWORK_EXECUTION: Review Notes Issued (Under Rework status triggered)

&#x20;   MANAGERIAL_REVIEW --> PARTNER_APPROVAL: Zero Open Notes, SRM Compiled & Critical Confirmations Cleared

&#x20;   PARTNER_APPROVAL --> DELIVERABLE_RELEASE: Opinion Selected, Partner Digital Signature & Seal Applied

&#x20;   DELIVERABLE_RELEASE --> COMPLIANCE_COUNTDOWN: 5-Part Bundle Released, Final 50% Invoice Issued & Portal Uploads Frozen

&#x20;   COMPLIANCE_COUNTDOWN --> ARCHIVED_READ_ONLY: 60 Calendar Days Elapsed (ISA 230) OR Manual Partner Lock

&#x20;   ARCHIVED_READ_ONLY --> [\*]: Permanently Sealed Read-Only Archive

\`\`\`



**## 5.3 Display Phases and Canonical States**



The eleven canonical states in §5.1 remain authoritative. Sections 6 and 7 present a simpler seven-phase user-facing workflow but do not replace these gate conditions. The diagram in §5.2 is not itself a permission model: transitions follow their underlying business commands and evidentiary gates.



\| Display phase | Canonical states represented |

\| --- | --- |

\| 1. Client Onboarding | \`LEAD_INGESTION\`, \`PROPOSAL_GENERATION\`, \`DUAL_KEY_PENDING\`, \`ADVANCE_BILLING\` |

\| 2. Audit Planning | \`PORTAL_ACTIVE_PLANNING\` |

\| 3. Audit Fieldwork | \`FIELDWORK_EXECUTION\` |

\| 4. Manager Review | \`MANAGERIAL_REVIEW\` |

\| 5. Partner Sign-off | \`PARTNER_APPROVAL\` |

\| 6. Report and Delivery | \`DELIVERABLE_RELEASE\` |

\| 7. Archive and Close | \`COMPLIANCE_COUNTDOWN\`, \`ARCHIVED_READ_ONLY\` |



\---



**# 6. Simplified Seven-Phase Engagement Workflow — New Requirements**



**## 6.1 Purpose and mapping**



This user-interface and task orchestration layer SHALL NOT modify the canonical Section 5 lifecycle state names, stage gates, reviews, or compliance evidence. Map stages as follows:



\- Phase 1 Client Onboarding: \`LEAD_INGESTION\`, \`PROPOSAL_GENERATION\`, \`DUAL_KEY_PENDING\`, \`ADVANCE_BILLING\`.

\- Phase 2 Audit Planning: \`PORTAL_ACTIVE_PLANNING\`.

\- Phase 3 Audit Fieldwork: \`FIELDWORK_EXECUTION\`.

\- Phase 4 Manager Review: \`MANAGERIAL_REVIEW\`, with procedure-level rework loop back to affected fieldwork.

\- Phase 5 Partner Sign-off: \`PARTNER_APPROVAL\`.

\- Phase 6 Report & Delivery: \`DELIVERABLE_RELEASE\`.

\- Phase 7 Archive & Close: \`COMPLIANCE_COUNTDOWN\` and \`ARCHIVED_READ_ONLY\`.



**## 6.2 Engagement dashboard**



Show a seven-step visual tracker with simple titles, responsible user, relevant milestone/date, task completion, gate readiness, explicit blockers and the next permitted action. Offer expandable phase checklists and contextual links to client, planning, financial statement line item, PBC, review, completion, finance and records workbenches. Distinguish task completion from regulatory clearance. Make the design responsive and keyboard accessible.



**## 6.3 Phase detail requirements**



\- **\*\*Client Onboarding:\*\*** quotation and client response, separated dual-key approvals, Engagement Letter, 50% invoice/payment and activation eligibility.

\- **\*\*Audit Planning:\*\*** staff and deadlines, workspace provisioning, TB import, approved mapping, materiality PM/TE/SAD, risk routing and Partner planning approval. Updated inputs must invalidate stale approvals.

\- **\*\*Audit Fieldwork:\*\*** FSLI action links, workprograms, evidence, analytical reviews, sampling, preparer assignments and versioned result submission.

\- **\*\*Manager Review:\*\*** independent reviewer authority and risk-based hierarchy; mandatory comments on rejection; preserve history and route affected work back to its preparer; resolve all relevant review notes; compile a current SRM.

\- **\*\*Partner Sign-off:\*\*** review high-risk areas/SRM, select an ISA opinion and rationale when modified, and sign the final report under approved credentials.

\- **\*\*Report & Delivery:\*\*** independently evaluated critical confirmations, controlled audited statement and report delivery, management letter, LOR, correspondence, final fee note and release checkpoints.

\- **\*\*Archive & Close:\*\*** signature-anchored 60-day countdown, authorized early lock and effective local read-only freeze. Show external provider retention/protection independently; an expired timer alone is not archive success.



**## 6.4 Reusable practice templates**



Permit administrator-approved versioned templates by engagement type and industry, with editable task groups, owners, due-date offsets, dependencies, effort estimates, client requests and ad-hoc tasks. Pin a template revision to each engagement; changes to templates do not silently rewrite existing work. These templates cannot remove mandatory domain gates. Provide My Tasks, Team Work, Waiting for Client, Under Review, Rework, Overdue and Completed list filters plus Kanban/calendar views.



**## 6.5 Recurring audit engagements**



Support creating the next annual engagement with reusable templates, prior-year client details and historical mapping suggestions. Require fresh commercial consent, current Partner continuance risk approval, current TB/materiality, professional audit review and reporting decisions. Never carry prior-year acceptance or opinion sign-offs forward as valid authorization.



**## 6.6 Safe workflow automation**



\`\`\`mermaid

flowchart LR

&#x20;   T["Versioned task template"] --> RUN["Engagement task instances"]

&#x20;   RUN --> AUTO["Task automator"]

&#x20;   AUTO --> GOOD["Permitted: assignments, follow-ups, reminders and escalation"]

&#x20;   AUTO --> G{"Needs protected professional or financial decision?"}

&#x20;   G -->|"Yes"| C["Submit to authorized domain command and independent actor"]

&#x20;   G -->|"No"| GOOD

&#x20;   C --> V["Check role, client scope, evidence and current revision"]

&#x20;   V --> E["Persist authentic decision and audit event"]

&#x20;   E --> PROJ["Recompute canonical lifecycle projection"]

&#x20;   classDef decision fill:#fff1d6,stroke:#b88b2c,color:#5d4316

&#x20;   class G decision

\`\`\`



Automators MAY assign tasks, create follow-up checklists, set reminders and due dates, escalate overdue PBC requests and notify permitted users after genuine domain events. They SHALL NOT synthesize approvals, record payment, clear risk, approve materiality, sign an opinion, post invoices, issue release or freeze files. Every automation must reauthorize, use idempotency keys, pin current versions, log its changes and fail safely on stale inputs. Task graphs must reject dependency cycles.



**## 6.7 User role, security and progress rules**



Preparer sees assigned fieldwork/rework; Reviewer sees submissions and review notes; Partner sees acceptance, high-risk areas, opinion and release; Client sees only authorized commercial and PBC status. Calculate phase readiness from server-side records, not mutable task flags. Track task percentage separately from mandatory gate satisfaction, avoid leaking AML/KYC evidence to client users and show readable blocker reasons.



**## 6.8 Acceptance criteria**



Acceptance criteria to verify:



1\. All 11 canonical states map to exactly one of seven phases.

2\. Absent/one-key approvals block Engagement Letter issuance.

3\. Both keys allow the letter but do not bypass the full 50% advance.

4\. Changed offer or risk basis invalidates the affected approval.

5\. Rejection/conditional clearance and open holds cannot be overwritten through tasks.

6\. Independent reviewer and mandatory rework comments remain enforced.

7\. Open review notes and stale materiality block subsequent professional decisions.

8\. Critical confirmation cases block report release.

9\. Frozen archival state requires an effective committed lock and does not imply external immutability.

10\. Recurring templates never copy professional sign-offs as current.

11\. Scoped actors cannot access unrelated clients.

12\. Automation retries cannot duplicate or forge domain events.



\---



**# 7. Architectural Non-Regression Requirements — New**



All domain commands and evidence records remain the sole authority for progression. The existing 11-stage derived lifecycle and five function modules remain unchanged. The seven-phase user experience is a deterministic presentation mapping. Task templates, personal work queues, recurring-engagement creation, deadline automation and dashboard analytics are separate supporting capabilities. All permissions, professional judgments, signed document integrity, 50/50 commercial terms and archive protections must continue to be enforced by authoritative services. Review external Microsoft identity/SharePoint/records protections separately before labelling them production compliant.
