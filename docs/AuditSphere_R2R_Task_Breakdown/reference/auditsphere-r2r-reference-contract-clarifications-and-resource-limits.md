# Contract clarifications and bounded resource policy

[Master index](../auditsphere-r2r-index-task-breakdown.md) · [Unchanged source blueprint](../source/auditsphere-r2r-source-blueprint-modules-20-26-historical.md)

**Reference, not an execution task.** The excerpts below retain the source contract. Baseline facts and research are historical to the supplied document; this breakdown does not re-audit the repository or re-verify external standards.

<!-- SOURCE-LINES: 1475-1517 -->
## 10. Contract clarifications for implementation review

**Filter and enum shorthand used in request tables:**

| Shorthand | Required typed meaning |
|---|---|
| `StatusFilters` | Allowlisted TechnicalState[], ManagementState[], ReflectionState[], Currentness[], optional date range; never a raw query expression. |
| `ScopeFilter` | Optional ClientId/EngagementId or GroupId, validated as a subset of server-granted scope; no client/group wildcard expansion. |
| `KindFilter` / `SourceSide` | ReconciliationKind enum / Ledger or Supporting enum. |
| `AsOfFilter` | Explicit DateOnly for time-sensitive queries; not a hidden switch to today's balances. |
| `AccountFilter` | Stable account IDs/codes and approved dimension-value IDs belonging to the selected chart/context. |
| `StageFilter` | Explicit existing/approved accounting, management, partner or group-review stages, not caller-defined role names. |
| `ScheduleKind` | CashFlow or Equity for the shared supplementary-schedule review request; unknown values rejected. |
| `Format` / `ExportFormat` | An allowlisted CSV/XLSX/DOCX/PDF enum, further restricted by each query and output profile. |

**Transaction adapter condition:** a facade is not accepted merely because it calls an old method. Operation identity, current authority, input fences and invalidation/evidence must be committed with the business mutation inside that method's transaction. Do not add an idempotency record outside the commit or declare success before the original service commits. Extract the smallest transactional core where necessary to satisfy this condition.

**Close versus input-set selection:** client/period locking must prevent a new required adjustment/reconciliation/disclosure from being added between readiness calculation and close. Capturing references to existing rows without a collection-membership revision does not satisfy that requirement.

**No implicit functional expansion:** the complete R2R lifecycle includes handling unsupported inputs honestly. It does not promise every financial-industry, GAAP, business-combination, hedge, tax measurement or statutory filing engine. Each supported framework/method profile must be explicitly approved, fully implemented and tested; unsupported profiles cannot yield claimed-compliant output.

**Document-maintenance rule:** update the exact contract, affected source mapping, validator, migration and consuming-module tests together. A renamed DTO or changed enum without consumer/test changes is an incomplete implementation, even when its individual project compiles.


<a id="section-10-1"></a>
### 10.1 Bounded resource-policy proposal

Create or reuse an approved versioned resource policy; every intake/layout operation reports the applicable limits. The following are **proposed initial upper bounds**, not observed capacity results. A stricter existing repository limit wins until a reviewed change replaces it. Do not increase current limits implicitly.

| Resource | Proposed initial bound / treatment |
|---|---|
| Interactive page | 200 rows; source exports through bounded streaming rather than circuit materialization |
| TB source | 50 MiB received file and 100,000 normalized rows, subject to stricter existing parser caps |
| GL source | Preserve current 100,000 transaction / 500,000 line ceiling and 10,000 transaction / 50,000 line chunk ceiling |
| Upload transport | Reuse current authenticated chunk limit; total compressed and expanded limits both mandatory |
| XLSX expansion | 200 MiB maximum expanded input, bounded entry count/ratio/shared strings; reject before unbounded allocation; stricter existing limit wins |
| Journal or reconciliation draft | 1,000 editable lines/items per revision; larger workflows require a separately reviewed bulk path |
| Statement layout | 1,000 line definitions, dependency depth 32; cycle detection required independently of the depth bound |
| Note table | 5,000 cells per note revision, bounded text per cell; no executable formulas |
| Text | Codes 100, labels 300, standard rationale 4,000 characters unless a documented existing stricter limit applies |

Acceptance must benchmark these agreed bounds and tune them downward where appropriate. Limit changes are configuration revisions with tests, not arbitrary constants altered by an agent to make an oversized fixture pass.
