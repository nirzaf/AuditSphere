import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, decimalInput, decode, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { PlanningCommandState } from './planning-command-state';
import { PlanningCommandReview } from './planning-command-review';
import { SHARED } from '../../core/ui';

const procedure = obj({ id: guid, sourceProcedureId: text, sourceSectionNumber: nullable(int), sourceSectionTitle: nullable(text), title: text,
  applicabilityStatus: text, status: text, riskId: nullable(guid), workpaperId: nullable(guid), originalWording: nullable(text),
  effectiveWording: nullable(text), definitionRevision: nullable(nat), definitionProvenance: text,
  applicabilityRevision: nat, applicabilityDecisionId: nullable(guid), pendingApplicabilityProposalId: nullable(guid),
  applicabilityRationale: nullable(text), applicabilityHistory: arr(obj({ id: guid, revision: nat, action: text,
    rationale: nullable(text), proposalDecisionId: nullable(guid), actorUserId: guid, occurredAt: instant,
    definitionRevision: nat, inputGeneration: nat }), 20), canProposeNotApplicable: bool,
  canApproveNotApplicable: bool, canReturnNotApplicable: bool, evidenceState: text,
  supportLinks: arr(obj({ toolKind: text, label: text, route: nullable(text), explanation: text, availability: text }), 20) });
const decodeManagerReview = obj({ engagementId: guid, completionReady: bool, partnerHandoffReady: bool,
  applicableProcedures: nat, proceduresNeedingReview: nat, itemTestsNeedingReview: nat, selectionsNeedingReview: nat,
  unresolvedReviewThreads: nat, openConfirmations: nat, bankReconciliationsNeedingApproval: nat, unevaluatedDifferences: nat,
  summaryReviewMemorandumState: text, summaryReviewMemorandumVersion: nullable(nat),
  summaryReviewMemorandumGeneratedAt: nullable(instant), blockers: arr(obj({ code: text, label: text, count: nat }), 100) });
export const decodeFieldwork = obj({ engagementId: guid, catalogProcedureCount: nat, catalogVersion: text,
  program: nullable(obj({ programCode: text, version: text, sourceHash: text })), procedures: arr(procedure, 5000),
  risks: arr(obj({ id: guid, area: text, significanceDecision: text, band: nullable(text), effectiveBand: nullable(text),
    balance: nullable(dec), currency: nullable(text), tolerableError: nullable(dec), planningMateriality: nullable(dec),
    riskAssessmentId: nullable(guid), materialityAssessmentId: nullable(guid), materialityCalculationId: nullable(guid),
    mappingVersionId: nullable(guid), mappingVersionNumber: nullable(nat), datasetId: nullable(guid), datasetDigest: nullable(text),
    destinationCode: nullable(text), statementSection: nullable(text), ruleVersion: nullable(text),
    explanation: nullable(text), blocker: nullable(text) }), 1000),
  differences: arr(obj({ currency: text, differenceCount: nat, grossAmount: dec, signedNetAmount: dec, unadjustedGrossAmount: dec, unadjustedSignedNetAmount: dec,
    correctedGrossAmount: dec, correctedSignedNetAmount: dec }), 100),
  aggregate: nullable(obj({ id: guid, status: text, conclusion: text, preparedByMe: bool })), aggregateCurrentAndReviewed: bool,
  schedules: arr(obj({ id: guid, scheduleType: text, rowCount: nat, currency: text }), 1000), samplingMethods: arr(text, 20),
  samplingRuns: arr(obj({ id: guid, selectionId: guid, selectionStatus: text, preparedByMe: bool, canReviewSelection: bool,
    createdAt: instant, method: text, interval: nullable(dec), keyItemThreshold: nullable(dec), sampleSize: nullable(int), seed: nullable(int),
    selectedCount: nat, populationCount: nat, coveragePercent: dec, sourceDigest: text, selectionDigest: text, engineVersion: text, previewDigest: nullable(text),
    orderingPolicy: nullable(text), attributeFields: arr(text, 8), reproduces: bool }), 10),
  evidenceCandidates: arr(obj({ uploadIntentId: guid, pbcRequestId: guid, requestArea: text, fileName: text, contentSha256: text, receivedAt: instant, superseded: bool }), 5000),
  physicalItems: arr(obj({ id: guid, fileIndex: text, boxReference: text, description: text, currentLocation: text,
    movements: arr(obj({ toLocation: text, movedAt: instant }), 1000), procedureTitles: arr(text, 1000) }), 5000),
  canManageFieldwork: bool, canReviewSelections: bool, canViewReviewNotes: bool, canAddOrResolveReviewNotes: bool, canRespondToReviewNotes: bool });
export const decodeProgramOptions = obj({ engagementId: guid, currentAdoptionId: nullable(guid), currentProgramVersionId: nullable(guid),
  currentVersion: nullable(text), currentSourceHash: nullable(text), currentContentDigest: nullable(text), baselineProcedureCount: nat,
  adHocProcedureCount: nat, currentAdoptionComplete: bool, currentAdoptionBlocker: nullable(text),
  options: arr(obj({ programVersionId: guid, programCode: text, version: text, sourceHash: text, contentDigest: nullable(text),
    procedureCount: nat, eligible: bool, blocker: nullable(text) }), 50) });
const samplingPreviewDecoder = obj({ previewDigest: text, sourceDigest: text, outcome: obj({ method: text, populationCount: nat, populationSignedTotal: dec,
  populationAbsoluteTotal: dec, selectedCount: nat, selectedAbsoluteTotal: dec, coveragePercent: dec, seed: nullable(int), interval: nullable(dec),
  keyItemThreshold: nullable(dec), items: arr(obj({ stableRowId: text, signedAmount: dec, absoluteAmount: dec, inclusionReason: text,
    cumulativeAbsoluteAmount: dec }), 100000),
  strata: nullable(arr(obj({ stratumKey: text, populationCount: nat, slots: nat, selectedCount: nat }), 10000)) }) });
export const decodeProcedureReview = obj({ procedureId: guid,
  currentResult: nullable(obj({ id: guid, revision: nat, workPerformed: text, conclusion: nullable(text),
    submittedEvidenceJson: nullable(text), evidenceDigest: nullable(text), evidenceReferencesJson: nullable(text) })),
  notes: arr(obj({ noteId: guid, resultRevision: nat, field: text, excerpt: text, startOffset: int, body: text, authorName: text, createdAt: instant, open: bool,
    events: arr(obj({ id: guid, kind: text, body: text, createdAt: instant }), 500) }), 500),
  evidence: arr(obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant }), 1000),
  physicalEvidence: arr(obj({ linkId: guid, itemId: guid, fileIndex: text, boxReference: text, description: text,
    currentLocation: text, linkedAt: instant }), 1000) });
const procedureCommandBasis = obj({ procedureId: guid, sourceProcedureId: text, title: text, applicabilityStatus: text,
  procedureStatus: text, definitionRevision: nat, inputGeneration: nat, currentResultRevision: nat,
  latestResultId: nullable(guid), latestResultRevision: nat, latestResultStatus: nullable(text),
  latestReviewId: nullable(guid), latestReviewDecision: nullable(text), canSubmit: bool, canReview: bool,
  blockers: arr(text, 100) });
export const decodeProcedureCommandState = obj({ basis: procedureCommandBasis, stateHash: text });
export const decodeProcedureCommandPreview = obj({ requestId: guid, requestHash: text, action: text,
  basis: procedureCommandBasis, canProceed: bool, blockers: arr(text, 100) });
export const decodeProcedureCommandReceipt = obj({ id: guid, procedureId: guid, sourceProcedureId: text, action: text,
  actorUserId: guid, actorEpoch: nat, requestId: guid, requestHash: text, definitionRevision: nat,
  inputGeneration: nat, expectedResultRevision: nat, resultId: nullable(guid), workpaperId: nullable(guid),
  reviewId: nullable(guid), resultRevision: nat, resultStatus: nullable(text), decision: nullable(text),
  evidenceDigest: nullable(text), committedAt: instant });
export const decodeProcedureCommandLookup = obj({ found: bool, receipt: nullable(decodeProcedureCommandReceipt) });
type Fieldwork = ReturnType<typeof decodeFieldwork>;
type SamplingPreview = ReturnType<typeof samplingPreviewDecoder>;
export const decodeSampleSet = obj({ selectionId: guid, procedureId: guid, method: text, rationale: text, status: text,
  selectedCount: nat, selectedSignedTotal: dec, inputGeneration: nat, testedCount: nat, exceptionCount: nat, cutOffRecordedCount: nat,
  subsequentMatchedCount: nat, totalCount: nat, page: nat, pageSize: nat,
  items: arr(obj({ selectionItemId: guid, stableRowId: text, signedAmount: dec, currency: text, inclusionReason: text,
    testResult: text, testRevision: nat, exceptionAmount: nullable(dec), auditItemTestId: nullable(guid), testWorkPerformed: nullable(text),
    evidenceReferences: arr(text, 100), contradictoryEvidence: nullable(text), followUp: nullable(text), testReviewed: bool,
    testReviewDecision: nullable(text), testReviewComment: nullable(text), canReviewTest: bool }), 500) });
type SampleSet = ReturnType<typeof decodeSampleSet>;
type SampleItem = SampleSet['items'][number];
type SampleTestDraft = { workPerformed: string; evidenceReferences: string; result: string; exceptionAmount: string;
  contradictoryEvidence: string; followUp: string; reviewerComment: string };

/** Risks recorded against one statement line (STE-NXT-013). The line is the only key the link sends. */
export function focusRisks<T extends { destinationCode: string | null }>(risks: readonly T[], line: string | null): T[] {
  if (!line) return [];
  return risks.filter((risk) => risk.destinationCode === line);
}

