# Graph Report - .  (2026-09-20)

## Corpus Check
- Large corpus: 232 files · ~573,035 words. Semantic extraction will be expensive (many Claude tokens). Consider running on a subfolder.

## Summary
- 3033 nodes · 7063 edges · 236 communities (219 shown, 17 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 118 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Audit Planning & Durable Drafts
- Actor Context & Recovery Authz
- Practice CRM Workflow
- NuGet Package Locks
- EF DbContext & Command Requests
- Financial Statement Calculation
- Firm Ledger Service
- Project Namespaces
- PBC Provider Transfer Sink
- Trial Balance Engine
- Billing Service
- .NET Compiler Dependencies
- .NET Configuration Dependencies
- PBC Upload Service
- .NET DI/Logging Dependencies
- NuGet Package Locks
- NuGet Package Locks
- EFCore/Diagnostics Dependencies
- Community 18
- Community 19
- Community 20
- Community 21
- Community 22
- Community 23
- Community 24
- Community 25
- Community 26
- Community 27
- Community 28
- Community 29
- Community 30
- Community 31
- Community 32
- Community 33
- Community 34
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65
- Community 66
- Community 67
- Community 68
- Community 69
- Community 70
- Community 71
- Community 72
- Community 73
- Community 74
- Community 75
- Community 76
- Community 77
- Community 78
- Community 79
- Community 80
- Community 81
- Community 82
- Community 83
- Community 84
- Community 85
- Community 86
- Community 87
- Community 88
- Community 89
- Community 90
- Community 91
- Community 92
- Community 93
- Community 94
- Community 95
- Community 96
- Community 97
- Community 98
- Community 99
- Community 100
- Community 101
- Community 102
- Community 103
- Community 104
- Community 105
- Community 106
- Community 107
- Community 108
- Community 109
- Community 110
- Community 111
- Community 112
- Community 113
- Community 114
- Community 115
- Community 116
- Community 117
- Community 118
- Community 119
- Community 120
- Community 121
- Community 122
- Community 123
- Community 124
- Community 125
- Community 126
- Community 127
- Community 128
- Community 129
- Community 130
- Community 131
- Community 132
- Community 133
- Community 134
- Community 135
- Community 136
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 145
- Community 146
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154
- Community 155
- Community 156
- Community 157
- Community 158
- Community 159
- Community 160
- Community 161
- Community 162
- Community 163
- Community 164
- Community 165
- Community 166
- Community 167
- Community 168
- Community 169
- Community 170
- Community 171
- Community 172
- Community 173
- Community 174
- Community 175
- Community 176
- Community 177
- Community 178
- Community 179
- Community 180
- Community 181
- Community 182
- Community 183
- Community 184
- Community 185
- Community 186
- Community 187
- Community 188
- Community 189
- Community 190
- Community 191
- Community 192
- Community 193
- Community 194
- Community 195
- Community 196
- Community 197
- Community 198
- Community 199
- Community 200
- Community 201
- Community 202
- Community 203
- Community 204
- Community 205
- Community 206
- Community 207
- Community 208
- Community 209
- Community 210
- Community 211
- Community 212
- Community 213
- Community 214
- Community 215
- Community 216
- Community 217
- Community 218
- Community 219
- Community 220
- Community 222
- Community 223
- Community 224
- Community 225
- Community 226
- Community 227
- Community 233
- Community 234
- Community 235

## God Nodes (most connected - your core abstractions)
1. `IAuditSphereDbContext` - 263 edges
2. `AuditSphereDbContext` - 137 edges
3. `ActorContext` - 129 edges
4. `CommandResult` - 127 edges
5. `OperationContextAdapter` - 102 edges
6. `net10.0` - 60 edges
7. `net10.0` - 56 edges
8. `DurableOperation` - 54 edges
9. `AuditSphereOps.Domain.Shared` - 52 edges
10. `AuditSphereOps.Application.Abstractions` - 46 edges

## Surprising Connections (you probably didn't know these)
- `ASH-02 Trial-balance dataset sealing` --references--> `Invariant: append-only audit evidence (DB triggers)`  [INFERRED]
  docs/execution/current-slice.md → README.md
- `ScriptedSink` --references--> `IPbcProviderSink`  [EXTRACTED]
  tests/AuditSphereOps.Domain.Tests/PbcTransferTests.cs → src/AuditSphereOps.Application/Documents/PbcDocumentTransferHandler.cs
- `OperationContextAdapter` --implements--> `IAuditSphereDbContext`  [EXTRACTED]
  tests/AuditSphereOps.Domain.Tests/AuthorizationDecisionTests.cs → src/AuditSphereOps.Application/Operations/Contracts.cs
- `Harness` --references--> `OperationRequest`  [EXTRACTED]
  tests/AuditSphereOps.Domain.Tests/DurableOutboxTests.cs → src/AuditSphereOps.Application/Operations/Contracts.cs
- `ProbeHandler` --references--> `OperationDefinition`  [EXTRACTED]
  tests/AuditSphereOps.Domain.Tests/DurableOutboxTests.cs → src/AuditSphereOps.Application/Operations/Contracts.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Controlled release safety chain** — src_auditsphereops_application_documents_documentsnapshotservice, src_auditsphereops_application_reviews_approvalservice, src_auditsphereops_application_completion_releaseservice, src_auditsphereops_application_completion_releasecheckpointservice, src_auditsphereops_infrastructure_providers_graphreleasecheckpointstore [EXTRACTED 0.90]
- **Trial-balance to financial-statement pipeline** — src_auditsphereops_application_accounting_trialbalanceimportservice, src_auditsphereops_application_accounting_adjustmentplanservice, src_auditsphereops_application_accounting_financialstatementservice, src_auditsphereops_application_accounting_financialstatementcalculator, src_auditsphereops_application_practice_ledgerservice [EXTRACTED 0.85]
- **Never-fake external effect gates (P1-P5,P7-P10)** — gate_external_effects, wp_p1, wp_p2, wp_p3, wp_p4, wp_p5, wp_p7, wp_p8, wp_p10 [INFERRED 0.80]

## Communities (236 total, 17 thin omitted)

### Community 0 - "Audit Planning & Durable Drafts"
Cohesion: 0.10
Nodes (29): Activity, ASH-05 Durable workpaper drafts, InputGeneration, PolicyGeneration, Spec §§19-23 Audit planning to findings, AuditPlanningService, AuditRiskResult, CreateAuditRiskRequest (+21 more)

### Community 1 - "Actor Context & Recovery Authz"
Cohesion: 0.15
Nodes (22): ActorContext, OperationProjection, OperationRecoveryService, RecoverySessionRequest, CancellationToken, Guid, IReadOnlyList, Task (+14 more)

### Community 2 - "Practice CRM Workflow"
Cohesion: 0.17
Nodes (15): CreateClientContactRequest, CreateLeadRequest, CreateOpportunityRequest, PracticeCrmService, ProposalResponseRequest, ReviseProposalRequest, CancellationToken, Guid (+7 more)

### Community 3 - "NuGet Package Locks"
Cohesion: 0.04
Nodes (47): type, dependencies, net10.0, contentHash, resolved, type, contentHash, resolved (+39 more)

### Community 4 - "EF DbContext & Command Requests"
Cohesion: 0.20
Nodes (14): IAuditSphereDbContext, DatabaseFacade, DbSet, BudgetActualSummary, CorrectTimeRequest, CreateTaskRequest, PracticeTimeService, RateCardDraftRequest (+6 more)

### Community 5 - "Financial Statement Calculation"
Cohesion: 0.10
Nodes (25): AdjustedCalculation, PackageLine, SourceBalance, FinancialStatementCalculator, PackageLine, IReadOnlyCollection, IReadOnlyDictionary, IReadOnlyList (+17 more)

### Community 6 - "Firm Ledger Service"
Cohesion: 0.19
Nodes (14): CreateFirmAccountRequest, CreateFirmJournalDraftRequest, CreateFirmPeriodRequest, LedgerService, ReverseFirmPostingRequest, CancellationToken, Guid, IReadOnlyList (+6 more)

### Community 7 - "Project Namespaces"
Cohesion: 0.18
Nodes (16): AuditSphereOps.Domain.Tests, AuditSphereOps.Infrastructure.Persistence, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Acceptance, AuditSphereOps.Domain.Accounting, AuditSphereOps.Domain.Completion, AuditSphereOps.Domain.Engagements, AuditSphereOps.Domain.Security (+8 more)

### Community 8 - "PBC Provider Transfer Sink"
Cohesion: 0.13
Nodes (17): PbcTransferPayload, IPbcProviderSink, PbcDocumentTransferHandler, PbcProviderReceipt, PbcTransferPayload, PbcTransferPlan, SimulationPbcProviderSink, CancellationToken (+9 more)

### Community 9 - "Trial Balance Engine"
Cohesion: 0.08
Nodes (24): ParsedTrialBalance, TbImportRow, TrialBalanceCalculator, AccountCode, Credit, Debit, IEnumerable, IReadOnlyDictionary (+16 more)

### Community 10 - "Billing Service"
Cohesion: 0.21
Nodes (13): AllocateReceiptRequest, BillingService, CreateBillingAccountRequest, CreateInvoiceDraftRequest, InvoiceBalance, InvoiceLineRequest, IssueCreditNoteRequest, RecordReceiptRequest (+5 more)

### Community 11 - ".NET Compiler Dependencies"
Cohesion: 0.07
Nodes (38): Humanizer.Core, Microsoft.Build.Framework, Microsoft.CodeAnalysis.Analyzers, Microsoft.CodeAnalysis.Common, Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.CSharp.Workspaces, Microsoft.CodeAnalysis.Workspaces.Common, Microsoft.CodeAnalysis.Workspaces.MSBuild (+30 more)

### Community 12 - ".NET Configuration Dependencies"
Cohesion: 0.07
Nodes (38): Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Configuration.CommandLine, Microsoft.Extensions.Configuration.FileExtensions, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.Configuration.UserSecrets, Microsoft.Extensions.Diagnostics, Microsoft.Extensions.FileProviders.Abstractions (+30 more)

### Community 13 - "PBC Upload Service"
Cohesion: 0.15
Nodes (16): ReadOnlySpan, CompletePbcUploadRequest, CreatePbcRequestRequest, PbcService, PbcStateChangeRequest, PbcUploadReceipt, RecordPbcUploadChunkRequest, StagedVerification (+8 more)

### Community 14 - ".NET DI/Logging Dependencies"
Cohesion: 0.08
Nodes (35): Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.Configuration.Binder, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Logging.Configuration, Microsoft.Extensions.Options, Microsoft.Extensions.Primitives (+27 more)

### Community 15 - "NuGet Package Locks"
Cohesion: 0.06
Nodes (33): type, dependencies, net10.0, contentHash, resolved, type, contentHash, resolved (+25 more)

### Community 16 - "NuGet Package Locks"
Cohesion: 0.06
Nodes (33): type, dependencies, net10.0, contentHash, resolved, type, contentHash, resolved (+25 more)

### Community 17 - "EFCore/Diagnostics Dependencies"
Cohesion: 0.06
Nodes (34): Microsoft.EntityFrameworkCore.Abstractions, Microsoft.EntityFrameworkCore.Analyzers, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Logging, Microsoft.Extensions.Logging.Abstractions, System.Diagnostics.EventLog, contentHash, dependencies (+26 more)

### Community 18 - "Community 18"
Cohesion: 0.13
Nodes (12): DbContext, EntityTypeBuilder, IDbContextFactory, IDesignTimeDbContextFactory, ModelBuilder, AuditSphereDbContext, DbSet, AuditSphereDbContextFactory (+4 more)

### Community 19 - "Community 19"
Cohesion: 0.09
Nodes (23): coverlet.collector, Microsoft.AspNetCore.Authentication.OpenIdConnect, Microsoft.EntityFrameworkCore.Design, Microsoft.Extensions.Hosting, Microsoft.NET.Test.Sdk, Npgsql, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http (+15 more)

### Community 20 - "Community 20"
Cohesion: 0.26
Nodes (11): TrialBalanceDatasetDto, TrialBalanceDatasetQuery, CancellationToken, Guid, Task, AuthorizationDecisionTests, Scope, CancellationToken (+3 more)

### Community 21 - "Community 21"
Cohesion: 0.22
Nodes (9): AuditScopeIntegrityTests, Fact, string, Task, PlanningFixture, Task, RecordsArchiveTests, Fact (+1 more)

### Community 22 - "Community 22"
Cohesion: 0.07
Nodes (27): Serilog, Serilog.Extensions.Logging, Serilog.Formatting.Compact, Serilog.Sinks.Console, Serilog.Sinks.Debug, Serilog.Sinks.File, contentHash, dependencies (+19 more)

### Community 23 - "Community 23"
Cohesion: 0.07
Nodes (27): Microsoft.Extensions.FileSystemGlobbing, Microsoft.Extensions.Primitives, contentHash, dependencies, resolved, type, contentHash, dependencies (+19 more)

### Community 24 - "Community 24"
Cohesion: 0.24
Nodes (6): Harness, DurableOutboxTests, Fact, InlineData, Task, Theory

### Community 25 - "Community 25"
Cohesion: 0.08
Nodes (25): AutosaveAfterDelayAsync, DiscardDraftAsync, DisposeAsync, FlushPendingSaveAsync, MarkDraftChanged, OnConclusionInputAsync, OnInitializedAsync, OnWorkInputAsync (+17 more)

### Community 26 - "Community 26"
Cohesion: 0.09
Nodes (26): dependencies, type, dependencies, type, dependencies, type, AuditSphereOps.Application, AuditSphereOps.Domain (+18 more)

### Community 27 - "Community 27"
Cohesion: 0.15
Nodes (13): AuditSphereOps.Domain.Reviews, AuditSphereOps.Application.Audit, AuditSphereOps.Application.Completion, AuditSphereOps.Application.Reviews, AuditSphereOps.Worker, AuditSphereOps.Domain.Audit, AuditSphereOps.Application.Abstractions, IHealthChecksBuilder (+5 more)

### Community 28 - "Community 28"
Cohesion: 0.27
Nodes (10): NpgsqlCommand, WorkerOptions, IEnumerable, PostgresOperationStore, CancellationToken, DateTimeOffset, Guid, IReadOnlyList (+2 more)

### Community 29 - "Community 29"
Cohesion: 0.18
Nodes (6): IEnumerable, AuditPlanningTests, Fact, InlineData, Task, Theory

### Community 30 - "Community 30"
Cohesion: 0.16
Nodes (22): AuditProcedure, AuditRisk, Finding, FindingStatuses, MaterialityAssessment, MaterialityStatuses, PopulationStatuses, PopulationVersion (+14 more)

### Community 31 - "Community 31"
Cohesion: 0.09
Nodes (25): System.Composition.AttributedModel, System.Composition.Convention, System.Composition.Hosting, System.Composition.Runtime, System.Composition.TypedParts, System.Composition, System.Composition.Convention, System.Composition.Hosting (+17 more)

### Community 32 - "Community 32"
Cohesion: 0.12
Nodes (25): Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Configuration.CommandLine, Microsoft.Extensions.Configuration.FileExtensions, Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.Configuration.UserSecrets, Microsoft.Extensions.Diagnostics, Microsoft.Extensions.FileProviders.Abstractions (+17 more)

### Community 33 - "Community 33"
Cohesion: 0.25
Nodes (10): Scope, AdjustmentBridgeTests, Scope, Users, Fact, Guid, IReadOnlyDictionary, string (+2 more)

### Community 34 - "Community 34"
Cohesion: 0.21
Nodes (11): DurableOperationRegistry, IOperationHandler, IOperationStore, OperationDefinition, CancellationToken, Guid, IReadOnlyDictionary, IReadOnlyList (+3 more)

### Community 35 - "Community 35"
Cohesion: 0.10
Nodes (24): dependencies, type, dependencies, type, AuditSphereOps.Application, AuditSphereOps.Domain, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational (+16 more)

### Community 36 - "Community 36"
Cohesion: 0.18
Nodes (14): AdjustmentPlanService, FinalizedPlan, PlanLineInput, SourceReconciliationService, CancellationToken, Guid, IReadOnlyList, string (+6 more)

### Community 37 - "Community 37"
Cohesion: 0.09
Nodes (23): Microsoft.Extensions.Configuration.Binder, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options, contentHash, dependencies, resolved, type, contentHash (+15 more)

### Community 38 - "Community 38"
Cohesion: 0.21
Nodes (10): PbcTransferTests, ScriptedSink, TransferContext, CancellationToken, Fact, Guid, List, ScriptedSink (+2 more)

### Community 39 - "Community 39"
Cohesion: 0.09
Nodes (21): AuditSphereOps.Application.Completion, AuditSphereOps.Application.Documents, AuditSphereOps.Domain.Documents, AuditSphereOps.Web.Authentication, FinancialPackageEntity, Microsoft.AspNetCore.Components, Microsoft.AspNetCore.Components.Routing, Microsoft.AspNetCore.Components.Web (+13 more)

### Community 40 - "Community 40"
Cohesion: 0.26
Nodes (10): Code, Message, CreateReleaseCandidateRequest, IssueReleaseRequest, ReleaseService, CancellationToken, Guid, IDbContextTransaction (+2 more)

### Community 41 - "Community 41"
Cohesion: 0.16
Nodes (11): AppUser, RoleGrant, DateTimeOffset, Guid, BillingTests, Fixture, Fact, Guid (+3 more)

### Community 42 - "Community 42"
Cohesion: 0.09
Nodes (21): CreateFindingAsync, CreateMaterialityAsync, CreatePopulationAsync, CreateRiskAsync, OnInitializedAsync, AuditRisk, AuditSphereDbContext, AuditSphereOps.Application.Audit (+13 more)

### Community 43 - "Community 43"
Cohesion: 0.09
Nodes (22): Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions, contentHash, dependencies, resolved, type, contentHash, dependencies (+14 more)

### Community 44 - "Community 44"
Cohesion: 0.14
Nodes (13): ActivitySource, CancellationTokenSource, Counter, Histogram, KeyValuePair, Meter, AuditDiagnostics, string (+5 more)

### Community 45 - "Community 45"
Cohesion: 0.18
Nodes (9): ASH-02 Trial-balance dataset sealing, ASH-03 Financial calculation boundary, AuditSphereOps.Domain.Shared, AuditSphereOps.Application.Security, AuditSphereOps.Application.Accounting, Invariant: scope-checked authorization (firm→client→dataset), Spec §8 Authorization & role matrix, CashFlowLineInput (+1 more)

### Community 46 - "Community 46"
Cohesion: 0.19
Nodes (9): AuditSphereOps.Application.Acceptance, AuditSphereOps.Domain.Documents, AuditSphereOps.Infrastructure.Providers, AuditSphereOps.Application.Operations, AuditSphereOps.Application.Documents, Gate: ExternalEffects.Enabled=false (never fake providers), Gate: no autonomous professional conclusions, Spec §15 Client portal, PBC & controlled uploads (+1 more)

### Community 47 - "Community 47"
Cohesion: 0.26
Nodes (11): Handler, StagedUpload, Store, Fixture, Fixture, PbcSeed, StagedUpload, TransferHarness (+3 more)

### Community 48 - "Community 48"
Cohesion: 0.23
Nodes (19): AccountingPackageStates, AdjustedTrialBalanceRow, AdjustedTrialBalanceSnapshot, AdjustmentJournal, AdjustmentLine, FinancialPackage, FinancialPackageCashFlowLine, FinancialPackageDisclosure (+11 more)

### Community 49 - "Community 49"
Cohesion: 0.10
Nodes (21): Microsoft.EntityFrameworkCore.Abstractions, Microsoft.EntityFrameworkCore.Analyzers, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Logging, contentHash, dependencies, contentHash, dependencies (+13 more)

### Community 50 - "Community 50"
Cohesion: 0.21
Nodes (19): Archive, ClientSafetyState, EqrCase, FirmSafetyState, OperationAttempt, OperationAuthority, OperationEvent, OperationMode (+11 more)

### Community 51 - "Community 51"
Cohesion: 0.10
Nodes (20): Microsoft.Extensions.Configuration.EnvironmentVariables, Microsoft.Extensions.Diagnostics.Abstractions, Microsoft.Extensions.Logging.Configuration, Microsoft.Extensions.Options.ConfigurationExtensions, OpenTelemetry.Api.ProviderBuilderExtensions, contentHash, dependencies, resolved (+12 more)

### Community 52 - "Community 52"
Cohesion: 0.16
Nodes (12): IClock, IReleaseCheckpointStore, IUnitOfWork, CancellationToken, DateTimeOffset, Func, Task, RecordReleaseCheckpointRequest (+4 more)

### Community 53 - "Community 53"
Cohesion: 0.20
Nodes (10): CheckpointReceipt, LocalAppendOnlyCheckpointStore, CancellationToken, Task, GraphReleaseCheckpointStore, CancellationToken, Task, ProviderBoundaryTests (+2 more)

### Community 54 - "Community 54"
Cohesion: 0.21
Nodes (9): Fixture, ReleaseEvidenceTests, ClientId, EngagementId, Fact, FirmId, Fixture, Guid (+1 more)

### Community 55 - "Community 55"
Cohesion: 0.11
Nodes (19): Microsoft.Extensions.Configuration.EnvironmentVariables, Microsoft.Extensions.Diagnostics.Abstractions, Microsoft.Extensions.Options.ConfigurationExtensions, OpenTelemetry.Api.ProviderBuilderExtensions, contentHash, dependencies, resolved, type (+11 more)

### Community 56 - "Community 56"
Cohesion: 0.11
Nodes (18): OpenTelemetry.Api.ProviderBuilderExtensions, OpenTelemetry, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.Http, contentHash, dependencies, contentHash, dependencies (+10 more)

### Community 57 - "Community 57"
Cohesion: 0.14
Nodes (18): dependencies, type, dependencies, type, AuditSphereOps.Application, AuditSphereOps.Domain, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational (+10 more)

### Community 58 - "Community 58"
Cohesion: 0.20
Nodes (11): CaptureDocumentSnapshotRequest, DocumentSnapshotReceipt, DocumentSnapshotService, CancellationToken, long, Task, DocumentSnapshotTests, Fixture (+3 more)

### Community 59 - "Community 59"
Cohesion: 0.34
Nodes (7): OperationRecoveryRequest, OperationRecoveryTests, Fact, Guid, Task, ScriptedSink, TransferHarness

### Community 60 - "Community 60"
Cohesion: 0.12
Nodes (17): Microsoft.IdentityModel.JsonWebTokens, Microsoft.IdentityModel.Tokens, contentHash, dependencies, resolved, type, contentHash, dependencies (+9 more)

### Community 61 - "Community 61"
Cohesion: 0.12
Nodes (15): GateRow, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Accounting, AuditSphereOps.Domain.Completion, AuditSphereOps.Domain.Engagements, AuditSphereOps.Domain.Reviews, AuditSphereOps.Domain.Security (+7 more)

### Community 62 - "Community 62"
Cohesion: 0.18
Nodes (9): ParsedCsvFile, TrialBalanceCsvImporter, int, List, string, TrialBalanceCsvParserTests, InlineData, Theory (+1 more)

### Community 63 - "Community 63"
Cohesion: 0.12
Nodes (15): CompleteUploadAsync, HeadingId, LoadAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Operations, AuditSphereOps.Domain.Completion, CurrentActorResolver (+7 more)

### Community 64 - "Community 64"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 65 - "Community 65"
Cohesion: 0.30
Nodes (7): OutboxMigrationTests, Fact, InlineData, string, Task, Theory, Trait

### Community 66 - "Community 66"
Cohesion: 0.18
Nodes (10): DbContextOptions, ValueTask, PgTestSchema, string, ValueTask, TrialBalanceWorkerTests, Fact, Guid (+2 more)

### Community 67 - "Community 67"
Cohesion: 0.14
Nodes (14): AGENTS.md (agent safety pointer), docs/execution/current-slice.md (verified local state), docs/execution/pending-tasks.md (P0-P10 dependency story), README (project overview), docs/execution/status.json (live progress pointer), Audit Workflow Gap-Closure user stories (ASH backlog), Invariant: composite scope FKs, ON DELETE RESTRICT, Invariant: money is numeric(19,6) (+6 more)

### Community 68 - "Community 68"
Cohesion: 0.35
Nodes (8): ApprovalApplicabilityResult, ApprovalService, CreateApprovalRequest, CancellationToken, Guid, string, Task, Workpaper

### Community 69 - "Community 69"
Cohesion: 0.28
Nodes (13): ArchiveManifest, ArchiveManifestEntry, ArchiveStates, ArchiveStructuredExport, LegalHold, ProtectionAttestation, RecordsAction, RecordsActionEvidence (+5 more)

### Community 70 - "Community 70"
Cohesion: 0.13
Nodes (14): ExecuteClosePeriodAsync, LoadFinanceDataAsync, OnInitializedAsync, PromptClosePeriod, AuditSphereDbContext, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security (+6 more)

### Community 71 - "Community 71"
Cohesion: 0.13
Nodes (14): OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Audit, AuditSphereOps.Domain.Audit, AuditSphereOps.Infrastructure.Persistence, CurrentActorResolver, FindingRecord, IDbContextFactory<AuditSphereDbContext> (+6 more)

### Community 72 - "Community 72"
Cohesion: 0.13
Nodes (14): ApproveInvoiceAsync, LoadInvoiceAsync, OnInitializedAsync, PostInvoiceAsync, AuditSphereDbContext, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security (+6 more)

### Community 73 - "Community 73"
Cohesion: 0.13
Nodes (14): ApproveTimeAsync, LoadDataAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security, CurrentActorResolver (+6 more)

### Community 74 - "Community 74"
Cohesion: 0.22
Nodes (9): Fixture, ReleaseTests, ClientId, EngagementId, Fact, FirmId, Fixture, Guid (+1 more)

### Community 75 - "Community 75"
Cohesion: 0.14
Nodes (13): AuditSphereOps.Domain.Acceptance, ClearanceRow, SectionRow, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security, AuditSphereOps.Infrastructure.Persistence (+5 more)

### Community 76 - "Community 76"
Cohesion: 0.23
Nodes (6): IAsyncDisposable, ProbeHandler, IAuditSphereDbContextFactory, OperationContextFactory, Harness, Guid

### Community 77 - "Community 77"
Cohesion: 0.29
Nodes (13): FirmAccount, FirmJournal, FirmJournalLine, FirmPeriod, FirmPosting, FirmPostingLine, LedgerPostingReceipt, LedgerSourceLink (+5 more)

### Community 78 - "Community 78"
Cohesion: 0.14
Nodes (13): type, dependencies, net10.0, contentHash, resolved, type, contentHash, resolved (+5 more)

### Community 79 - "Community 79"
Cohesion: 0.14
Nodes (13): CreateContactAsync, LoadClientAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Engagements, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security (+5 more)

### Community 80 - "Community 80"
Cohesion: 0.14
Nodes (13): ClearPoint, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Reviews, AuditSphereOps.Domain.Security, AuditSphereOps.Infrastructure.Persistence, CurrentActorResolver, IDbContextFactory<AuditSphereDbContext> (+5 more)

### Community 81 - "Community 81"
Cohesion: 0.14
Nodes (14): Microsoft.Extensions.Hosting.Abstractions, OpenTelemetry, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting, contentHash, dependencies, requested, resolved (+6 more)

### Community 82 - "Community 82"
Cohesion: 0.14
Nodes (14): Microsoft.Extensions.Hosting.Abstractions, OpenTelemetry, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting, contentHash, dependencies, requested, resolved (+6 more)

### Community 83 - "Community 83"
Cohesion: 0.35
Nodes (5): Fixture, PracticeTimeTests, Fact, Guid, Task

### Community 84 - "Community 84"
Cohesion: 0.32
Nodes (12): BillingAccount, BillingSourceAllocation, BillingStates, CreditNote, FirmFinanceProfile, Invoice, InvoiceLine, Receipt (+4 more)

### Community 85 - "Community 85"
Cohesion: 0.17
Nodes (13): dependencies, type, AuditSphereOps.Domain, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational, Npgsql, auditsphereops.application, Npgsql.EntityFrameworkCore.PostgreSQL (+5 more)

### Community 86 - "Community 86"
Cohesion: 0.15
Nodes (12): OnInitializedAsync, ArchiveManifestEntry, AuditSphereDbContext, AuditSphereOps.Application.Abstractions, AuditSphereOps.Application.Security, AuditSphereOps.Domain.Completion, AuditSphereOps.Domain.Records, AuditSphereOps.Infrastructure.Persistence (+4 more)

### Community 87 - "Community 87"
Cohesion: 0.15
Nodes (13): OpenTelemetry, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting, contentHash, dependencies, requested, resolved, type (+5 more)

### Community 88 - "Community 88"
Cohesion: 0.22
Nodes (9): ApprovalTests, Fixture, Fact, Task, ClientId, EngagementId, FirmId, Guid (+1 more)

### Community 89 - "Community 89"
Cohesion: 0.17
Nodes (11): CandidateRow, PackageRow, CandidateRow, OnInitializedAsync, PackageRow, PortfolioMetrics, AuditSphereDbContext, CurrentActorResolver (+3 more)

### Community 90 - "Community 90"
Cohesion: 0.17
Nodes (8): ClaimsPrincipal, AuditSphereOps.Web.Authentication, CurrentActorResolver, CancellationToken, Task, TrustedActorResolver, CancellationToken, Task

### Community 91 - "Community 91"
Cohesion: 0.18
Nodes (10): AuditSphereOps.Domain.Records, AuditSphereOps.Application.Records, Spec §25 Purview retention, archive & disposal, ObserveLegalHoldRequest, ObserveRecordsActionRequest, RecordsProfileResult, ReleaseLegalHoldRequest, RequestLegalHoldRequest (+2 more)

### Community 92 - "Community 92"
Cohesion: 0.20
Nodes (10): Restore drill runbook, Exception, Invariant: exactly-once outbox & RESULT_UNCERTAIN, Spec §29 Durable jobs, transactions & event contracts, OperationBlockedException, OperationOwnershipLostException, SafeRetryException, TimeSpan (+2 more)

### Community 93 - "Community 93"
Cohesion: 0.17
Nodes (11): OperationProjection, LiftQuarantineAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Operations, AuditSphereOps.Domain.Completion, CurrentActorResolver, IDbContextFactory<AuditSphereDbContext> (+3 more)

### Community 94 - "Community 94"
Cohesion: 0.32
Nodes (5): CancellationToken, Task, CoreEntityCatalogTests, Fact, Task

### Community 95 - "Community 95"
Cohesion: 0.30
Nodes (9): AdjustmentJournalService, AccountCode, CancellationToken, Credit, Debit, Guid, IReadOnlyList, string (+1 more)

### Community 96 - "Community 96"
Cohesion: 0.17
Nodes (12): Microsoft.Extensions.DependencyInjection.Abstractions, contentHash, dependencies, resolved, type, dependencies, contentHash, dependencies (+4 more)

### Community 97 - "Community 97"
Cohesion: 0.18
Nodes (12): Microsoft.EntityFrameworkCore.Abstractions, Microsoft.EntityFrameworkCore.Analyzers, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Logging, contentHash, dependencies, dependencies (+4 more)

### Community 98 - "Community 98"
Cohesion: 0.18
Nodes (12): Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Primitives, contentHash, dependencies, resolved, type, dependencies (+4 more)

### Community 99 - "Community 99"
Cohesion: 0.17
Nodes (11): CreateLeadAsync, LoadLeadsAsync, OnInitializedAsync, QualifyLeadAsync, AuditSphereDbContext, AuditSphereOps.Application.Practice, AuditSphereOps.Domain.Practice, CurrentActorResolver (+3 more)

### Community 100 - "Community 100"
Cohesion: 0.17
Nodes (12): OpenTelemetry.Api, OpenTelemetry.Api.ProviderBuilderExtensions, OpenTelemetry.Instrumentation.Runtime, contentHash, dependencies, resolved, type, contentHash (+4 more)

### Community 101 - "Community 101"
Cohesion: 0.17
Nodes (12): Serilog.Extensions.Hosting, Serilog.Formatting.Compact, Serilog.Settings.Configuration, Serilog.Sinks.Console, Serilog.Sinks.Debug, Serilog.Sinks.File, Serilog.AspNetCore, contentHash (+4 more)

### Community 102 - "Community 102"
Cohesion: 0.17
Nodes (12): OpenTelemetry.Api, OpenTelemetry.Api.ProviderBuilderExtensions, OpenTelemetry.Instrumentation.Runtime, contentHash, dependencies, resolved, type, contentHash (+4 more)

### Community 103 - "Community 103"
Cohesion: 0.17
Nodes (12): OpenTelemetry.Api, OpenTelemetry.Api.ProviderBuilderExtensions, OpenTelemetry.Instrumentation.Runtime, contentHash, dependencies, resolved, type, contentHash (+4 more)

### Community 104 - "Community 104"
Cohesion: 0.17
Nodes (12): xunit.extensibility.core, xunit.extensibility.execution, xunit.core, xunit.extensibility.execution, contentHash, dependencies, resolved, type (+4 more)

### Community 105 - "Community 105"
Cohesion: 0.18
Nodes (10): IJSRuntime, OnInitializedAsync, PbcFileMetadata, AuditSphereDbContext, CurrentActorResolver, IDbContextFactory<AuditSphereDbContext>, PageTitle, PbcUploadIntent (+2 more)

### Community 106 - "Community 106"
Cohesion: 0.18
Nodes (11): Microsoft.Extensions.Primitives, contentHash, dependencies, resolved, type, contentHash, dependencies, resolved (+3 more)

### Community 107 - "Community 107"
Cohesion: 0.31
Nodes (10): BudgetLine, EngagementBudget, PracticeTimeStates, RateCardVersion, TimeEntry, WorkTask, DateOnly, DateTimeOffset (+2 more)

### Community 108 - "Community 108"
Cohesion: 0.18
Nodes (10): OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Audit, AuditSphereOps.Infrastructure.Persistence, CurrentActorResolver, EvidenceLink, IDbContextFactory<AuditSphereDbContext>, Microsoft.EntityFrameworkCore (+2 more)

### Community 109 - "Community 109"
Cohesion: 0.18
Nodes (10): LoadEngagementAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Engagements, AuditSphereOps.Domain.Practice, AuditSphereOps.Domain.Security, CurrentActorResolver, EngagementHold (+2 more)

### Community 110 - "Community 110"
Cohesion: 0.18
Nodes (10): OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Application.Accounting, CurrentActorResolver, FinancialPackageCashFlowLine, FinancialPackageDisclosure, FinancialPackageLine, FinancialPackageValidation (+2 more)

### Community 111 - "Community 111"
Cohesion: 0.18
Nodes (10): LoadJournalAsync, OnInitializedAsync, PostJournalAsync, AdjustmentLine, AuditSphereDbContext, AuditSphereOps.Application.Accounting, AuditSphereOps.Domain.Accounting, CurrentActorResolver (+2 more)

### Community 112 - "Community 112"
Cohesion: 0.38
Nodes (9): QuestionnaireSeed, Guid, AcceptanceDecision, EvaluationResponse, QuestionDefinition, QuestionnaireTemplate, SpecialistClearance, DateTimeOffset (+1 more)

### Community 113 - "Community 113"
Cohesion: 0.36
Nodes (4): TrialBalanceValidationHandler, CancellationToken, string, Task

### Community 114 - "Community 114"
Cohesion: 0.40
Nodes (5): ReleaseCheckpointHandler, ReleaseCheckpointPayload, CancellationToken, string, Task

### Community 115 - "Community 115"
Cohesion: 0.31
Nodes (8): PbcRequest, PbcStates, PbcUploadChunk, PbcUploadIntent, PbcUploadStates, DateTimeOffset, Guid, string

### Community 116 - "Community 116"
Cohesion: 0.20
Nodes (9): type, dependencies, net10.0, contentHash, resolved, type, auditsphereops.domain, Microsoft.Extensions.Primitives (+1 more)

### Community 117 - "Community 117"
Cohesion: 0.22
Nodes (10): Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, dependencies, contentHash, dependencies, resolved (+2 more)

### Community 118 - "Community 118"
Cohesion: 0.47
Nodes (9): DocumentReference, DocumentSnapshot, EvidenceLink, IntegrationCapability, RepositoryBinding, SourceReceipt, SyncCursor, DateTimeOffset (+1 more)

### Community 119 - "Community 119"
Cohesion: 0.38
Nodes (9): ClientContact, CrmStates, Lead, Opportunity, PracticeClient, Proposal, DateTimeOffset, Guid (+1 more)

### Community 120 - "Community 120"
Cohesion: 0.20
Nodes (9): LoadAdminDataAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Security, CurrentActorResolver, IConfiguration, IDbContextFactory<AuditSphereDbContext>, PageTitle (+1 more)

### Community 121 - "Community 121"
Cohesion: 0.20
Nodes (9): ApproveAndSend, OnInitializedAsync, AuthenticationStateProvider, Microsoft.AspNetCore.Components.Authorization, NavigationManager, PageTitle, RecordResponse, SubmitForReview (+1 more)

### Community 122 - "Community 122"
Cohesion: 0.44
Nodes (5): CatalogFixture, CatalogFixture, RouteCatalogTests, Fact, Task

### Community 123 - "Community 123"
Cohesion: 0.33
Nodes (7): HealthCheckContext, HealthCheckResult, IHealthCheck, CancellationToken, Task, MigrationCheck, NpgsqlCheck

### Community 124 - "Community 124"
Cohesion: 0.50
Nodes (4): OperationResult, TaskCompletionSource, ProbeHandler, CancellationToken

### Community 125 - "Community 125"
Cohesion: 0.22
Nodes (8): IssueAsync, OnInitializedAsync, AuditSphereDbContext, AuditSphereOps.Domain.Records, CurrentActorResolver, Guid, IDbContextFactory<AuditSphereDbContext>, PageTitle

### Community 126 - "Community 126"
Cohesion: 0.56
Nodes (4): AccountingIntegrityTests, Fact, Task, Trait

### Community 127 - "Community 127"
Cohesion: 0.22
Nodes (9): xunit.analyzers, xunit.assert, xunit.core, xunit, contentHash, dependencies, requested, resolved (+1 more)

### Community 128 - "Community 128"
Cohesion: 0.25
Nodes (7): FocusOnNavigate, Found, LayoutView, NotFound, Router, RouteView, PageTitle

### Community 129 - "Community 129"
Cohesion: 0.32
Nodes (6): TrialBalanceDiscovery, IPendingOperationDiscovery, PbcTransferDiscovery, CancellationToken, int, Task

### Community 130 - "Community 130"
Cohesion: 0.29
Nodes (8): Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Abstractions, Microsoft.EntityFrameworkCore.Analyzers, Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Logging, dependencies, dependencies

### Community 131 - "Community 131"
Cohesion: 0.39
Nodes (7): Approval, ApprovalApplicability, ApprovalStates, ReviewPoint, DateTimeOffset, Guid, string

### Community 132 - "Community 132"
Cohesion: 0.50
Nodes (4): GraphPbcProviderSink, CancellationToken, Guid, Task

### Community 133 - "Community 133"
Cohesion: 0.25
Nodes (7): OnInitializedAsync, AuditSphereDbContext, CurrentActorResolver, Guid, IDbContextFactory<AuditSphereDbContext>, PageTitle, PbcRequest

### Community 134 - "Community 134"
Cohesion: 0.25
Nodes (7): ApproveAsync, OnInitializedAsync, AuditSphereDbContext, CurrentActorResolver, IDbContextFactory<AuditSphereDbContext>, MappingAllocation, PageTitle

### Community 135 - "Community 135"
Cohesion: 0.25
Nodes (8): Microsoft.EntityFrameworkCore.Abstractions, Microsoft.EntityFrameworkCore.Analyzers, contentHash, dependencies, requested, resolved, type, Microsoft.EntityFrameworkCore

### Community 136 - "Community 136"
Cohesion: 0.25
Nodes (8): Microsoft.IdentityModel.Protocols, System.IdentityModel.Tokens.Jwt, contentHash, dependencies, requested, resolved, type, Microsoft.IdentityModel.Protocols.OpenIdConnect

### Community 137 - "Community 137"
Cohesion: 0.25
Nodes (7): commandName, dotnetRunMessages, environmentVariables, DOTNET_ENVIRONMENT, profiles, AuditSphereOps.Worker, $schema

### Community 138 - "Community 138"
Cohesion: 0.25
Nodes (8): Microsoft.CodeCoverage, Microsoft.TestPlatform.TestHost, contentHash, dependencies, requested, resolved, type, Microsoft.NET.Test.Sdk

### Community 139 - "Community 139"
Cohesion: 0.29
Nodes (5): ASH-01 Telemetry composition, AuditSphereOps.Application.Diagnostics, Operational telemetry (OpenTelemetry), Spec §43 Blazor UX, routing & document transfers, Blazor Interactive Server

### Community 140 - "Community 140"
Cohesion: 0.29
Nodes (7): ASH-06 Connection-capacity baseline, GitHub Actions CI (build-test-ready), PostgreSQL connection-capacity decision, Spec §45 Local setup, CI/CD & operations, .NET 10 (SDK 10.0.300 pinned), PostgreSQL 18.6 (loopback :5433 dev), P9 Independent review & protected merge governance

### Community 141 - "Community 141"
Cohesion: 0.57
Nodes (4): BackgroundService, Worker, CancellationToken, Task

### Community 142 - "Community 142"
Cohesion: 0.33
Nodes (5): DbUpdateException, TrialBalanceImportService, CancellationToken, Guid, Task

### Community 144 - "Community 144"
Cohesion: 0.33
Nodes (6): AdjustmentPlanLine, JournalSourceReconciliation, ReflectionStates, DateTimeOffset, Guid, string

### Community 145 - "Community 145"
Cohesion: 0.48
Nodes (6): Engagement, EngagementAssignment, EngagementHold, DateOnly, DateTimeOffset, Guid

### Community 146 - "Community 146"
Cohesion: 0.29
Nodes (7): Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options, contentHash, dependencies, resolved, type, Microsoft.Extensions.Logging

### Community 147 - "Community 147"
Cohesion: 0.29
Nodes (7): Microsoft.Extensions.Logging.Abstractions, Npgsql, contentHash, dependencies, requested, resolved, type

### Community 148 - "Community 148"
Cohesion: 0.29
Nodes (6): OnInitializedAsync, AuthenticationStateProvider, Microsoft.AspNetCore.Components.Authorization, NavigationManager, PageTitle, RecordDecision

### Community 149 - "Community 149"
Cohesion: 0.29
Nodes (7): Microsoft.IdentityModel.Protocols.OpenIdConnect, contentHash, dependencies, requested, resolved, type, Microsoft.AspNetCore.Authentication.OpenIdConnect

### Community 150 - "Community 150"
Cohesion: 0.33
Nodes (6): Microsoft.Extensions.DependencyModel, Serilog.Settings.Configuration, contentHash, dependencies, resolved, type

### Community 151 - "Community 151"
Cohesion: 0.33
Nodes (6): Microsoft.IdentityModel.Abstractions, contentHash, dependencies, resolved, type, Microsoft.IdentityModel.Logging

### Community 152 - "Community 152"
Cohesion: 0.33
Nodes (6): Microsoft.IdentityModel.Logging, contentHash, dependencies, resolved, type, Microsoft.IdentityModel.Tokens

### Community 153 - "Community 153"
Cohesion: 0.33
Nodes (6): Serilog.Extensions.Logging, Serilog.Extensions.Hosting, contentHash, dependencies, resolved, type

### Community 154 - "Community 154"
Cohesion: 0.33
Nodes (6): System.CodeDom, contentHash, dependencies, resolved, type, Mono.TextTemplating

### Community 155 - "Community 155"
Cohesion: 0.33
Nodes (6): Microsoft.TestPlatform.ObjectModel, contentHash, dependencies, resolved, type, Microsoft.TestPlatform.TestHost

### Community 156 - "Community 156"
Cohesion: 0.33
Nodes (6): xunit.abstractions, xunit.extensibility.core, contentHash, dependencies, resolved, type

### Community 157 - "Community 157"
Cohesion: 0.33
Nodes (5): diagnosticMessages, maxParallelThreads, parallelizeAssembly, parallelizeTestCollections, $schema

### Community 158 - "Community 158"
Cohesion: 0.60
Nodes (3): Get-PgDatabaseNames(), Get-PgServerVersion(), Invoke-PsqlQuery()

### Community 159 - "Community 159"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.EntityFrameworkCore

### Community 160 - "Community 160"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.EntityFrameworkCore.Relational

### Community 161 - "Community 161"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.EntityFrameworkCore.Relational

### Community 162 - "Community 162"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.DependencyInjection

### Community 163 - "Community 163"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.AspNetCore.App.Internal.Assets

### Community 164 - "Community 164"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.EntityFrameworkCore.Design

### Community 165 - "Community 165"
Cohesion: 0.40
Nodes (5): Npgsql, contentHash, requested, resolved, type

### Community 166 - "Community 166"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.Extensions.Hosting

### Community 167 - "Community 167"
Cohesion: 0.40
Nodes (5): Npgsql, contentHash, requested, resolved, type

### Community 168 - "Community 168"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, coverlet.collector

### Community 169 - "Community 169"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.Caching.Abstractions

### Community 170 - "Community 170"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.Configuration.Abstractions

### Community 171 - "Community 171"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.Configuration

### Community 172 - "Community 172"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.DependencyInjection

### Community 173 - "Community 173"
Cohesion: 0.40
Nodes (5): contentHash, dependencies, resolved, type, Microsoft.Extensions.FileProviders.Abstractions

### Community 174 - "Community 174"
Cohesion: 0.40
Nodes (5): contentHash, requested, resolved, type, Microsoft.Extensions.Hosting

### Community 175 - "Community 175"
Cohesion: 0.40
Nodes (5): Npgsql, contentHash, requested, resolved, type

### Community 176 - "Community 176"
Cohesion: 0.50
Nodes (3): Client, PlanningSeed, Guid

### Community 178 - "Community 178"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Abstractions

### Community 179 - "Community 179"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Analyzers

### Community 180 - "Community 180"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Caching.Memory

### Community 181 - "Community 181"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.DependencyInjection.Abstractions

### Community 182 - "Community 182"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Logging.Abstractions

### Community 183 - "Community 183"
Cohesion: 0.50
Nodes (3): dependencies, net10.0, version

### Community 184 - "Community 184"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Abstractions

### Community 185 - "Community 185"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Caching.Memory

### Community 186 - "Community 186"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.Abstractions

### Community 187 - "Community 187"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.DependencyInjection.Abstractions

### Community 188 - "Community 188"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Logging.Abstractions

### Community 189 - "Community 189"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Options

### Community 190 - "Community 190"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Humanizer.Core

### Community 191 - "Community 191"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Build.Framework

### Community 192 - "Community 192"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.CodeAnalysis.Analyzers

### Community 193 - "Community 193"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Analyzers

### Community 194 - "Community 194"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.DependencyModel

### Community 195 - "Community 195"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.IdentityModel.Abstractions

### Community 196 - "Community 196"
Cohesion: 0.50
Nodes (4): Serilog, contentHash, resolved, type

### Community 197 - "Community 197"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Abstractions

### Community 198 - "Community 198"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.FileExtensions

### Community 199 - "Community 199"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.Json

### Community 200 - "Community 200"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.UserSecrets

### Community 201 - "Community 201"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Hosting.Abstractions

### Community 202 - "Community 202"
Cohesion: 0.50
Nodes (4): System.Diagnostics.EventLog, contentHash, resolved, type

### Community 203 - "Community 203"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.CodeCoverage

### Community 204 - "Community 204"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.EntityFrameworkCore.Analyzers

### Community 205 - "Community 205"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Caching.Memory

### Community 206 - "Community 206"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.CommandLine

### Community 207 - "Community 207"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Configuration.UserSecrets

### Community 208 - "Community 208"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.FileProviders.Physical

### Community 209 - "Community 209"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.FileSystemGlobbing

### Community 210 - "Community 210"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Logging.Console

### Community 211 - "Community 211"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Logging

### Community 212 - "Community 212"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Logging.EventLog

### Community 213 - "Community 213"
Cohesion: 0.50
Nodes (4): contentHash, resolved, type, Microsoft.Extensions.Options.ConfigurationExtensions

### Community 214 - "Community 214"
Cohesion: 0.50
Nodes (4): xunit.analyzers, contentHash, resolved, type

## Knowledge Gaps
- **1129 isolated node(s):** `restore-drill.sh script`, `verify-tenant.sh script`, `PackageLine`, `CashFlowLineInput`, `DisclosureInput` (+1124 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **17 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `IAuditSphereDbContext` connect `EF DbContext & Command Requests` to `Audit Planning & Durable Drafts`, `Actor Context & Recovery Authz`, `Practice CRM Workflow`, `Community 131`, `Financial Statement Calculation`, `Firm Ledger Service`, `PBC Provider Transfer Sink`, `Billing Service`, `PBC Upload Service`, `Community 142`, `Community 144`, `Community 145`, `Community 18`, `Community 20`, `Community 28`, `Community 30`, `Community 34`, `Community 36`, `Community 40`, `Community 41`, `Community 48`, `Community 50`, `Community 52`, `Community 58`, `Community 59`, `Community 68`, `Community 69`, `Community 76`, `Community 77`, `Community 84`, `Community 92`, `Community 94`, `Community 95`, `Community 107`, `Community 112`, `Community 113`, `Community 114`, `Community 115`, `Community 118`, `Community 119`, `Community 124`?**
  _High betweenness centrality (0.087) - this node is a cross-community bridge._
- **Why does `ActorContext` connect `Actor Context & Recovery Authz` to `Audit Planning & Durable Drafts`, `Practice CRM Workflow`, `EF DbContext & Command Requests`, `Financial Statement Calculation`, `Firm Ledger Service`, `Billing Service`, `PBC Upload Service`, `Community 142`, `Community 20`, `Community 33`, `Community 36`, `Community 40`, `Community 41`, `Community 45`, `Community 47`, `Community 52`, `Community 54`, `Community 58`, `Community 59`, `Community 68`, `Community 74`, `Community 83`, `Community 90`, `Community 95`?**
  _High betweenness centrality (0.029) - this node is a cross-community bridge._
- **Why does `AuditSphereDbContext` connect `Community 18` to `Community 131`, `EF DbContext & Command Requests`, `Financial Statement Calculation`, `Project Namespaces`, `Community 144`, `Community 145`, `Community 20`, `Community 21`, `Community 30`, `Community 33`, `Community 34`, `Community 41`, `Community 48`, `Community 50`, `Community 66`, `Community 68`, `Community 69`, `Community 76`, `Community 77`, `Community 83`, `Community 84`, `Community 107`, `Community 112`, `Community 115`, `Community 118`, `Community 119`?**
  _High betweenness centrality (0.025) - this node is a cross-community bridge._
- **What connects `restore-drill.sh script`, `verify-tenant.sh script`, `PackageLine` to the rest of the system?**
  _1129 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Audit Planning & Durable Drafts` be split into smaller, more focused modules?**
  _Cohesion score 0.10377358490566038 - nodes in this community are weakly interconnected._
- **Should `NuGet Package Locks` be split into smaller, more focused modules?**
  _Cohesion score 0.041666666666666664 - nodes in this community are weakly interconnected._
- **Should `Financial Statement Calculation` be split into smaller, more focused modules?**
  _Cohesion score 0.10299003322259136 - nodes in this community are weakly interconnected._