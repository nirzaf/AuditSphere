import { Component, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { Api, CommandState, routeGuid } from '../../core/api';
import { arr, bool, dec, decimalInput, decode, guid, instant, int, nat, nullable, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const procedure = obj({ id: guid, sourceProcedureId: text, sourceSectionNumber: nullable(int), sourceSectionTitle: nullable(text), title: text,
  applicabilityStatus: text, status: text, riskId: nullable(guid) });
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
const samplingPreviewDecoder = obj({ previewDigest: text, sourceDigest: text, outcome: obj({ method: text, populationCount: nat, populationSignedTotal: dec,
  populationAbsoluteTotal: dec, selectedCount: nat, selectedAbsoluteTotal: dec, coveragePercent: dec, seed: nullable(int), interval: nullable(dec),
  keyItemThreshold: nullable(dec), items: arr(obj({ stableRowId: text, signedAmount: dec, absoluteAmount: dec, inclusionReason: text,
    cumulativeAbsoluteAmount: dec }), 100000),
  strata: nullable(arr(obj({ stratumKey: text, populationCount: nat, slots: nat, selectedCount: nat }), 10000)) }) });
export const decodeProcedureReview = obj({ procedureId: guid,
  currentResult: nullable(obj({ id: guid, revision: nat, workPerformed: text, conclusion: nullable(text) })),
  notes: arr(obj({ noteId: guid, resultRevision: nat, field: text, excerpt: text, startOffset: int, body: text, authorName: text, createdAt: instant, open: bool,
    events: arr(obj({ id: guid, kind: text, body: text, createdAt: instant }), 500) }), 500),
  evidence: arr(obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant }), 1000) });
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

@Component({
  selector: 'audit-fieldwork',
  imports: [FormsModule, RouterLink, MatButtonModule, MatTabsModule, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Audit fieldwork</span></nav>
    <audit-page-header title="Controlled audit fieldwork" eyebrow="Engagement audit" [description]="ws.data()?.canManageFieldwork ? 'Fieldwork procedures, differences, and aggregate conclusion on this engagement.' : 'Review submitted procedure results and resolve review notes for this engagement.'" />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="fieldwork coverage" />
    @if (ws.data(); as w) {
      @if (w.program) { <p role="status">{{ w.procedures.length }} adopted procedures · {{ count(w, 'APPLICABLE') }} applicable · {{ reviewed(w) }} reviewed</p> }
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
          <p class="actions"><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Open planning</a><a routerLink="/app/audit/library">Program library</a><a [routerLink]="['/app/engagements', w.engagementId, 'completion']">Open completion</a></p>
        } @else {
          <p>No published program has been adopted for this engagement.</p>
          <button matButton="filled" (click)="send('/api/ui/engagements/' + w.engagementId + '/fieldwork/program', {}, 'Program published and adopted.')" [disabled]="cmd.busy()">Publish and adopt {{ w.catalogVersion }}</button>
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
            <thead><tr><th scope="col">Source</th><th scope="col">Section</th><th scope="col">Procedure</th><th scope="col">Applicability</th><th scope="col">Linked risk</th><th scope="col">Evidence</th><th scope="col">Action</th></tr></thead>
            <tbody>@for (p of visible(w); track p.id) {
              <tr [id]="'procedure-' + p.id"><td><code>{{ p.sourceProcedureId }}</code></td><td>{{ p.sourceSectionTitle }}</td><td>{{ p.title }}</td><td>{{ p.applicabilityStatus }}</td>
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
                      <button matButton (click)="saveRiskLink(p.id)" [disabled]="cmd.busy() || riskSelection(p) === (p.riskId ?? '')">Save risk link</button>
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
                <td>@switch (p.applicabilityStatus) {
                  @case ('PENDING') { <span class="actions">
                    <button matButton="filled" (click)="decide(p.id, 'APPLICABLE')" [disabled]="cmd.busy()">Applicable</button>
                    <button matButton="filled" (click)="decide(p.id, 'NA_PENDING_REVIEW')" [disabled]="cmd.busy()">N/A for review</button></span> }
                  @case ('APPLICABLE') { <small>Execute from source workflow</small> }
                  @default { <small>Reviewer disposition required</small> } }</td></tr> }</tbody>
          </table></div>
        </section>
      }
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class AuditFieldwork {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), decodeFieldwork,
    'Fieldwork unavailable: an authorized internal engagement scope is required.');
  readonly cmd = new CommandState(this.api);
  readonly section = signal<number | null>(null);
  readonly toolTab = signal(0);
  readonly review = signal<ReturnType<typeof decodeProcedureReview> | null>(null);
  readonly reviewError = signal('');
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
  reviewed(w: Fieldwork): number { return w.procedures.filter((p) => p.status === 'REVIEWED').length; }
  riskSelection(p: Fieldwork['procedures'][number]): string { return this.riskLinkSelections.get(p.id) ?? p.riskId ?? ''; }
  setRiskSelection(procedureId: string, riskId: string): void { this.riskLinkSelections.set(procedureId, riskId); }
  riskLabel(w: Fieldwork, riskId: string | null): string {
    if (!riskId) return 'No risk linked';
    const risk = w.risks.find((x) => x.id === riskId);
    return risk ? `${risk.area} · ${risk.effectiveBand ?? risk.band ?? 'Not assessed'}${risk.significanceDecision === 'SIGNIFICANT' ? ' · Significant' : ''}` : 'Linked risk is outside this list';
  }
  riskFor(w: Fieldwork, riskId: string | null) { return riskId ? w.risks.find((x) => x.id === riskId) : undefined; }
  saveRiskLink(procedureId: string): void {
    const riskId = this.riskLinkSelections.get(procedureId) ?? '';
    void this.send(`/api/ui/procedures/${procedureId}/risk`, { riskId: riskId || null },
      'Procedure risk link saved. Submitted results must be checked against the refreshed planning basis.');
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
  private base(): string { return `/api/ui/engagements/${this.id()}/fieldwork`; }
  decide(procedureId: string, decision: string): void { void this.send(`/api/ui/procedures/${procedureId}/applicability`, { decision }, 'Recorded.'); }
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
    this.review.set(null); this.reviewError.set('');
    if (!procedureId) return;
    try { this.review.set(await this.api.get(`/api/ui/procedures/${procedureId}/review`, decodeProcedureReview)); } catch (e) { this.reviewError.set((e as Error).message); }
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