@Component({
  selector: 'audit-fieldwork',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatTabsModule, PlanningCommandReview, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Audit fieldwork</span></nav>
    @if (statementFocus.line) {
      <p role="status">Opened from statement line {{ statementFocus.line }}{{ statementFocus.area ? ' (' + statementFocus.area + ')' : '' }}. {{ focusedRisks().length ? focusedRisks().length + ' risk(s) are recorded for this line.' : 'No risk is recorded for this line yet.' }}</p>
    }
    <audit-page-header title="Controlled audit fieldwork" eyebrow="Engagement audit" [description]="ws.data()?.canManageFieldwork ? 'Fieldwork procedures, differences, and aggregate conclusion on this engagement.' : 'Review submitted procedure results and resolve review notes for this engagement.'" />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="fieldwork coverage" />
    @if (ws.data(); as w) {
      @if (w.program) { <p role="status">{{ w.procedures.length }} adopted procedures · {{ count(w, 'APPLICABLE') }} applicable · {{ reviewed(w) }} reviewed</p> }
      @if (w.canReviewSelections) {
        <section class="panel" aria-labelledby="manager-review-summary-heading">
          <h2 id="manager-review-summary-heading">Manager review and handoff</h2>
          <audit-state [loading]="managerReview.loading()" [error]="managerReview.error()" label="manager review summary" />
          @if (managerReview.data(); as summary) {
            <p role="status">{{ summary.partnerHandoffReady ? 'Ready for Partner handoff' : 'Partner handoff is blocked' }} ·
              Summary Review Memorandum: {{ summary.summaryReviewMemorandumState.replace('_', ' ').toLowerCase() }}
              @if (summary.summaryReviewMemorandumVersion !== null) { · version {{ summary.summaryReviewMemorandumVersion }} }
              @if (summary.summaryReviewMemorandumGeneratedAt) { · generated {{ summary.summaryReviewMemorandumGeneratedAt.slice(0, 10) }} }
            </p>
            <div class="table-scroll"><table aria-label="Manager review evidence counts">
              <thead><tr><th scope="col">Review evidence</th><th scope="col">Count</th></tr></thead>
              <tbody>
                <tr><th scope="row">Applicable procedures</th><td>{{ summary.applicableProcedures }}</td></tr>
                <tr><th scope="row">Procedures needing review</th><td>{{ summary.proceduresNeedingReview }}</td></tr>
                <tr><th scope="row">Selected item tests needing review</th><td>{{ summary.itemTestsNeedingReview }}</td></tr>
                <tr><th scope="row">Selections needing review</th><td>{{ summary.selectionsNeedingReview }}</td></tr>
                <tr><th scope="row">Unresolved review threads</th><td>{{ summary.unresolvedReviewThreads }}</td></tr>
                <tr><th scope="row">Open confirmations</th><td>{{ summary.openConfirmations }}</td></tr>
                <tr><th scope="row">Bank reconciliations needing approval</th><td>{{ summary.bankReconciliationsNeedingApproval }}</td></tr>
                <tr><th scope="row">Unevaluated differences</th><td>{{ summary.unevaluatedDifferences }}</td></tr>
              </tbody>
            </table></div>
            @if (summary.blockers.length) {
              <h3>Current blockers</h3>
              <ul>@for (blocker of summary.blockers; track blocker.code) { <li>{{ blocker.label }} <span>({{ blocker.count }})</span></li> }</ul>
            }
            <a [routerLink]="['/app/engagements', id(), 'completion']">Open completion and Summary Review Memorandum</a>
          }
        </section>
      }
      @if (w.canManageFieldwork || w.canViewReviewNotes) {
      <section class="panel" aria-labelledby="fieldwork-tools-heading">
        <h2 id="fieldwork-tools-heading">{{ w.canManageFieldwork ? 'Fieldwork tools' : 'Procedure review' }}</h2>
        @if (w.canManageFieldwork) { <a [routerLink]="['/app/engagements',id(),'confirmations']">Confirmation dashboard</a> }
        <mat-tab-group [selectedIndex]="toolTab()" (selectedIndexChange)="toolTab.set($event)">
          @if (w.canManageFieldwork || w.canReviewSelections) {
          <mat-tab label="Sampling">
            @if (w.canManageFieldwork) {
            <p>Choose a method variant, preview the exact rows, then confirm to record the selection. Population attributes come from the approved schedule; account, currency, direction and month are not inferred from the amount.</p>
            <p><strong>Method limits:</strong> MUS is monetary-unit interval selection; systematic and random methods select by row; key item selects above a threshold; stratified combines key items with random selection; attribute strata allocates rows across the selected attribute groups. The deterministic preview does not establish statistical sufficiency, confidence or an error bound. A qualified practitioner remains responsible for the methodology and sample size.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); sample()">
              <label>Procedure <select name="sp" [(ngModel)]="s.procedure"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
              <label>Approved schedule <select name="ss" [(ngModel)]="s.schedule"><option value="">Select</option>@for (x of w.schedules; track x.id) { <option [value]="x.id">{{ x.scheduleType }} · {{ x.rowCount }} rows ({{ x.currency }})</option> }</select></label>
              <label>Method <select name="sm" [(ngModel)]="s.method">@for (m of w.samplingMethods; track m) { <option [value]="m">{{ methodLabel(m) }}</option> }</select></label>
              @if (s.method === 'MUS') { <label>Interval <input name="si" inputmode="decimal" [(ngModel)]="s.interval" /></label> }
              @if (s.method === 'KEY_ITEM' || s.method === 'STRATIFIED') { <label>Key-item threshold <input name="sk" inputmode="decimal" [(ngModel)]="s.key" /></label> }
              @if (countBased()) {
                <label>Sample size <input name="sz" type="number" min="1" [(ngModel)]="s.size" /></label>
                <label>Seed <input name="sd" type="number" [(ngModel)]="s.seed" /></label>
              }
              @if (s.method === 'ATTRIBUTE_STRATA') {
                <fieldset class="fieldwork-attribute-fields"><legend>Stratify by</legend>
                  @for (field of attributeFields; track field.value) {
                    <label><input type="checkbox" [checked]="s.attributeFields.includes(field.value)" (change)="toggleAttributeField(field.value, $any($event.target).checked)" /> {{ field.label }}</label>
                  }
                </fieldset>
              }
              <label>Rationale <input name="sr" [(ngModel)]="s.rationale" maxlength="500" /></label>
              <button matButton="filled" type="submit" [disabled]="samplingBusy()">Preview selection</button>
            </form>
            @if (samplingPreview(); as preview) {
              <section class="panel" aria-labelledby="sampling-preview-heading">
                <h3 id="sampling-preview-heading">Exact selection preview</h3>
                @if (previewMatchesInputs()) {
                  <p role="status">{{ preview.outcome.selectedCount }} of {{ preview.outcome.populationCount }} rows selected · {{ preview.outcome.coveragePercent | money }}% absolute-amount coverage. Review the row identities and strata before recording.</p>
                } @else { <p role="alert" class="error-text">Sampling inputs changed after this preview. Preview again before recording.</p> }
                <dl class="facts"><dt>Source digest</dt><dd><code>{{ preview.sourceDigest }}</code></dd><dt>Preview digest</dt><dd><code>{{ preview.previewDigest }}</code></dd></dl>
                @if (preview.outcome.strata?.length) {
                  <h4>Attribute strata and allocation</h4>
                  <div class="table-scroll"><table aria-label="Attribute strata allocation"><thead><tr><th>Stratum</th><th>Population</th><th>Selected</th></tr></thead>
                    <tbody>@for (stratum of preview.outcome.strata; track stratum.stratumKey) { <tr><td>{{ stratum.stratumKey }}</td><td>{{ stratum.populationCount }}</td><td>{{ stratum.selectedCount }}</td></tr> }</tbody></table></div>
                }
                <h4>Selected row identities</h4>
                <div class="table-scroll"><table aria-label="Exact selected sampling rows"><thead><tr><th>Stable row ID</th><th>Signed amount</th><th>Selection basis</th></tr></thead>
                  <tbody>@for (item of preview.outcome.items; track item.stableRowId) { <tr><td><code>{{ item.stableRowId }}</code></td><td>{{ item.signedAmount | money }}</td><td>{{ item.inclusionReason }}</td></tr> }</tbody></table></div>
                <p><small>All selected IDs are shown above. Recording stores the selection, source and selection digests, method version, ordering policy and seed for replay.</small></p>
                <button matButton="filled" type="button" [disabled]="samplingBusy() || !previewMatchesInputs()" (click)="recordSampling()">Record this exact sample</button>
              </section>
            }
            }
            @if (w.samplingRuns.length) {
              <div class="table-scroll"><table aria-label="Sampling calculation log">
                <thead><tr><th>When</th><th>Method</th><th>Parameters</th><th>Selection</th><th>Selected</th><th>Coverage</th><th>Attributes / provenance</th><th>Re-performed</th><th>Execution</th></tr></thead>
                <tbody>@for (r of w.samplingRuns; track r.id) {
                  <tr><td>{{ r.createdAt.slice(0, 16).replace('T', ' ') }}</td><td>{{ methodLabel(r.method) }}</td><td>{{ parameters(r) }}</td><td>{{ r.selectionStatus }}</td><td>{{ r.selectedCount }} of {{ r.populationCount }}</td>
                    <td>{{ r.coveragePercent | money }}%</td><td>{{ r.attributeFields.length ? r.attributeFields.join(', ') : '—' }}<br /><small>{{ r.orderingPolicy ?? 'Legacy ordering' }} · {{ r.engineVersion }}</small></td>
                    <td [class.error-text]="!r.reproduces">{{ r.reproduces ? 'Matches' : 'Source changed' }}</td><td>
                      <button matButton (click)="openSampleSet(r.selectionId)" [disabled]="sampleSetLoading() || sampleActionBusy()">Open sample set</button>
                      @if (r.canReviewSelection) {
                        <label>Review note <input [(ngModel)]="selectionReviewComment" [name]="'selection-review-' + r.selectionId" /></label>
                        <button matButton (click)="reviewSelection(r.selectionId, 'REVIEWED')" [disabled]="sampleActionBusy()">Approve for execution</button>
                        <button matButton (click)="reviewSelection(r.selectionId, 'CHANGES_REQUIRED')" [disabled]="sampleActionBusy()">Return for changes</button>
                      } @else if (r.selectionStatus !== 'REVIEWED') {
                        <small>{{ r.preparedByMe ? 'Independent review required before execution.' : 'Awaiting an authorized selection reviewer.' }}</small>
                      }
                    </td></tr> }</tbody>
              </table></div>
            } @else { <p role="status">No saved sampling selections are available in this engagement yet.</p> }
            @if (sampleSetLoading()) { <p role="status">Loading the selected sample and execution history…</p> }
            @if (sampleSetError()) { <p role="alert" class="error-text">{{ sampleSetError() }}</p> }
            @if (sampleSet(); as selected) {
              <section class="panel" aria-labelledby="sample-execution-heading">
                <h3 id="sample-execution-heading">Sample execution · {{ selected.method }} · {{ selected.status }}</h3>
                <p>{{ selected.testedCount }} of {{ selected.totalCount }} items tested · {{ selected.exceptionCount }} exceptions or limitations · {{ selected.selectedCount }} items in this reviewed sample.</p>
                <p><strong>Rationale:</strong> {{ selected.rationale }}</p>
                @if (selected.status !== 'REVIEWED') {
                  <p role="status">Execution is locked until an independent reviewer approves this exact sample.</p>
                }
                @for (item of selected.items; track item.selectionItemId) {
                  @let draft = itemDraft(item);
                  <article class="panel" [attr.data-sample-item]="item.stableRowId">
                    <h4>{{ item.stableRowId }} · {{ item.signedAmount | money }} {{ item.currency }}</h4>
                    <p><strong>Selection basis:</strong> {{ item.inclusionReason }}</p>
                    @if (item.testRevision > 0) {
                      <div class="facts" aria-label="Recorded item test">
                        <p><strong>Current result:</strong> {{ item.testResult }} · revision {{ item.testRevision }}{{ item.testReviewed ? ' · independently reviewed' : ' · review pending' }}</p>
                        <p><strong>Work performed:</strong> {{ item.testWorkPerformed }}</p>
                        @if (item.exceptionAmount !== null) { <p><strong>Exception amount:</strong> {{ item.exceptionAmount | money }} {{ item.currency }}</p> }
                        @if (item.evidenceReferences.length) { <p><strong>Evidence references:</strong> {{ item.evidenceReferences.join(', ') }}</p> }
                        @if (item.contradictoryEvidence) { <p><strong>Contradictory evidence:</strong> {{ item.contradictoryEvidence }}</p> }
                        @if (item.followUp) { <p><strong>Required follow-up:</strong> {{ item.followUp }}</p> }
                        @if (item.testReviewDecision) { <p><strong>Reviewer decision:</strong> {{ item.testReviewDecision }}{{ item.testReviewComment ? ' — ' + item.testReviewComment : '' }}</p> }
                      </div>
                    } @else { <p role="status">No execution result recorded.</p> }
                    @if (selected.status === 'REVIEWED' && w.canManageFieldwork) {
                      <form class="inline-form" (submit)="$event.preventDefault(); recordItemTest(item)">
                        <label>Result <select [(ngModel)]="draft.result" [name]="'result-' + item.selectionItemId"><option value="PASS">Pass</option><option value="EXCEPTION">Exception</option><option value="LIMITATION">Limitation</option></select></label>
                        <label>Work performed <textarea [(ngModel)]="draft.workPerformed" [name]="'work-' + item.selectionItemId" maxlength="20000" rows="2"></textarea></label>
                        <label>Evidence references (one per line) <textarea [(ngModel)]="draft.evidenceReferences" [name]="'evidence-' + item.selectionItemId" rows="2"></textarea></label>
                        @if (draft.result === 'EXCEPTION') { <label>Exception amount <input inputmode="decimal" [(ngModel)]="draft.exceptionAmount" [name]="'amount-' + item.selectionItemId" /></label> }
                        @if (draft.result !== 'PASS') {
                          <label>Contradictory evidence <textarea [(ngModel)]="draft.contradictoryEvidence" [name]="'contradiction-' + item.selectionItemId" rows="2"></textarea></label>
                          <label>Required follow-up <textarea [(ngModel)]="draft.followUp" [name]="'followup-' + item.selectionItemId" rows="2"></textarea></label>
                        }
                        <button matButton="filled" type="submit" [disabled]="sampleActionBusy()">{{ item.testRevision ? 'Record a new test revision' : 'Record item test' }}</button>
                      </form>
                    }
                    @if (item.canReviewTest && item.auditItemTestId) {
                      <div class="inline-form" aria-label="Independent item-test review">
                        <label>Reviewer note <input [(ngModel)]="draft.reviewerComment" [name]="'test-review-' + item.selectionItemId" /></label>
                        <button matButton (click)="reviewItemTest(item.auditItemTestId!, 'REVIEWED', item.selectionItemId)" [disabled]="sampleActionBusy()">Review item test</button>
                        <button matButton (click)="reviewItemTest(item.auditItemTestId!, 'CHANGES_REQUIRED', item.selectionItemId)" [disabled]="sampleActionBusy() || !draft.reviewerComment.trim()">Return item test</button>
                      </div>
                    }
                  </article>
                }
                <p>Showing items {{ pageStart(selected) }}–{{ pageEnd(selected) }} of {{ selected.totalCount }}.</p>
                <div class="actions">
                  <button matButton (click)="loadSampleSet(selected.selectionId, selected.page - 1)" [disabled]="sampleSetLoading() || selected.page <= 1">Previous</button>
                  <button matButton (click)="loadSampleSet(selected.selectionId, selected.page + 1)" [disabled]="sampleSetLoading() || selected.page * selected.pageSize >= selected.totalCount">Next</button>
                </div>
              </section>
            }
          </mat-tab>
          }
          @if (w.canManageFieldwork) {
          <mat-tab label="Client evidence">
            <p>Link a received client upload to a procedure. The link pins the provider receipt digest; a superseded version cannot be linked.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); linkEvidence()">
              <label>Procedure <select name="ep" [(ngModel)]="e.procedure" (ngModelChange)="selectProcedure($event)"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
              <label>Received upload <select name="eu" [(ngModel)]="e.upload"><option value="">Select</option>@for (c of w.evidenceCandidates; track c.uploadIntentId) { <option [value]="c.uploadIntentId">{{ c.requestArea }} · {{ c.fileName }} · {{ c.receivedAt.slice(0, 16).replace('T', ' ') }}{{ c.superseded ? ' (superseded)' : '' }}</option> }</select></label>
              <label>Note <input name="en" [(ngModel)]="e.note" maxlength="500" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Link evidence</button>
            </form>
            @if (review()?.evidence?.length) { <ul aria-label="Linked client evidence">@for (l of review()!.evidence; track l.linkId) { <li>{{ l.fileName }} <code>{{ l.contentSha256.slice(0, 12) }}</code>{{ l.note ? ' — ' + l.note : '' }}</li> }</ul> }
          </mat-tab>
          <mat-tab label="Physical files">
            <form class="inline-form" (submit)="$event.preventDefault(); register()">
              <label>File index <input name="pi" [(ngModel)]="ph.index" maxlength="40" placeholder="X-1" /></label>
              <label>Box <input name="pb" [(ngModel)]="ph.box" maxlength="40" placeholder="Box 3" /></label>
              <label>Description <input name="pd" [(ngModel)]="ph.description" maxlength="300" /></label>
              <label>Location <input name="pl" [(ngModel)]="ph.location" maxlength="200" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Register file</button>
            </form>
            @for (item of w.physicalItems; track item.id) {
              <article class="panel" [attr.data-file-index]="item.fileIndex">
                <p><strong>{{ item.fileIndex }}</strong> · {{ item.boxReference }} · {{ item.description }} — at <em>{{ item.currentLocation }}</em></p>
                <p><small>History: {{ history(item.movements) }}</small></p>
                <p><small>Procedures: {{ item.procedureTitles.length ? item.procedureTitles.join(', ') : 'none' }}</small></p>
                <div class="inline-form">
                  <label>Link to procedure <select [(ngModel)]="ph.linkTarget" [name]="'pt-' + item.id" [attr.aria-label]="'Procedure for ' + item.fileIndex"><option value="">Select</option>@for (p of applicable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
                  <button matButton (click)="linkPhysical(item.id)" [disabled]="cmd.busy() || !ph.linkTarget" [attr.aria-label]="'Link ' + item.fileIndex">Link</button>
                  <label>Move to <input [(ngModel)]="ph.moveTo" [name]="'mv-' + item.id" [attr.aria-label]="'New location for ' + item.fileIndex" /></label>
                  <button matButton (click)="move(item.id)" [disabled]="cmd.busy()" [attr.aria-label]="'Move ' + item.fileIndex">Record move</button>
                </div>
              </article>
            }
          </mat-tab>
          <mat-tab label="Ad hoc steps">
            <p>Add a step for an unusual risk or transaction. The adopted programme's baseline is untouched; the step must be performed and reviewed before completion.</p>
            <form class="inline-form" (submit)="$event.preventDefault(); adHoc()">
              <label>Title <input name="at" [(ngModel)]="ah.title" maxlength="300" /></label>
              <label>Step <textarea name="aw" [(ngModel)]="ah.wording" maxlength="4000" rows="2"></textarea></label>
              <label>Why it is needed <input name="ar" [(ngModel)]="ah.reason" maxlength="500" /></label>
              <label>Audit area <input name="as" [(ngModel)]="ah.section" maxlength="100" /></label>
              <button matButton="filled" type="submit" [disabled]="cmd.busy()">Insert step</button>
            </form>
          </mat-tab>
          }
          @if (w.canViewReviewNotes) {
          <mat-tab label="Review notes">
            <p>Anchor a note to text in the current submitted result. A result cannot be approved while a note is open; the preparer responds and a reviewer resolves.</p>
            @if (reviewable(w).length) {
              <label>Procedure <select [(ngModel)]="e.procedure" name="rp" (ngModelChange)="selectProcedure($event)"><option value="">Select</option>@for (p of reviewable(w); track p.id) { <option [value]="p.id">{{ p.sourceProcedureId }} · {{ p.title }}</option> }</select></label>
            } @else { <p role="status">No submitted procedure results are ready for review.</p> }
            @if (reviewError()) { <p role="alert" class="error-text">{{ reviewError() }}</p> }
            @if (review()?.currentResult; as r) {
              <div aria-label="Current submitted result">
                <p><strong>Revision {{ r.revision }} — work performed:</strong> {{ r.workPerformed }}</p>
                <p><strong>Conclusion:</strong> {{ r.conclusion }}</p>
              </div>
              @if (w.canAddOrResolveReviewNotes) {
              <form class="inline-form" (submit)="$event.preventDefault(); addNote(r.id)">
                <label>Field <select name="nf" [(ngModel)]="n.field"><option value="WORK_PERFORMED">Work performed</option><option value="CONCLUSION">Conclusion</option></select></label>
                <label>Quoted text <input name="ne" [(ngModel)]="n.excerpt" maxlength="500" /></label>
                <label>Note <input name="nb" [(ngModel)]="n.body" maxlength="4000" /></label>
                <button matButton="filled" type="submit" [disabled]="cmd.busy()">Add note</button>
              </form>
              }
            }
            @for (note of review()?.notes ?? []; track note.noteId) {
              <article class="panel" [attr.data-note]="note.excerpt">
                <p><mark>“{{ note.excerpt }}”</mark> (r{{ note.resultRevision }}, {{ note.field.toLowerCase().replace('_', ' ') }}) — {{ note.body }} <small>{{ note.authorName }}</small> <strong>{{ note.open ? 'Open' : 'Resolved' }}</strong></p>
                @for (ev of note.events; track ev.id) { <p><small>{{ ev.kind.toLowerCase() }}: {{ ev.body }}</small></p> }
                <div class="inline-form">
                  @if (w.canRespondToReviewNotes) {
                    <label>Reply <input [(ngModel)]="n.reply" [name]="'nr-' + note.noteId" [attr.aria-label]="'Reply to note on ' + note.excerpt" /></label>
                    <button matButton (click)="noteEvent(note.noteId, 'RESPONSE')" [disabled]="cmd.busy()">Respond</button>
                  }
                  @if (note.open && w.canAddOrResolveReviewNotes) { <button matButton (click)="noteEvent(note.noteId, 'RESOLVED')" [disabled]="cmd.busy()" [attr.aria-label]="'Resolve note on ' + note.excerpt">Resolve</button> }
                </div>
              </article>
            }
          </mat-tab>
          }
        </mat-tab-group>
      </section>
      }

      @if (w.canManageFieldwork) {
      <section class="panel" aria-labelledby="program-heading">
        <h2 id="program-heading">Versioned audit program</h2>
        <p>The controlled source catalog contains {{ w.catalogProcedureCount }} procedures across 20 sections. Applicability and evidence status are persisted per engagement.</p>
        @if (w.program; as p) {
          <dl class="facts"><dt>Program</dt><dd>{{ p.programCode }} v{{ p.version }}</dd><dt>Source hash</dt><dd><code>{{ p.sourceHash }}</code></dd>
            <dt>Adopted procedures</dt><dd>{{ w.procedures.length }}</dd><dt>Applicable / reviewed</dt><dd>{{ count(w, 'APPLICABLE') }} / {{ reviewed(w) }}</dd></dl>
          @if (programOptions.data(); as options) {
            <p>Controlled baseline {{ options.baselineProcedureCount }} · ad hoc {{ options.adHocProcedureCount }}. The baseline and engagement-added procedures are counted separately.</p>
            @if (!options.currentAdoptionComplete || options.currentAdoptionBlocker) { <p class="error" role="alert">{{ options.currentAdoptionBlocker ?? 'The current adoption is not verified complete. Review its source and retained procedures before relying on coverage.' }}</p> }
          }
          <p class="actions"><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Open planning</a><a routerLink="/app/audit/library">Program library</a><a [routerLink]="['/app/engagements', w.engagementId, 'completion']">Open completion</a></p>
        } @else {
          <p>No published program has been adopted for this engagement.</p>
          @if (programOptions.data(); as options) {
            @if (options.currentAdoptionBlocker) { <p class="error" role="alert">{{ options.currentAdoptionBlocker }}</p> }
            @if (options.options.length) {
              <label for="published-program-version">Published programme version</label>
              <select id="published-program-version" [ngModel]="selectedProgramVersion(options)" (ngModelChange)="selectedProgramVersionId.set($event)">
                @for (option of options.options; track option.programVersionId) {
                  <option [value]="option.programVersionId" [disabled]="!option.eligible">{{ option.programCode }} v{{ option.version }} · {{ option.procedureCount }} procedures{{ option.eligible ? '' : ' · unavailable' }}</option>
                }
              </select>
              @if (selectedProgram(options); as selected) {
                <p>Source SHA-256 <code>{{ selected.sourceHash }}</code> · content SHA-256 <code>{{ selected.contentDigest ?? 'unverified' }}</code> · baseline {{ selected.procedureCount }}</p>
                @if (selected.blocker) { <p role="status">{{ selected.blocker }}</p> }
                <button matButton="filled" (click)="adoptProgram(options)" [disabled]="cmd.busy() || !selected.eligible || !!options.currentAdoptionId || !!options.currentAdoptionBlocker">Adopt exact published version {{ selected.version }}</button>
              }
            } @else { <p role="status">No published eligible programme is available. A firm-wide owner must publish a reviewed version first.</p> }
          } @else if (programOptions.loading()) { <p role="status">Loading engagement-scoped published versions…</p> }
          @else if (programOptions.error()) { <p class="error" role="alert">{{ programOptions.error() }}</p> }
        }
      </section>

      @if (w.differences.length) {
        <section class="panel" aria-labelledby="difference-summary-heading">
          <h2 id="difference-summary-heading">Aggregate differences and reporting assessment</h2>
          <p>Review gross and signed amounts separately by currency. These totals do not determine materiality or an audit opinion; the engagement team records the professional conclusion.</p>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Currency</th><th scope="col" class="number">Count</th><th scope="col" class="number">Gross</th><th scope="col" class="number">Signed/net</th><th scope="col" class="number">Unadjusted gross</th><th scope="col" class="number">Unadjusted net</th><th scope="col" class="number">Corrected gross</th></tr></thead>
            <tbody>@for (d of w.differences; track d.currency) { <tr><td>{{ d.currency }}</td><td class="number">{{ d.differenceCount }}</td><td class="number">{{ d.grossAmount | money }}</td><td class="number">{{ d.signedNetAmount | money }}</td>
              <td class="number">{{ d.unadjustedGrossAmount | money }}</td><td class="number">{{ d.unadjustedSignedNetAmount | money }}</td><td class="number">{{ d.correctedGrossAmount | money }}</td></tr> }</tbody>
          </table></div>
          @if (w.aggregate; as a) {
            <dl class="facts"><dt>Assessment</dt><dd>{{ a.status }}</dd><dt>Current and independently reviewed</dt><dd>{{ w.aggregateCurrentAndReviewed ? 'Yes' : 'No — refresh the assessment before completion' }}</dd>
              <dt>Professional conclusion / reporting impact</dt><dd>{{ a.conclusion }}</dd></dl>
            @if (a.status !== 'REVIEWED' && !a.preparedByMe) { <button matButton="filled" (click)="send('/api/ui/area-assessments/' + a.id + '/review', {}, 'Aggregate conclusion reviewed.')" [disabled]="cmd.busy()">Review aggregate conclusion</button> }
          }
          <form (submit)="$event.preventDefault(); aggregate(w)">
            <label>Required: professional aggregate conclusion and reporting impact <textarea name="agg" [(ngModel)]="aggregateConclusion" maxlength="4000" rows="4" required></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || !aggregateConclusion.trim()">Record aggregate conclusion</button>
          </form>
        </section>
      }

      @if (w.program) {
        <section class="panel" aria-labelledby="coverage-heading">
          <h2 id="coverage-heading">Procedure applicability and evidence coverage</h2>
          <p>An applicable procedure cannot be completed from a title alone. Link its financial-statement risk so execution and review use current qualitative risk and mapped FSLI exposure. Submit work, evidence and a current independent review through the guarded commands.</p>
          <div class="inline-form">
            <label>Audit section <select name="section" [ngModel]="section() ?? firstSection(w)" (ngModelChange)="section.set($event)">
              <option [ngValue]="0">All sections</option>
              @for (g of sections(w); track g.key) { <option [ngValue]="g.key">{{ g.key === -2 ? 'Unclassified' : 'Section ' + g.key }} — {{ g.title }} ({{ g.count }})</option> }</select></label>
            <span>Showing {{ visible(w).length }} of {{ w.procedures.length }} authorized procedures</span>
          </div>
          <div class="table-scroll"><table>
            <thead><tr><th scope="col">Source</th><th scope="col">Section</th><th scope="col">Procedure</th><th scope="col">Instructions and provenance</th><th scope="col">Applicability</th><th scope="col">Linked risk</th><th scope="col">Evidence</th><th scope="col">Action</th></tr></thead>
            <tbody>@for (p of visible(w); track p.id) {
              <tr [id]="'procedure-' + p.id"><td><code>{{ p.sourceProcedureId }}</code></td><td>{{ p.sourceSectionTitle }}</td><td>{{ p.title }}</td>
                <td><details><summary>View original and effective wording</summary>
                  <p><strong>Original source:</strong> {{ p.originalWording ?? 'No controlled source text; see the retained ad-hoc wording history.' }}</p>
                  <p><strong>Effective instructions:</strong> {{ p.effectiveWording ?? p.title }}</p>
                  <p><strong>Definition provenance:</strong> {{ p.definitionProvenance }}{{ p.definitionRevision ? ' · revision ' + p.definitionRevision : '' }}</p>
                </details></td><td>{{ p.applicabilityStatus }}</td>
                <td>
                  @if (w.canManageFieldwork) {
                    <div class="inline-form">
                      <label [attr.for]="'risk-link-' + p.id">Financial-statement risk</label>
                      <select [id]="'risk-link-' + p.id" [name]="'risk-link-' + p.id" [ngModel]="riskSelection(p)"
                        (ngModelChange)="setRiskSelection(p.id, $event)">
                        <option value="">No linked risk</option>
                        @for (risk of w.risks; track risk.id) {
                          <option [value]="risk.id">{{ risk.area }} · {{ risk.effectiveBand ?? risk.band ?? 'Not assessed' }}{{ risk.significanceDecision === 'SIGNIFICANT' ? ' · Significant' : '' }}{{ risk.blocker ? ' · Review blocked' : '' }}</option>
                        }
                      </select>
                      <button matButton (click)="saveRiskLink(p.id)" [disabled]="cmd.busy() || !riskLink.canPrepare('LINK_PROCEDURE_RISK') || riskSelection(p) === (p.riskId ?? '')">Preview risk link</button>
                      @if (riskFor(w, p.riskId); as linkedRisk) {
                        <div class="facts" aria-label="Current procedure risk basis">
                          <p><strong>{{ linkedRisk.effectiveBand ?? linkedRisk.band ?? 'Unassessed' }} effective risk</strong>
                            {{ linkedRisk.significanceDecision === 'SIGNIFICANT' ? '· Significant risk' : '' }}</p>
                          @if (linkedRisk.blocker) { <p role="alert" class="error-text">{{ linkedRisk.blocker }}</p> }
                          @if (linkedRisk.balance !== null && linkedRisk.currency) {
                            <p>Mapped FSLI balance: {{ linkedRisk.balance | money }} {{ linkedRisk.currency }}</p>
                          }
                          @if (linkedRisk.tolerableError !== null && linkedRisk.planningMateriality !== null && linkedRisk.currency) {
                            <p>TE {{ linkedRisk.tolerableError | money }} {{ linkedRisk.currency }} · PM {{ linkedRisk.planningMateriality | money }} {{ linkedRisk.currency }}</p>
                          }
                          @if (linkedRisk.explanation) { <p>{{ linkedRisk.explanation }}</p> }
                          <details>
                            <summary>Risk and planning source revision</summary>
                            <p>Risk assessment: <code>{{ linkedRisk.riskAssessmentId ?? 'not assessed' }}</code></p>
                            <p>Materiality assessment / calculation: <code>{{ linkedRisk.materialityAssessmentId ?? 'not available' }}</code> / <code>{{ linkedRisk.materialityCalculationId ?? 'not available' }}</code></p>
                            <p>Approved mapping: <code>{{ linkedRisk.mappingVersionId ?? 'not available' }}</code>{{ linkedRisk.mappingVersionNumber !== null ? ' · version ' + linkedRisk.mappingVersionNumber : '' }}</p>
                            <p>Dataset: <code>{{ linkedRisk.datasetId ?? 'not available' }}</code>{{ linkedRisk.datasetDigest ? ' · SHA-256 ' + linkedRisk.datasetDigest : '' }}</p>
                            <p>FSLI: {{ linkedRisk.destinationCode ?? 'not matched' }}{{ linkedRisk.statementSection ? ' · ' + linkedRisk.statementSection : '' }} · rules {{ linkedRisk.ruleVersion ?? 'unavailable' }}</p>
                          </details>
                        </div>
                      }
                    </div>
                  } @else {
                    @if (riskFor(w, p.riskId); as linkedRisk) {
                      <div class="facts" aria-label="Current procedure risk basis">
                        <p><strong>{{ linkedRisk.effectiveBand ?? linkedRisk.band ?? 'Unassessed' }} effective risk</strong>
                          {{ linkedRisk.significanceDecision === 'SIGNIFICANT' ? '· Significant risk' : '' }}</p>
                        @if (linkedRisk.blocker) { <p role="alert" class="error-text">{{ linkedRisk.blocker }}</p> }
                        @if (linkedRisk.balance !== null && linkedRisk.currency) { <p>Mapped FSLI balance: {{ linkedRisk.balance | money }} {{ linkedRisk.currency }}</p> }
                        @if (linkedRisk.tolerableError !== null && linkedRisk.planningMateriality !== null && linkedRisk.currency) {
                          <p>TE {{ linkedRisk.tolerableError | money }} {{ linkedRisk.currency }} · PM {{ linkedRisk.planningMateriality | money }} {{ linkedRisk.currency }}</p>
                        }
                        @if (linkedRisk.explanation) { <p>{{ linkedRisk.explanation }}</p> }
                        <details><summary>Risk and planning source revision</summary>
                          <p>Risk assessment: <code>{{ linkedRisk.riskAssessmentId ?? 'not assessed' }}</code></p>
                          <p>Materiality assessment / calculation: <code>{{ linkedRisk.materialityAssessmentId ?? 'not available' }}</code> / <code>{{ linkedRisk.materialityCalculationId ?? 'not available' }}</code></p>
                          <p>Approved mapping: <code>{{ linkedRisk.mappingVersionId ?? 'not available' }}</code>{{ linkedRisk.mappingVersionNumber !== null ? ' · version ' + linkedRisk.mappingVersionNumber : '' }}</p>
                          <p>Dataset: <code>{{ linkedRisk.datasetId ?? 'not available' }}</code>{{ linkedRisk.datasetDigest ? ' · SHA-256 ' + linkedRisk.datasetDigest : '' }}</p>
                          <p>FSLI: {{ linkedRisk.destinationCode ?? 'not matched' }}{{ linkedRisk.statementSection ? ' · ' + linkedRisk.statementSection : '' }} · rules {{ linkedRisk.ruleVersion ?? 'unavailable' }}</p>
                        </details>
                      </div>
                    } @else { <small>{{ riskLabel(w, p.riskId) }}</small> }
                  }
                </td><td>{{ p.status }}</td>
                <td>
                  <strong>{{ p.applicabilityStatus }}</strong>
                  @if (p.applicabilityStatus === 'PENDING' || p.applicabilityStatus === 'NA_UNVERIFIED' || p.applicabilityStatus === 'APPLICABLE') {
                    @if (p.applicabilityStatus === 'PENDING' || p.applicabilityStatus === 'NA_UNVERIFIED') {
                      <button matButton="filled" (click)="decide(p, 'APPLICABLE')" [disabled]="cmd.busy()">Applicable</button>
                    } @else { <small>Execute from source workflow</small> }
                  }
                  @if (p.canProposeNotApplicable) {
                    <label [attr.for]="'na-reason-' + p.id">Reason for non-applicability</label>
                    <textarea [id]="'na-reason-' + p.id" [name]="'na-reason-' + p.id" maxlength="1000"
                      [ngModel]="applicabilityReason(p.id)" (ngModelChange)="setApplicabilityReason(p.id, $event)"></textarea>
                    <button matButton="filled" (click)="decide(p, 'PROPOSE_NOT_APPLICABLE')"
                      [disabled]="cmd.busy() || !applicabilityReason(p.id).trim()">Propose N/A</button>
                  }
                  @if (p.canApproveNotApplicable) {
                    <p><strong>Pending N/A reason:</strong> {{ p.applicabilityRationale }}</p>
                    <button matButton="filled" (click)="decide(p, 'APPROVE_NOT_APPLICABLE')" [disabled]="cmd.busy()">Approve as Partner</button>
                    <label [attr.for]="'na-return-' + p.id">Reason to return proposal</label>
                    <textarea [id]="'na-return-' + p.id" [name]="'na-return-' + p.id" maxlength="1000"
                      [ngModel]="applicabilityReason(p.id)" (ngModelChange)="setApplicabilityReason(p.id, $event)"></textarea>
                    <button matButton (click)="decide(p, 'RETURN_NOT_APPLICABLE')"
                      [disabled]="cmd.busy() || !applicabilityReason(p.id).trim()">Return proposal</button>
                  }
                  @if (p.applicabilityStatus === 'NA_UNVERIFIED') {
                    <p role="status">Historical N/A is unverified against current proposal, Partner and input evidence.</p>
                  }
                  @if (w.canManageFieldwork || w.canViewReviewNotes) {
                    <button matButton (click)="openProcedure(p)">Open procedure</button>
                  }
                  @if (p.applicabilityHistory?.length) {
                    <details><summary>Applicability history (latest 20)</summary>
                      @for (decision of p.applicabilityHistory; track decision.id) {
                        <p>Revision {{ decision.revision }} · {{ decision.action }} · {{ decision.occurredAt | date:'medium' }} · {{ decision.actorUserId }}{{ decision.rationale ? ' · ' + decision.rationale : '' }}</p>
                      }
                    </details>
                  }
                </td></tr> }</tbody>
          </table></div>
        </section>

        @if (procedureSelectedId()) {
          <section class="panel" aria-labelledby="procedure-command-heading">
            <h2 id="procedure-command-heading">Procedure execution and review</h2>
            @if (procedureCommandState(); as state) {
              <p><strong>{{ state.basis.sourceProcedureId }} · {{ state.basis.title }}</strong></p>
              <p>Applicability {{ state.basis.applicabilityStatus }} · procedure {{ state.basis.procedureStatus }} · definition revision {{ state.basis.definitionRevision }} · input generation {{ state.basis.inputGeneration }}</p>
              @if (selectedProcedure(w); as selected) {
                <details><summary>Source and effective instructions</summary>
                  <p><strong>Original source:</strong> {{ selected.originalWording ?? 'Retained ad-hoc wording only.' }}</p>
                  <p><strong>Effective instructions:</strong> {{ selected.effectiveWording ?? selected.title }}</p>
                  <p><strong>Definition provenance:</strong> {{ selected.definitionProvenance }}{{ selected.definitionRevision ? ' · revision ' + selected.definitionRevision : '' }}</p>
                </details>
              }
              @if (selectedProcedure(w); as selected) {
                @if (selected.supportLinks.length) {
                  <section aria-labelledby="procedure-support-heading">
                    <h3 id="procedure-support-heading">Supporting tools and manual work</h3>
                    @for (support of selected.supportLinks; track support.toolKind + ':' + (support.route ?? '')) {
                      <p>@if (support.route) { <a [routerLink]="support.route">{{ support.label }}</a> } @else { <strong>{{ support.label }}</strong> }
                        · {{ support.availability }} — {{ support.explanation }}</p>
                    }
                  </section>
                }
              }
              @if (state.basis.blockers.length) {
                <div role="alert" class="error"><p>Current blockers:</p><ul>@for (b of state.basis.blockers; track b) { <li>{{ b }}</li> }</ul></div>
              }
              @if (review(); as r) {
                @if (r.currentResult; as result) {
                  <div class="facts"><h3>Frozen submitted result</h3>
                    <p><strong>Work performed:</strong> {{ result.workPerformed }}</p>
                    <p><strong>Conclusion:</strong> {{ result.conclusion }}</p>
                    <p><strong>Evidence digest:</strong> <code>{{ result.evidenceDigest ?? 'legacy/unverified' }}</code></p>
                    @if (result.submittedEvidenceJson) { <details><summary>Evidence snapshot considered at submission</summary><pre>{{ result.submittedEvidenceJson }}</pre></details> }
                    @if (r.evidence.length || r.physicalEvidence.length) {
                      <details><summary>Current linked evidence (not the submitted snapshot)</summary>
                        @for (e of r.evidence; track e.linkId) { <p>{{ e.fileName }} · {{ e.contentSha256 }}</p> }
                        @for (phys of r.physicalEvidence; track phys.linkId) { <p>{{ phys.fileIndex }} · {{ phys.boxReference }} · {{ phys.currentLocation }}</p> }
                      </details>
                    }
                  </div>
                }
              }
              @if (state.basis.canSubmit) {
                <form (submit)="$event.preventDefault(); prepareProcedureCommand('SUBMIT_RESULT')">
                  <h3>Record performed work</h3>
                  <label>Work performed <textarea name="procedure-work" [(ngModel)]="procedureWork" (ngModelChange)="procedureInputChanged()" maxlength="100000" rows="4" required></textarea></label>
                  <label>Conclusion <textarea name="procedure-conclusion" [(ngModel)]="procedureConclusion" (ngModelChange)="procedureInputChanged()" maxlength="20000" rows="3" required></textarea></label>
                  <fieldset><legend>Evidence actually considered</legend>
                    @if (!(review()?.evidence?.length) && !(review()?.physicalEvidence?.length)) { <p>Link received PBC evidence or a physical file in the evidence controls first, or record a complete manual reference below.</p> }
                    @for (e of review()?.evidence ?? []; track e.linkId) {
                      <label><input type="checkbox" [checked]="procedureSelectedLinks.has(e.linkId)" (change)="toggleProcedureLink(e.linkId, $any($event.target).checked)" /> {{ e.fileName }} · {{ e.contentSha256 }}</label>
                    }
                    @for (phys of review()?.physicalEvidence ?? []; track phys.linkId) {
                      <label><input type="checkbox" [checked]="procedureSelectedPhysical.has(phys.linkId)" (change)="toggleProcedurePhysical(phys.linkId, $any($event.target).checked)" /> {{ phys.fileIndex }} · {{ phys.boxReference }} · {{ phys.currentLocation }}</label>
                    }
                    <label>Manual reference <input name="manual-reference" [(ngModel)]="procedureManualReference" (ngModelChange)="procedureInputChanged()" maxlength="500" /></label>
                    <label>What was observed <textarea name="manual-observation" [(ngModel)]="procedureObservation" (ngModelChange)="procedureInputChanged()" maxlength="4000" rows="3"></textarea></label>
                    <label>Legacy free-text references (one per line) <textarea name="legacy-references" [(ngModel)]="procedureLegacyReferences" (ngModelChange)="procedureInputChanged()" rows="2"></textarea></label>
                  </fieldset>
                  <button matButton="filled" type="submit" [disabled]="procedureCommandBusy() || !procedureWork.trim() || !procedureConclusion.trim()">Preview exact submission</button>
                </form>
              }
              @if (state.basis.canReview) {
                <section aria-labelledby="procedure-review-action-heading">
                  <h3 id="procedure-review-action-heading">Independent review</h3>
                  @if (review()?.currentResult; as result) { <p>Reviewing result revision {{ result.revision }}. The submitted snapshot above is the evidence considered.</p> }
                  <label>Decision <select name="procedure-decision" [(ngModel)]="procedureDecision" (ngModelChange)="procedureInputChanged()">
                    <option value="REVIEWED">Reviewed</option><option value="CHANGES_REQUIRED">Changes required</option></select></label>
                  <label>Review comment (required when returning) <textarea name="procedure-comment" [(ngModel)]="procedureComment" (ngModelChange)="procedureInputChanged()" maxlength="20000" rows="3"></textarea></label>
                  <button matButton="filled" (click)="prepareProcedureCommand('REVIEW_RESULT')" [disabled]="procedureCommandBusy() || (procedureDecision === 'CHANGES_REQUIRED' && !procedureComment.trim())">Preview exact review</button>
                </section>
              }
              @if (procedureCommandPreview(); as preview) {
                <section class="panel" aria-labelledby="procedure-preview-heading">
                  <h3 id="procedure-preview-heading">Exact command preview</h3>
                  <p>{{ preview.action === 'SUBMIT_RESULT' ? 'Submit this exact work and evidence snapshot.' : 'Record this exact independent review.' }}</p>
                  <p>Request SHA-256 <code>{{ preview.requestHash }}</code></p>
                  <p>Basis: definition revision {{ preview.basis.definitionRevision }} · input generation {{ preview.basis.inputGeneration }} · current result revision {{ preview.basis.currentResultRevision }}</p>
                  @if (preview.blockers.length) { <ul role="alert">@for (b of preview.blockers; track b) { <li>{{ b }}</li> }</ul> }
                  <label><input type="checkbox" [(ngModel)]="procedureAssent" /> I reviewed this exact procedure, work, evidence and basis.</label>
                  <button matButton="filled" (click)="executeProcedureCommand()" [disabled]="procedureCommandBusy() || !procedureAssent || !preview.canProceed">Record exact outcome</button>
                </section>
              }
              @if (procedureCommandReceipt(); as receipt) {
                <p role="status">Recorded {{ receipt.action }} · request <code>{{ receipt.requestId }}</code> · result revision {{ receipt.resultRevision }} · {{ receipt.committedAt | date:'medium' }}{{ receipt.evidenceDigest ? ' · evidence ' + receipt.evidenceDigest : '' }}</p>
              }
              @if (procedureUnknown(); as pending) {
                <p role="alert">The outcome is unknown. Keep request <code>{{ pending.requestId }}</code> and check the retained receipt; do not resend a new identity.</p>
                <button matButton (click)="recoverProcedureCommand()" [disabled]="procedureCommandBusy()">Check original outcome</button>
              }
              @if (procedureCommandError()) { <p role="alert" class="error-text">{{ procedureCommandError() }}</p> }
            } @else if (procedureCommandBusy()) { <p role="status">Loading exact procedure state…</p> }
            @else { <p role="alert">{{ procedureCommandError() || 'Procedure command state is unavailable.' }}</p> }
          </section>
        }
      }
      }
    }
    <audit-planning-command-review [flow]="riskLink" />
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  private readonly applicabilityReasons = signal<Record<string, string>>({});
  private readonly route = inject(ActivatedRoute);
  readonly id = routeGuid();
  readonly statementFocus = { line: this.route.snapshot?.queryParamMap.get('line') ?? null, area: this.route.snapshot?.queryParamMap.get('area') ?? null };
  readonly focusedRisks = computed(() => focusRisks(this.ws.data()?.risks ?? [], this.statementFocus.line));
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), decodeFieldwork,
    'Fieldwork unavailable: an authorized internal engagement scope is required.');
  readonly programOptions = this.api.resource(() => this.id() && this.ws.data()?.canManageFieldwork
    ? `/api/ui/engagements/${this.id()}/fieldwork/program-options` : null, decodeProgramOptions,
    'Published programme options unavailable for this authorized engagement.');
  readonly selectedProgramVersionId = signal('');
  readonly managerReview = this.api.resource(() => this.id() && this.ws.data()?.canReviewSelections
    ? `/api/ui/engagements/${this.id()}/manager-review` : null, decodeManagerReview,
    'Manager review summary unavailable: an authorized reviewer scope is required.');
  readonly cmd = new CommandState(this.api);
  readonly riskLink = new PlanningCommandState(() => this.id(), () => this.ws.data(),
    () => !!this.ws.data()?.canManageFieldwork && !this.ws.loading() && !this.ws.error(),
    () => this.cmd.busy() || this.cmd.uncertain(), () => this.ws.reload(), ['LINK_PROCEDURE_RISK']);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  readonly review = signal<ReturnType<typeof decodeProcedureReview> | null>(null);
  readonly reviewError = signal('');
  readonly procedureSelectedId = signal('');
  readonly procedureCommandState = signal<ReturnType<typeof decodeProcedureCommandState> | null>(null);
  readonly procedureCommandPreview = signal<ReturnType<typeof decodeProcedureCommandPreview> | null>(null);
  readonly procedureCommandReceipt = signal<ReturnType<typeof decodeProcedureCommandReceipt> | null>(null);
  readonly procedureCommandError = signal('');
  readonly procedureCommandBusy = signal(false);
  readonly procedureUnknown = signal<{ requestId: string; requestHash: string } | null>(null);
  procedureWork = '';
  procedureConclusion = '';
  procedureDecision = 'REVIEWED';
  procedureComment = '';
  procedureManualReference = '';
  procedureObservation = '';
  procedureLegacyReferences = '';
  procedureAssent = false;
  procedureSelectedLinks = new Set<string>();
  procedureSelectedPhysical = new Set<string>();
  private procedurePendingRequest: Record<string, unknown> | null = null;
  private reviewRequest = 0;
  private reviewDestroyed = false;
  private readonly reviewLifetime = inject(DestroyRef);
  readonly samplingPreview = signal<SamplingPreview | null>(null);
  readonly samplingBusy = signal(false);
  readonly sampleSet = signal<SampleSet | null>(null);
  readonly sampleSetLoading = signal(false);
  readonly sampleActionBusy = signal(false);
  readonly sampleSetError = signal('');
  selectionReviewComment = '';
  private readonly sampleDrafts = new Map<string, SampleTestDraft>();
  private activeEngagementId: string | null = null;
  private readonly riskLinkSelections = new Map<string, string>();
  private readonly riskLinkBases = new Map<string, string | null>();
  private readonly riskLinkSession = inject(SessionService);
  readonly attributeFields = [
    { value: 'ACCOUNT', label: 'Account code' }, { value: 'CURRENCY', label: 'Currency' },
    { value: 'DIRECTION', label: 'Debit / credit / zero direction' }, { value: 'MONTH', label: 'Transaction or posting month' },
  ];
  private previewInputFingerprint = '';
  s = { procedure: '', schedule: '', method: 'MUS', interval: '', key: '', size: null as number | null, seed: null as number | null,
    attributeFields: [] as string[], rationale: '' };
  e = { procedure: '', upload: '', note: '' };
  ph = { index: '', box: '', description: '', location: '', linkTarget: '', moveTo: '' };
  ah = { title: '', wording: '', reason: '', section: '' };
  n = { field: 'WORK_PERFORMED', excerpt: '', body: '', reply: '' };
  aggregateConclusion = '';
  readonly countBased = () => ['RANDOM', 'SYSTEMATIC', 'STRATIFIED', 'ATTRIBUTE_STRATA'].includes(this.s.method);

  constructor() {
    this.reviewLifetime.onDestroy(() => { this.reviewDestroyed = true; ++this.reviewRequest; this.review.set(null); this.reviewError.set(''); });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation(); this.ws.data();
      untracked(() => { ++this.reviewRequest; this.review.set(null); this.reviewError.set(''); });
    });
    effect(() => {
      const workspace = this.ws.data();
      untracked(() => {
        if (workspace && this.e.procedure && workspace.procedures.some(p => p.id === this.e.procedure))
          void this.selectProcedure(this.e.procedure);
      });
    });
    effect(() => {
      this.id(); this.riskLinkSession.current(); this.riskLinkSession.invalidation();
      untracked(() => { this.riskLinkSelections.clear(); this.riskLinkBases.clear(); });
    });
    effect(() => {
      const w = this.ws.data();
      untracked(() => {
        if (!w) return;
        if (!w.canManageFieldwork) this.riskLinkSelections.clear();
        for (const [id] of this.riskLinkSelections) if (!w.procedures.some(p => p.id === id)) this.riskLinkSelections.delete(id);
        for (const p of w.procedures) {
          if (this.riskLinkBases.has(p.id) && this.riskLinkBases.get(p.id) !== p.riskId) this.riskLinkSelections.delete(p.id);
          this.riskLinkBases.set(p.id, p.riskId);
        }
      });
    });
    // Seed the conclusion editor from the persisted assessment whenever the workspace reloads.
    effect(() => {
      const w = this.ws.data();
      this.ws.loading();
      const engagementId = this.id();
      untracked(() => {
        if (engagementId !== this.activeEngagementId) {
          this.activeEngagementId = engagementId;
          this.riskLinkSelections.clear();
          this.sampleSet.set(null); this.sampleSetError.set(''); this.sampleDrafts.clear();
        }
        this.aggregateConclusion = w?.aggregate?.conclusion ?? '';
        if (!w) {
          this.samplingPreview.set(null); this.previewInputFingerprint = '';
          if (this.ws.error()) { this.sampleSet.set(null); this.sampleSetError.set(''); this.sampleDrafts.clear(); }
        }
      });
    });
  }
  count(w: Fieldwork, status: string): number { return w.procedures.filter((p) => p.applicabilityStatus === status).length; }
  reviewed(w: Fieldwork): number { return w.procedures.filter((p) => p.evidenceState === 'REVIEWED_CURRENT').length; }
  riskSelection(p: Fieldwork['procedures'][number]): string { return this.riskLinkSelections.get(p.id) ?? p.riskId ?? ''; }
  setRiskSelection(procedureId: string, riskId: string): void { this.riskLink.inputChanged(); this.riskLinkSelections.set(procedureId, riskId); }
  riskLabel(w: Fieldwork, riskId: string | null): string {
    if (!riskId) return 'No risk linked';
    const risk = w.risks.find((x) => x.id === riskId);
    return risk ? `${risk.area} · ${risk.effectiveBand ?? risk.band ?? 'Not assessed'}${risk.significanceDecision === 'SIGNIFICANT' ? ' · Significant' : ''}` : 'Linked risk is outside this list';
  }
  riskFor(w: Fieldwork, riskId: string | null) { return riskId ? w.risks.find((x) => x.id === riskId) : undefined; }
  async saveRiskLink(procedureId: string): Promise<void> {
    const w = this.ws.data(), p = w?.procedures.find(p => p.id === procedureId);
    if (!w?.canManageFieldwork || !p || this.cmd.busy() || this.cmd.uncertain()) return;
    const riskId = this.riskSelection(p);
    if (riskId === (p.riskId ?? '') || (riskId && !w.risks.some(r => r.id === riskId))) return;
    await this.riskLink.prepare(() => ({ action: 'LINK_PROCEDURE_RISK', linkProcedureRisk: { procedureId,
      riskId: this.riskSelection(p) || null, expectedRiskId: p.riskId } }));
  }
  applicable(w: Fieldwork) { return w.procedures.filter((p) => p.applicabilityStatus === 'APPLICABLE'); }
  reviewable(w: Fieldwork) { return w.canManageFieldwork ? this.applicable(w) : w.procedures; }
  sections(w: Fieldwork) {
    const map = new Map<number, { key: number; title: string; count: number }>();
    for (const p of w.procedures) {
      const key = p.sourceSectionNumber ?? -2;
      const g = map.get(key) ?? { key, title: p.sourceSectionTitle ?? '', count: 0 };
      g.count++; map.set(key, g);
    }
    return [...map.values()].sort((a, b) => a.key - b.key);
  }
  firstSection(w: Fieldwork): number { return w.procedures.length ? (w.procedures[0].sourceSectionNumber ?? -2) : 0; }
  visible(w: Fieldwork) {
    const s = this.section() ?? this.firstSection(w);
    return s === 0 ? w.procedures : w.procedures.filter((p) => (p.sourceSectionNumber ?? -2) === s);
  }
  parameters(r: { interval: string | null; keyItemThreshold: string | null; sampleSize: number | null; seed: number | null; sourceDigest: string }): string {
    return [r.interval ? `interval ${r.interval}` : null, r.keyItemThreshold ? `key items ≥ ${r.keyItemThreshold}` : null, r.sampleSize !== null ? `size ${r.sampleSize}` : null,
      r.seed !== null ? `seed ${r.seed}` : null, `source ${r.sourceDigest.slice(0, 10)}`].filter((x) => x).join(', ');
  }
  itemDraft(item: SampleItem): SampleTestDraft {
    let draft = this.sampleDrafts.get(item.selectionItemId);
    if (!draft) {
      draft = { workPerformed: item.testWorkPerformed ?? '', evidenceReferences: item.evidenceReferences.join('\n'), result: item.testResult === 'PENDING' ? 'PASS' : item.testResult,
        exceptionAmount: item.exceptionAmount ?? '', contradictoryEvidence: item.contradictoryEvidence ?? '', followUp: item.followUp ?? '', reviewerComment: '' };
      this.sampleDrafts.set(item.selectionItemId, draft);
    }
    return draft;
  }
  pageStart(set: SampleSet): number { return set.totalCount ? (set.page - 1) * set.pageSize + 1 : 0; }
  pageEnd(set: SampleSet): number { return Math.min(set.page * set.pageSize, set.totalCount); }
  methodLabel(method: string): string {
    return ({ MUS: 'Monetary unit (MUS)', KEY_ITEM: 'Key item threshold', RANDOM: 'Seeded random', SYSTEMATIC: 'Systematic random',
      STRATIFIED: 'Key items plus random', ATTRIBUTE_STRATA: 'Attribute strata' } as Record<string, string>)[method] ?? method;
  }
  toggleAttributeField(field: string, checked: boolean): void {
    this.s.attributeFields = checked
      ? [...new Set([...this.s.attributeFields, field])]
      : this.s.attributeFields.filter(x => x !== field);
  }
  previewMatchesInputs(): boolean {
    return this.samplingPreview() !== null && this.previewInputFingerprint === this.samplingFingerprint();
  }
  private samplingFingerprint(): string {
    const s = this.s;
    return JSON.stringify({ procedureId: s.procedure, scheduleId: s.schedule, method: s.method.trim().toUpperCase(),
      interval: s.method === 'MUS' ? (s.interval.trim() ? decimalInput(s.interval) : null) : null,
      keyItemThreshold: ['KEY_ITEM', 'STRATIFIED'].includes(s.method) ? (s.key.trim() ? decimalInput(s.key) : null) : null,
      sampleSize: this.countBased() ? s.size : null, seed: this.countBased() ? s.seed : null,
      rationale: s.rationale.trim(), attributeFields: s.method === 'ATTRIBUTE_STRATA' ? [...s.attributeFields].sort() : null });
  }
  private samplingInput(): Record<string, unknown> | null {
    const s = this.s;
    const interval = s.method === 'MUS' && s.interval.trim() ? decimalInput(s.interval) : null;
    const keyItemThreshold = ['KEY_ITEM', 'STRATIFIED'].includes(s.method) && s.key.trim() ? decimalInput(s.key) : null;
    if (!s.procedure || !s.schedule || !s.rationale.trim()) {
      this.invalid('Choose a procedure and approved schedule, and enter a rationale.'); return null;
    }
    if (s.method === 'MUS' && (!s.interval.trim() || interval === null)) {
      this.invalid('Enter a valid monetary-unit interval.'); return null;
    }
    if (['KEY_ITEM', 'STRATIFIED'].includes(s.method) && (!s.key.trim() || keyItemThreshold === null)) {
      this.invalid('Enter a valid key-item threshold.'); return null;
    }
    if (this.countBased() && (!Number.isInteger(s.size) || (s.size ?? 0) < 1 || !Number.isInteger(s.seed))) {
      this.invalid('Enter a positive whole-number sample size and an integer seed.'); return null;
    }
    if (s.method === 'ATTRIBUTE_STRATA' && s.attributeFields.length === 0) {
      this.invalid('Choose at least one attribute field for stratified attribute sampling.'); return null;
    }
    return { procedureId: s.procedure, scheduleId: s.schedule, method: s.method, interval, keyItemThreshold,
      sampleSize: this.countBased() ? s.size : null, seed: this.countBased() ? s.seed : null, rationale: s.rationale.trim(),
      attributeFields: s.method === 'ATTRIBUTE_STRATA' ? [...s.attributeFields].sort() : null };
  }
  history(m: { toLocation: string; movedAt: string }[]): string { return m.map((x) => `${x.toLocation} (${x.movedAt.slice(0, 10)})`).join(' → '); }
  send(url: string, body: unknown, ok: string, after?: () => void): Promise<boolean> {
    return this.cmd.run(url, body, ok, after).finally(() => this.ws.reload());
  }
  private invalid(m: string): void { this.cmd.failed.set(true); this.cmd.message.set(m); }
  selectedProgram(options: ReturnType<typeof decodeProgramOptions>) {
    const selectedId = this.selectedProgramVersionId();
    return options.options.find(x => x.programVersionId === selectedId) ?? options.options.find(x => x.eligible) ?? null;
  }
  selectedProgramVersion(options: ReturnType<typeof decodeProgramOptions>): string {
    return this.selectedProgram(options)?.programVersionId ?? '';
  }
  adoptProgram(options: ReturnType<typeof decodeProgramOptions>): void {
    const selected = this.selectedProgram(options);
    if (!selected?.eligible || options.currentAdoptionId || options.currentAdoptionBlocker) return;
    void this.send(`/api/ui/engagements/${this.id()}/fieldwork/program`, {
      programVersionId: selected.programVersionId, sourceHash: selected.sourceHash,
      contentDigest: selected.contentDigest, expectedAdoptionId: options.currentAdoptionId,
    }, 'Exact published programme adopted.');
  }
  private base(): string { return `/api/ui/engagements/${this.id()}/fieldwork`; }
  applicabilityReason(procedureId: string): string { return this.applicabilityReasons()[procedureId] ?? ''; }
  setApplicabilityReason(procedureId: string, value: string): void {
    this.applicabilityReasons.update(current => ({ ...current, [procedureId]: value }));
  }
  decide(procedure: ReturnType<typeof decodeFieldwork>['procedures'][number], decision: string): void {
    const rationale = this.applicabilityReason(procedure.id).trim();
    void this.send(`/api/ui/procedures/${procedure.id}/applicability`, {
      decision, rationale: rationale || null, expectedApplicabilityRevision: procedure.applicabilityRevision,
      expectedDefinitionRevision: procedure.definitionRevision ?? 0, commandId: globalThis.crypto.randomUUID(),
      proposalDecisionId: procedure.pendingApplicabilityProposalId,
    }, 'Applicability decision recorded.');
  }
  aggregate(w: Fieldwork): void { void this.send(this.base() + '/aggregate', { conclusion: this.aggregateConclusion }, 'Aggregate conclusion recorded.'); }
  async sample(): Promise<void> {
    const input = this.samplingInput();
    if (!input) return;
    this.samplingPreview.set(null);
    this.previewInputFingerprint = '';
    this.samplingBusy.set(true);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
    try {
      const result = await this.api.command<unknown>(this.base() + '/sampling/preview', input);
      if (!result.ok) { this.cmd.failed.set(true); this.cmd.message.set(result.message); return; }
      const preview = decode(samplingPreviewDecoder, result.value);
      this.samplingPreview.set(preview);
      this.previewInputFingerprint = this.samplingFingerprint();
      this.cmd.message.set('Preview ready. Review the selected row IDs and strata before recording.');
    } catch {
      this.cmd.failed.set(true);
      this.cmd.message.set('The preview response could not be verified. Refresh the workspace and try again.');
    } finally { this.samplingBusy.set(false); }
  }
  async recordSampling(): Promise<void> {
    if (!this.previewMatchesInputs()) return this.invalid('Sampling inputs changed. Preview and review the selection again.');
    const input = this.samplingInput();
    const preview = this.samplingPreview();
    if (!input || !preview) return;
    this.samplingBusy.set(true);
    this.cmd.failed.set(false);
    this.cmd.message.set('');
    try {
      const result = await this.api.command<{ selectedCount: number; populationCount: number; coveragePercent: string; selectionId: string }>(
        this.base() + '/sampling', { ...input, expectedPreviewDigest: preview.previewDigest });
      if (result.ok) {
        this.cmd.message.set(`Recorded ${result.value.selectedCount} of ${result.value.populationCount} selected rows. The persisted selection is awaiting independent review.`);
        this.samplingPreview.set(null);
        this.previewInputFingerprint = '';
        this.sampleSet.set(null);
        this.ws.reload();
        await this.openSampleSet(result.value.selectionId);
      } else {
        this.cmd.failed.set(true);
        this.cmd.message.set(result.unknown
          ? `${result.message} Repeating the unchanged preview is idempotent.` : result.message);
        if (result.code === 'generation.stale') this.samplingPreview.set(null);
      }
    } finally { this.samplingBusy.set(false); }
  }
  async openSampleSet(selectionId: string): Promise<void> { await this.loadSampleSet(selectionId, 1); }
  async loadSampleSet(selectionId: string, page = 1): Promise<void> {
    this.sampleSetLoading.set(true); this.sampleSetError.set('');
    try {
      const set = await this.api.get(`/api/ui/selections/${selectionId}/sample-set?page=${page}&pageSize=100`, decodeSampleSet);
      this.sampleSet.set(set);
      for (const item of set.items) this.itemDraft(item);
    } catch (error) {
      this.sampleSet.set(null); this.sampleSetError.set((error as Error).message || 'The sample set is unavailable.');
    } finally { this.sampleSetLoading.set(false); }
  }
  async reviewSelection(selectionId: string, decision: 'REVIEWED' | 'CHANGES_REQUIRED'): Promise<void> {
    const comment = this.selectionReviewComment.trim();
    if (!comment) return this.invalid('Enter the independent review note before deciding this sample.');
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    const result = await this.api.command(this.baseSelection(selectionId) + '/review', { decision, comment });
    if (result.ok) {
      this.cmd.message.set(decision === 'REVIEWED' ? 'The exact sample is approved for execution.' : 'The sample was returned with the recorded review note.');
      this.selectionReviewComment = '';
      this.ws.reload();
    } else {
      this.cmd.failed.set(true);
      this.cmd.message.set(result.unknown ? `${result.message} Refresh the sample status before deciding again.` : result.message);
    }
    await this.loadSampleSet(selectionId, this.sampleSet()?.page ?? 1);
    this.sampleActionBusy.set(false);
  }
  async recordItemTest(item: SampleItem): Promise<void> {
    const draft = this.itemDraft(item);
    const evidenceReferences = draft.evidenceReferences.split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    if (!draft.workPerformed.trim()) return this.invalid('Describe the work performed for this selected item.');
    if (['PASS', 'EXCEPTION'].includes(draft.result) && evidenceReferences.length === 0)
      return this.invalid('Pass and exception results require at least one evidence reference.');
    if (['EXCEPTION', 'LIMITATION'].includes(draft.result) && !draft.followUp.trim())
      return this.invalid('Exceptions and limitations require documented follow-up.');
    let exceptionAmount: string | null = null;
    if (draft.exceptionAmount.trim()) {
      try { exceptionAmount = decimalInput(draft.exceptionAmount); }
      catch { return this.invalid('Enter the exception amount as a decimal.'); }
    }
    const set = this.sampleSet();
    if (!set) return;
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    const result = await this.api.command(this.baseItem(item.selectionItemId) + '/tests', {
      workPerformed: draft.workPerformed.trim(), evidenceReferences, result: draft.result, exceptionAmount,
      contradictoryEvidence: draft.contradictoryEvidence.trim() || null, followUp: draft.followUp.trim() || null,
    });
    if (result.ok) {
      this.cmd.message.set(`Item test recorded for ${item.stableRowId}. The current result and evidence are now in the sample history.`);
      this.sampleDrafts.delete(item.selectionItemId);
    } else {
      this.cmd.failed.set(true);
      this.cmd.message.set(result.unknown ? `${result.message} Refresh the item history before recording another revision.` : result.message);
    }
    await this.loadSampleSet(set.selectionId, set.page);
    this.sampleActionBusy.set(false);
  }
  async reviewItemTest(testId: string, decision: 'REVIEWED' | 'CHANGES_REQUIRED', selectionItemId: string): Promise<void> {
    const draft = this.sampleDrafts.get(selectionItemId);
    const comment = draft?.reviewerComment.trim() ?? '';
    if (decision === 'CHANGES_REQUIRED' && !comment) return this.invalid('Enter a reason when returning an item test for changes.');
    const set = this.sampleSet();
    if (!set) return;
    this.sampleActionBusy.set(true); this.cmd.failed.set(false); this.cmd.message.set('');
    const result = await this.api.command(this.baseItemTest(testId) + '/review', { decision, comment: comment || null });
    if (result.ok) this.cmd.message.set(decision === 'REVIEWED' ? 'Item-test result independently reviewed.' : 'Item-test result returned with the reviewer note.');
    else {
      this.cmd.failed.set(true);
      this.cmd.message.set(result.unknown ? `${result.message} Refresh the current review state before deciding again.` : result.message);
    }
    await this.loadSampleSet(set.selectionId, set.page);
    this.sampleActionBusy.set(false);
  }
  private baseSelection(selectionId: string): string { return `/api/ui/selections/${selectionId}`; }
  private baseItem(itemId: string): string { return `/api/ui/selection-items/${itemId}`; }
  private baseItemTest(testId: string): string { return `/api/ui/item-tests/${testId}`; }
  async selectProcedure(procedureId: string): Promise<void> {
    const request = ++this.reviewRequest, engagement = this.id(), workspace = this.ws.data(),
      actor = this.riskLinkSession.current(), epoch = this.riskLinkSession.invalidation();
    this.review.set(null); this.reviewError.set('');
    if (this.reviewDestroyed || !actor?.staff || !procedureId || !workspace?.procedures.some(p => p.id === procedureId)) return;
    const current = () => !this.reviewDestroyed && request === this.reviewRequest && engagement === this.id() &&
      workspace === this.ws.data() && actor === this.riskLinkSession.current() && epoch === this.riskLinkSession.invalidation();
    try {
      const review = await this.api.get(`/api/ui/procedures/${procedureId}/review`, decodeProcedureReview);
      if (!current()) return;
      if (review.procedureId !== procedureId) throw new Error('Procedure review unavailable.');
      this.review.set(review);
    } catch (e) { if (current()) this.reviewError.set((e as Error).message); }
  }

  async openProcedure(procedure: ReturnType<typeof decodeFieldwork>['procedures'][number]): Promise<void> {
    this.e.procedure = procedure.id;
    this.procedureSelectedId.set(procedure.id);
    this.procedureCommandState.set(null);
    this.procedureCommandPreview.set(null);
    this.procedureCommandReceipt.set(null);
    this.procedureCommandError.set('');
    this.procedureUnknown.set(null);
    this.procedureAssent = false;
    this.procedureWork = '';
    this.procedureConclusion = '';
    this.procedureDecision = 'REVIEWED';
    this.procedureComment = '';
    this.procedureManualReference = '';
    this.procedureObservation = '';
    this.procedureLegacyReferences = '';
    this.procedureSelectedLinks.clear();
    this.procedureSelectedPhysical.clear();
    this.procedurePendingRequest = null;
    await this.loadProcedureCommandState(procedure.id);
    await this.selectProcedure(procedure.id);
  }
  selectedProcedure(w: Fieldwork) { return w.procedures.find(p => p.id === this.procedureSelectedId()); }
  procedureInputChanged(): void {
    this.procedureCommandPreview.set(null);
    this.procedureAssent = false;
    this.procedurePendingRequest = null;
  }
  toggleProcedureLink(linkId: string, checked: boolean): void {
    if (checked) this.procedureSelectedLinks.add(linkId); else this.procedureSelectedLinks.delete(linkId);
    this.procedureInputChanged();
  }
  toggleProcedurePhysical(linkId: string, checked: boolean): void {
    if (checked) this.procedureSelectedPhysical.add(linkId); else this.procedureSelectedPhysical.delete(linkId);
    this.procedureInputChanged();
  }
  private async loadProcedureCommandState(procedureId: string): Promise<void> {
    try {
      const state = await this.api.get(`/api/ui/procedures/${procedureId}/commands/state`, decodeProcedureCommandState);
      if (this.procedureSelectedId() === procedureId) this.procedureCommandState.set(state);
    } catch (e) {
      if (this.procedureSelectedId() === procedureId) this.procedureCommandError.set((e as Error).message);
    }
  }
  private procedureEvidenceSelections(): unknown[] {
    const selections: Record<string, unknown>[] = [];
    for (const linkId of this.procedureSelectedLinks) selections.push({ kind: 'STORED_LINK', linkId });
    for (const physicalLinkId of this.procedureSelectedPhysical) selections.push({ kind: 'PHYSICAL_LINK', physicalLinkId });
    if (this.procedureManualReference.trim() || this.procedureObservation.trim())
      selections.push({ kind: 'MANUAL_REFERENCE', manualReference: this.procedureManualReference.trim(),
        observation: this.procedureObservation.trim() });
    return selections;
  }
  private procedureActionRequest(action: string): Record<string, unknown> | null {
    const state = this.procedureCommandState();
    if (!state) return null;
    const basis = state.basis, review = action === 'REVIEW_RESULT';
    return {
      requestId: globalThis.crypto.randomUUID(), action,
      expectedDefinitionRevision: basis.definitionRevision, expectedInputGeneration: basis.inputGeneration,
      expectedResultRevision: review ? basis.latestResultRevision : basis.currentResultRevision,
      resultId: review ? basis.latestResultId : null,
      workPerformed: review ? null : this.procedureWork.trim(),
      structuredResultJson: review ? null : '{}',
      evidenceReferences: review ? null : this.procedureLegacyReferences.split('\n').map(x => x.trim()).filter(x => x),
      conclusion: review ? null : this.procedureConclusion.trim(),
      decision: review ? this.procedureDecision : null,
      comment: review ? (this.procedureComment.trim() || null) : null,
      evidenceSelections: review ? null : this.procedureEvidenceSelections(),
      reviewed: false, expectedRequestHash: null,
    };
  }
  async prepareProcedureCommand(action: string): Promise<void> {
    const procedureId = this.procedureSelectedId();
    if (!procedureId || this.procedureCommandBusy()) return;
    const request = this.procedureActionRequest(action);
    if (!request) return;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    this.procedureCommandPreview.set(null);
    this.procedureAssent = false;
    try {
      const outcome = await this.api.command(`/api/ui/procedures/${procedureId}/commands/preview`, request);
      if (!outcome.ok) { this.procedurePendingRequest = null; this.procedureCommandError.set(outcome.message); return; }
      this.procedurePendingRequest = request;
      this.procedureCommandPreview.set(decode(decodeProcedureCommandPreview, outcome.value));
    } catch {
      this.procedureCommandError.set('The procedure command preview could not be verified. Refresh and review again.');
    } finally { this.procedureCommandBusy.set(false); }
  }
  async executeProcedureCommand(): Promise<void> {
    const procedureId = this.procedureSelectedId(), preview = this.procedureCommandPreview(), request = this.procedurePendingRequest;
    if (!procedureId || !preview || !request || !this.procedureAssent || this.procedureCommandBusy()) return;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    try {
      const outcome = await this.api.command(`/api/ui/procedures/${procedureId}/commands/actions`,
        { ...request, reviewed: true, expectedRequestHash: preview.requestHash });
      if (outcome.ok) {
        this.procedureCommandReceipt.set(decode(decodeProcedureCommandReceipt, outcome.value));
        this.procedureCommandPreview.set(null);
        this.procedureAssent = false;
        this.procedurePendingRequest = null;
        this.ws.reload();
        await this.loadProcedureCommandState(procedureId);
        await this.selectProcedure(procedureId);
      } else if (outcome.unknown) {
        this.procedureUnknown.set({ requestId: request['requestId'] as string, requestHash: preview.requestHash });
        this.procedureCommandError.set(outcome.message);
      } else {
        this.procedureCommandError.set(outcome.message);
      }
    } catch {
      this.procedureCommandError.set('The outcome could not be verified. Retain the request identity and recover it before retrying.');
    } finally { this.procedureCommandBusy.set(false); }
  }
  async recoverProcedureCommand(): Promise<void> {
    const procedureId = this.procedureSelectedId(), pending = this.procedureUnknown();
    if (!procedureId || !pending) return;
    this.procedureCommandBusy.set(true);
    this.procedureCommandError.set('');
    try {
      const lookup = await this.api.get(`/api/ui/procedures/${procedureId}/commands/receipts/${pending.requestId}?requestHash=${pending.requestHash}`, decodeProcedureCommandLookup);
      if (lookup.found && lookup.receipt) {
        this.procedureCommandReceipt.set(lookup.receipt);
        this.procedureUnknown.set(null);
        this.ws.reload();
        await this.loadProcedureCommandState(procedureId);
        await this.selectProcedure(procedureId);
      } else {
        this.procedureCommandError.set('The original outcome is not retained yet. Do not resend a new identity; refresh and reconcile persisted state.');
      }
    } catch (e) { this.procedureCommandError.set((e as Error).message); }
    finally { this.procedureCommandBusy.set(false); }
  }

  private refreshReview = () => { void this.selectProcedure(this.e.procedure); };
  linkEvidence(): void {
    if (!this.e.procedure || !this.e.upload) return this.invalid('Choose a procedure and a received upload.');
    void this.send(`/api/ui/procedures/${this.e.procedure}/evidence`, { uploadIntentId: this.e.upload, note: this.e.note || null }, 'Evidence linked.', this.refreshReview);
  }
  register(): void {
    const index = this.ph.index;
    void this.send(this.base() + '/physical', { fileIndex: index, boxReference: this.ph.box, description: this.ph.description, location: this.ph.location },
      `Registered ${index.trim().toUpperCase()}.`, () => (this.ph = { ...this.ph, index: '', box: '', description: '', location: '' }));
  }
  linkPhysical(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/link`, { procedureId: this.ph.linkTarget }, 'Physical file linked.'); }
  move(itemId: string): void { void this.send(`/api/ui/physical-items/${itemId}/move`, { toLocation: this.ph.moveTo }, 'Movement recorded.', () => (this.ph.moveTo = '')); }
  adHoc(): void {
    void this.send(this.base() + '/ad-hoc', { title: this.ah.title, wording: this.ah.wording, reason: this.ah.reason, sectionTitle: this.ah.section || null },
      'Ad hoc step inserted; it must be performed and reviewed before completion.', () => (this.ah = { title: '', wording: '', reason: '', section: '' }));
  }
  addNote(resultId: string): void {
    void this.send('/api/ui/review-notes', { resultId, field: this.n.field, excerpt: this.n.excerpt, body: this.n.body }, 'Note added.',
      () => { this.n = { ...this.n, excerpt: '', body: '' }; this.refreshReview(); });
  }
  noteEvent(noteId: string, kind: 'RESPONSE' | 'RESOLVED'): void {
    void this.send(`/api/ui/review-notes/${noteId}/events`, { kind, body: this.n.reply }, kind === 'RESOLVED' ? 'Note resolved.' : 'Response added.',
      () => { this.n.reply = ''; this.refreshReview(); });
  }
}
