import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api, CommandState, routeGuid } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { arr, bool, dec, decode, guid, instant, int, nat, nullable, obj, oneOf, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { EqrDecision } from './eqr-decision';
import { GoingConcern } from './going-concern';
import { SubsequentEvents } from './subsequent-events';
import { OpeningBalancesPanel } from './opening-balances';

const deliverable = obj({ id: guid, kind: text, title: text, version: nat, signed: bool, current: bool, contentSha256: text, createdAt: instant,
  signatureKind: oneOf('NONE', 'VISUAL', 'CERTIFICATE'), certificateSubject: nullable(text) });
/** Result of re-checking a signed report against its own certificate signature (ADR-0016). */
export const decodeSignatureCheck = obj({ signatureKind: oneOf('VISUAL', 'CERTIFICATE'), state: oneOf('NOT_CERTIFICATE_SIGNED', 'VALID', 'ALTERED', 'UNREADABLE'),
  subject: nullable(text), thumbprintSha256: nullable(text), message: text });
export const decodeCompletion = obj({ engagementId: guid,
  gates: arr(obj({ name: text, status: text, tone: text, authority: nullable(text), date: nullable(instant) }), 50),
  representations: arr(obj({ id: guid, code: text, title: text, narrative: text, obtained: bool, obtainedAt: nullable(instant), signatoryName: nullable(text) }), 500),
  packageId: nullable(guid), packageStatus: text, packageEvidence: nullable(obj({ current: bool, message: text })),
  packageReviews: arr(obj({ id: guid, financialPackageId: guid, packageRevision: nat, packageGeneration: nat, packageHash: text,
    financialPackageArtifactId: guid, artifactVersion: text, artifactSha256Hex: text, presentationRevisionId: nullable(guid),
    presentationCompositionId: nullable(guid), compositionManifestDigest: nullable(text), stage: text, decision: text, evidenceMode: text,
    evidenceReference: text, decidedByUserId: nullable(guid), decidedAt: instant, sequence: nat }), 1000),
  partnerApproved: bool, eqrStatus: text, releaseCandidateId: nullable(guid),
  release: nullable(obj({ candidateId: guid, candidateStatus: oneOf('READY', 'ISSUED'), targetKind: text, targetRevision: nat, candidateRevision: nat,
    manifestDigest: text, candidateCreatedAt: instant, checkpointId: nullable(guid), checkpointState: oneOf('ABSENT', 'VERIFIED', 'PENDING', 'MISMATCHED', 'EXPIRED'),
    checkpointVerifiedAt: nullable(instant), checkpointMatchesCandidate: bool, checkpointReadBackMatchesManifest: bool, releaseId: nullable(guid),
    releasedAt: nullable(instant), releaseCheckpointId: nullable(guid), releaseManifestDigest: nullable(text), releaseMatchesCandidate: bool,
    issueState: oneOf('NOT_ISSUED', 'ISSUED', 'RECONCILIATION_REQUIRED'),
    deliveryDisposition: oneOf('NOT_APPLICABLE', 'AWAITING_AUTHORIZATION_OR_CONFIGURATION', 'OUTCOME_UNCONFIRMED') })), canPrepareRelease: bool,
  confirmations: arr(obj({ caseId: guid, type: text, respondent: text, bookedAmount: dec, currency: text, status: text, monitoring: text,
    daysSinceDispatch: nullable(int), critical: bool, criticalityRationale: nullable(text) }), 2000),
  deliverables: arr(deliverable, 500), clearance: nullable(obj({ id: guid, summaryReviewMemorandumId: guid, partnerUserId: guid, clearedAt: instant })),
  opinion: nullable(obj({ id: guid, partnerClearanceId: guid, decidedByUserId: guid, decidedAt: instant, opinionType: text, label: text, focusArea: nullable(text) })),
  opinionAreas: arr(obj({ id: guid, code: text, name: text }), 2000),
  shared: arr(obj({ title: text, openComments: arr(obj({ id: guid, body: text }), 500) }), 500),
  signedLetters: arr(obj({ id: guid, deliverableId: guid, version: nat, managementSignatory: text, contentSha256: text, uploadedAt: instant, verified: bool, current: bool }), 100),
  bundles: nullable(obj({ blockers: arr(text, 100), bundles: arr(obj({ id: guid, sha256: text, assembledAt: instant }), 100) })),
  opinionTypes: arr(text, 10),
  freeze: nullable(obj({ state: text, reportSignedAt: instant, dueAt: instant, frozenAt: nullable(instant), externalReadOnly: text, daysRemaining: int,
    amendments: arr(obj({ id: guid, reason: text, openedAt: nullable(instant), closedAt: nullable(instant) }), 500) })),
  freezeDays: nat, trail: nullable(arr(obj({ at: instant, kind: text, actor: text, description: text, entityId: guid }), 20000)), trailCoverageNote: text,
  locks: arr(obj({ id: guid, documentKey: text, lockedBy: text, lockedAt: instant }), 500),
  holdingLetter: nullable(obj({ deliverableId: nullable(guid), issuedAt: nullable(instant), outstandingCount: nat, state: text, message: text })),
  workprogramApproval: nullable(obj({ hasApproval: bool, isCurrent: bool, approvedAt: nullable(instant), canApprove: bool })),
  reportSigning: obj({ mode: oneOf('VISUAL', 'CERTIFICATE'), required: bool, ready: bool, subject: nullable(text), notAfter: nullable(instant), message: text }) });
/** Archive readiness reviewed before an early compliance lock (STE 4.4.3). */
const archiveReadiness = obj({ freezeId: guid, revision: nat, state: text, dueAt: instant, archiveReadinessDigest: nullable(text), blockers: arr(text, 50) });
const fieldworkBasis = obj({ engagementId: guid, program: nullable(obj({ programCode: text, version: text, sourceHash: text })),
  procedures: arr(obj({ sourceProcedureId: text, title: text, applicabilityStatus: text, status: text }), 5000), canManageFieldwork: bool });
const varianceRecord = obj({ id: guid, clientId: guid, engagementId: guid, procedureId: nullable(guid), accountArea: text, periodReference: text,
  expectedAmount: dec, actualAmount: dec, differenceAmount: dec, differencePercent: nullable(dec), investigationThreshold: dec,
  exceedsThreshold: bool, explanation: nullable(text), conclusion: oneOf('EXPLAINED', 'UNEXPLAINED', 'CORROBORATED'), currency: text, evidenceReferences: arr(text, 100), revision: text,
  recordedByUserId: guid, reviewedByUserId: nullable(guid), recordedAt: instant, reviewedAt: nullable(instant), reviewedRevision: nullable(text) });
const varianceStateDecoder = obj({ engagementId: guid, clientId: guid, accountArea: text, periodReference: text, expectedRevision: text,
  reviewBasis: text, record: nullable(varianceRecord), canPrepare: bool, canReview: bool, blockers: arr(text, 100) });
const variancePreviewDecoder = obj({ requestId: guid, action: oneOf('PREPARE', 'REVIEW'), requestHash: text, reviewBasis: text,
  expectedRevision: text, canProceed: bool, blockers: arr(text, 100), currentRecord: nullable(varianceRecord), summary: text });
const varianceReceiptDecoder = obj({ id: guid, engagementId: guid, varianceInvestigationId: guid, actorId: guid, actorEpoch: text,
  requestId: guid, requestHash: text, action: oneOf('PREPARE', 'REVIEW'), createdAt: instant, preview: variancePreviewDecoder, result: varianceRecord });
const varianceLookupDecoder = obj({ found: bool, receipt: nullable(varianceReceiptDecoder) });
const isCanonicalLong = (value: string) => /^(0|[1-9][0-9]{0,18})$/.test(value) && BigInt(value) <= 9223372036854775807n;
const serverTrim = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
type VarianceRecord = ReturnType<typeof varianceRecord>;
type VarianceState = ReturnType<typeof varianceStateDecoder>;
type VariancePreview = ReturnType<typeof variancePreviewDecoder>;
type VarianceReceipt = ReturnType<typeof varianceReceiptDecoder>;
type VarianceOwner = { firmId: string; userId: string; generation: string; epoch: number; engagementId: string };
type VarianceFields = { procedureId: string | null; accountArea: string; periodReference: string; expectedAmount: string; actualAmount: string;
  investigationThreshold: string; currency: string; explanation: string | null; evidenceReferences: string[] };
type VarianceIntent = { action: 'PREPARE' | 'REVIEW'; body: { requestId: string; expectedRevision: string; reviewBasis: string; fields: VarianceFields | null };
  state: VarianceState; preview: VariancePreview; basisContentHash: string | null; resultContentHash: string };
type VarianceContentIdentity = { clientId: string; engagementId: string; procedureId: string | null; accountArea: string; periodReference: string;
  expectedAmount: string; actualAmount: string; differenceAmount: string; differencePercent: string | null; investigationThreshold: string;
  exceedsThreshold: boolean; explanation: string | null; conclusion: VarianceRecord['conclusion']; currency: string; evidenceReferences: string[] };
type VariancePending = { version: 2; firmId: string; actorId: string; engagementId: string; accountArea: string; periodReference: string;
  requestId: string; requestHash: string; action: 'PREPARE' | 'REVIEW'; varianceId: string | null; actorEpoch: string;
  expectedRevision: string; reviewBasis: string; basisContentHash: string | null; resultContentHash: string };
const canonicalFixed = (value: string, scale: number): string | null => {
  const match = /^(-?)(\d{1,13})(?:\.(\d{1,6}))?$/.exec(value);
  if (!match || (match[3]?.length ?? 0) > scale) return null;
  const integer = match[2].replace(/^0+(?=\d)/, '');
  const fraction = (match[3] ?? '').padEnd(scale, '0');
  const nonZero = /[1-9]/.test(integer + fraction);
  return `${match[1] === '-' && nonZero ? '-' : ''}${integer}${scale ? `.${fraction}` : ''}`;
};
const scaledInteger = (canonical: string, scale: number): bigint => {
  const negative = canonical.startsWith('-');
  const unsigned = negative ? canonical.slice(1) : canonical;
  const [integer, fraction = ''] = unsigned.split('.');
  const value = BigInt(integer) * (10n ** BigInt(scale)) + BigInt(fraction || '0');
  return negative ? -value : value;
};
const fixedFromScaled = (value: bigint, scale: number): string => {
  const negative = value < 0n;
  const unsigned = negative ? -value : value;
  const factor = 10n ** BigInt(scale);
  const integer = (unsigned / factor).toString();
  const fraction = (unsigned % factor).toString().padStart(scale, '0');
  return `${negative && unsigned !== 0n ? '-' : ''}${integer}${scale ? `.${fraction}` : ''}`;
};
const roundedRatioToEven = (numerator: bigint, denominator: bigint): bigint => {
  const quotient = numerator / denominator, remainder = numerator % denominator;
  const twiceRemainder = (remainder < 0n ? -remainder : remainder) * 2n;
  if (twiceRemainder > denominator || twiceRemainder === denominator && (quotient < 0n ? -quotient : quotient) % 2n === 1n)
    return quotient + (numerator < 0n ? -1n : 1n);
  return quotient;
};
type Completion = ReturnType<typeof decodeCompletion>;
const OPINION_LABELS: Record<string, string> = { UNMODIFIED: 'Clean (unmodified)', QUALIFIED: 'Qualified', ADVERSE: 'Adverse', DISCLAIMER: 'Disclaimer of opinion' };
const REPORTS: [string, string][] = [['AUDIT_FINDINGS_REPORT', 'Audit Findings Report'], ['MANAGEMENT_LETTER', 'Management Letter'],
  ['REPRESENTATION_LETTER', 'Representation Letter'], ['INDEPENDENT_AUDITORS_REPORT', "Independent Auditor's Report"]];

@Component({
  selector: 'audit-completion',
  imports: [FormsModule, RouterLink, MatButtonModule, EqrDecision, GoingConcern, SubsequentEvents, OpeningBalancesPanel, ...SHARED],
  template: `
    <nav aria-label="Breadcrumb"><a routerLink="/app">Portfolio</a> / <a [routerLink]="['/app/engagements', id()]">Engagement</a> / <span>Completion</span></nav>
    <audit-page-header title="Engagement completion checklist" eyebrow="Engagement audit" description="Completion gates, unresolved items, and the exact state required before release." />
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="completion state" />
    @if (id(); as engagementId) { <audit-going-concern [engagementId]="engagementId" (refreshed)="ws.reload()" /><audit-subsequent-events [engagementId]="engagementId" (refreshed)="ws.reload()" /><audit-opening-balances [engagementId]="engagementId" (refreshed)="ws.reload()" /> }
    <section class="panel" aria-labelledby="variance-investigation-heading" data-variance-investigation>
      <h2 id="variance-investigation-heading">Analytical completion variance investigation</h2>
      <p>This is the variance investigation used by the completion gate. Preparation and review remain separate decisions; this panel does not make an accounting or audit conclusion for you.</p>
      <p>Use identifiers for current exact-engagement evidence; other uploads do not become eligible by implication. <a [routerLink]="['/app/engagements', id(), 'audit-fieldwork']">Open fieldwork evidence</a>.</p>
      <div class="form-grid">
        <label>Account area<input name="varianceArea" maxlength="80" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.accountArea" (ngModelChange)="varianceSelectionChanged()" /></label>
        <label>Period reference<input name="variancePeriod" maxlength="60" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.periodReference" (ngModelChange)="varianceSelectionChanged()" /></label>
      </div>
      <button matButton="outlined" type="button" (click)="loadVarianceState()" [disabled]="varianceBusy() || !varianceForm.accountArea.trim() || !varianceForm.periodReference.trim()">Load exact variance basis</button>
      @if (varianceLoading()) { <p role="status">Loading the exact engagement variance basis…</p> }
      @if (varianceError()) { <p role="alert">{{ varianceError() }}</p> }
      @if (varianceMessage()) { <p role="status">{{ varianceMessage() }}</p> }
      @if (varianceRecoveryUnavailable()) { <p role="alert">The unresolved variance recovery reference is unreadable or unavailable. No replacement request will be sent.</p> }
      @if (variancePending(); as pending) {
        <div role="alert" data-variance-pending-reference>
          <p>An earlier {{ pending.action.toLowerCase() }} request remains unresolved for {{ pending.accountArea }} / {{ pending.periodReference }}. No business content is stored in this reference and no request will be resent automatically.</p>
          <p>Request <code>{{ pending.requestId }}</code> · hash <code>{{ pending.requestHash }}</code></p>
          @if (varianceState()?.accountArea === pending.accountArea && varianceState()?.periodReference === pending.periodReference) {
            <button matButton="outlined" type="button" (click)="lookupVarianceRequest()" [disabled]="varianceBusy()">Look up this exact request</button>
          } @else {
            <button matButton="outlined" type="button" (click)="loadPendingVarianceScope()" [disabled]="varianceBusy()">Load pending request scope</button>
          }
        </div>
      }
      @if (varianceState(); as state) {
        @if (state.engagementId !== id() || state.accountArea !== varianceForm.accountArea.trim().toUpperCase() || state.periodReference !== varianceForm.periodReference.trim().toUpperCase()) {
          <p role="alert">The displayed variance basis does not match the current engagement, account area and period. Load the exact basis again.</p>
        } @else {
          @if (varianceBasisCurrent()) {
            <p>Current basis revision {{ state.expectedRevision }} · scope {{ state.accountArea }} / {{ state.periodReference }}. Review basis <code>{{ state.reviewBasis }}</code>. Permissions below are server-derived and are rechecked when a command is submitted.</p>
          } @else {
            <p role="alert">This is the pre-submission basis only. A request may have changed the authoritative record; it is not presented as current until refreshed.</p>
          }
          @if (state.blockers.length) { <div role="alert"><strong>{{ varianceBasisCurrent() ? 'Current variance blockers' : 'Blockers from the pre-submission basis' }}</strong><ul>@for (blocker of state.blockers; track blocker) { <li>{{ blocker }}</li> }</ul></div> }
          @if (state.record; as record) {
            <dl class="facts" [attr.data-variance-record]="record.id">
              <dt>Variance identity and revision</dt><dd><code>{{ record.id }}</code> · {{ record.revision }}</dd>
              <dt>Expected</dt><dd>{{ record.expectedAmount }} {{ record.currency }}</dd><dt>Actual</dt><dd>{{ record.actualAmount }} {{ record.currency }}</dd>
              <dt>Difference</dt><dd>{{ record.differenceAmount }} {{ record.currency }}@if (record.differencePercent !== null) { · {{ record.differencePercent }}% }</dd>
              <dt>Investigation threshold</dt><dd>{{ record.investigationThreshold }} {{ record.currency }} · {{ record.exceedsThreshold ? 'exceeded' : 'not exceeded' }}</dd>
              <dt>Investigation conclusion</dt><dd>{{ record.conclusion }}</dd><dt>Explanation</dt><dd>{{ record.explanation || 'No explanation recorded' }}</dd>
              <dt>Evidence references</dt><dd>@for (reference of record.evidenceReferences; track reference) { <code>{{ reference }}</code><br /> } @empty { None recorded }</dd>
              <dt>Preparation identity</dt><dd><code>{{ record.recordedByUserId }}</code> · {{ record.recordedAt }}</dd>
              <dt>Independent review</dt><dd>@if (isVarianceReviewCurrent(record)) { Currently reviewed by <code>{{ record.reviewedByUserId }}</code> at {{ record.reviewedAt }} for revision {{ record.revision }}. } @else if (record.reviewedByUserId || record.reviewedRevision !== null) { Historical review metadata does not establish review of this current revision. } @else { Not independently reviewed. }</dd>
            </dl>
          } @else { <p>No variance investigation exists for this exact account area and period.</p> }
          @if (!variancePending() && state.canPrepare && !variancePreview()) {
            <h3>Prepare or revise the investigation</h3>
            <div class="form-grid">
              <label>Procedure ID (optional, exact scope)<input name="varianceProcedure" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.procedureId" (ngModelChange)="clearVarianceAssent()" /></label>
              <label>Expected amount<input name="varianceExpected" inputmode="decimal" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.expectedAmount" (ngModelChange)="clearVarianceAssent()" /></label>
              <label>Actual amount<input name="varianceActual" inputmode="decimal" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.actualAmount" (ngModelChange)="clearVarianceAssent()" /></label>
              <label>Investigation threshold<input name="varianceThreshold" inputmode="decimal" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.investigationThreshold" (ngModelChange)="clearVarianceAssent()" /></label>
              <label>Currency<input name="varianceCurrency" maxlength="3" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.currency" (ngModelChange)="clearVarianceAssent()" /></label>
              <label>Investigation explanation<textarea name="varianceExplanation" maxlength="4000" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.explanation" (ngModelChange)="clearVarianceAssent()"></textarea></label>
              <label>Exact evidence references (one per line)<textarea name="varianceEvidence" maxlength="20000" [disabled]="varianceBusy()" [(ngModel)]="varianceForm.evidenceReferences" (ngModelChange)="clearVarianceAssent()"></textarea></label>
            </div>
            <button matButton="filled" type="button" (click)="previewVariancePreparation()" [disabled]="varianceBusy() || !varianceInputsValid()">Review preparation preview</button>
          }
          @if (!variancePending() && state.canReview && state.record && !variancePreview()) {
            <p>A different authorized reviewer may review this exact record after checking its current evidence and basis.</p>
            <button matButton="filled" type="button" (click)="previewVarianceReview()" [disabled]="varianceBusy()">Review current variance record</button>
          }
          @if (variancePreview(); as preview) {
            <section aria-label="Exact variance command preview" data-variance-preview>
              <h3>{{ preview.action === 'PREPARE' ? 'Preparation' : 'Independent review' }} preview</h3>
              <p>{{ preview.summary }}</p><p>Request <code>{{ preview.requestId }}</code> · hash <code>{{ preview.requestHash }}</code></p>
              <p>Expected basis revision {{ preview.expectedRevision }} · review basis <code>{{ preview.reviewBasis }}</code></p>
              @if (varianceIntentFields(); as intentFields) {
                <dl class="facts"><dt>Exact account area / period</dt><dd>{{ intentFields.accountArea }} / {{ intentFields.periodReference }}</dd>
                  <dt>Expected / actual / threshold</dt><dd>{{ intentFields.expectedAmount }} / {{ intentFields.actualAmount }} / {{ intentFields.investigationThreshold }} {{ intentFields.currency }}</dd>
                  <dt>Explanation</dt><dd>{{ intentFields.explanation || 'None supplied' }}</dd>
                  <dt>Exact evidence references</dt><dd>@for (reference of intentFields.evidenceReferences; track reference) { <code>{{ reference }}</code><br /> } @empty { None supplied }</dd>
                </dl>
              }
              @if (preview.blockers.length) { <ul>@for (blocker of preview.blockers; track blocker) { <li>{{ blocker }}</li> }</ul> }
              <label><input type="checkbox" name="varianceAssent" [disabled]="varianceBusy()" [(ngModel)]="varianceAssent" />I reviewed this exact preview and intend to submit this one request.</label>
              <button matButton="filled" type="button" (click)="submitVarianceCommand()" [disabled]="varianceBusy() || !varianceAssent || !preview.canProceed">Submit this exact request once</button>
              <button matButton="outlined" type="button" (click)="cancelVariancePreview()" [disabled]="varianceBusy()">Discard preview</button>
            </section>
          }
        }
      }
    </section>
    @if (confirmedEqrDecision(); as decisionId) {
      <p role="status" data-eqr-confirmation>The server confirmed EQR decision {{ decisionId }}. Current concurrence is not assumed; the completion state is refreshed separately.</p>
    }
    @if (ws.data(); as w) {
      <p class="actions" aria-label="Completion navigation"><a [routerLink]="['/app/engagements', w.engagementId, 'audit-plan']">Audit plan</a>
        <a [routerLink]="['/app/engagements', w.engagementId, 'audit-fieldwork']">Fieldwork control center</a><a [routerLink]="['/app/engagements', w.engagementId]">Engagement</a></p>

      <section class="panel" aria-labelledby="release-state-heading">
        <h2 id="release-state-heading">Final release evidence</h2>
        <p>Financial package: @if (w.packageId) { <code>{{ w.packageId }}</code> · {{ w.packageStatus }} } @else { No package recorded. }</p>
        @if (w.packageEvidence; as evidence) {
          <p [attr.role]="evidence.current ? 'status' : 'alert'" data-package-evidence><strong>Current package release evidence: {{ evidence.current ? 'Verified' : 'Not established' }}.</strong> {{ evidence.message }}</p>
          @if (!evidence.current) { <p>Recorded approvals remain historical evidence; they do not establish that the package is currently ready for release.</p> }
        }
        <h3>Financial package review decisions</h3>
        @if (w.packageReviews.length) {
          <div class="table-scroll"><table aria-label="Exact financial package review decisions">
            <thead><tr><th scope="col">Decision identity</th><th scope="col">Package basis</th><th scope="col">Presentation and artifact</th><th scope="col">Recorded decision</th></tr></thead>
            <tbody>@for (r of w.packageReviews; track r.id) {
              <tr [attr.data-package-review]="r.id">
                <td><strong>{{ r.stage }} · {{ r.decision }}</strong><br />Review ID <code>{{ r.id }}</code><br />Reviewer user <code>{{ r.decidedByUserId ?? 'Not recorded' }}</code></td>
                <td>Package <code>{{ r.financialPackageId }}</code><br />Revision {{ r.packageRevision }} · generation {{ r.packageGeneration }}<br />Hash <code>{{ r.packageHash }}</code></td>
                <td>Artifact <code>{{ r.financialPackageArtifactId }}</code> · {{ r.artifactVersion }} · <code>{{ r.artifactSha256Hex }}</code><br />Presentation revision {{ r.presentationRevisionId ?? 'Not recorded' }}<br />Composition {{ r.presentationCompositionId ?? 'Not recorded' }}<br />Manifest {{ r.compositionManifestDigest ?? 'Not recorded' }}</td>
                <td>{{ r.evidenceMode }} · {{ r.evidenceReference }}<br />Sequence {{ r.sequence }} · <time [attr.datetime]="r.decidedAt">{{ r.decidedAt }}</time></td>
              </tr>
            }</tbody>
          </table></div>
        } @else { <p>No current package review decisions are recorded.</p> }
        @if (w.release; as r) {
          <dl class="facts">
            <dt>Candidate</dt><dd><a [routerLink]="['/app/releases', r.candidateId]">{{ r.candidateId }}</a> · {{ r.candidateStatus }} · {{ r.targetKind }} revision {{ r.targetRevision }} (candidate revision {{ r.candidateRevision }})</dd>
            <dt>Candidate manifest SHA-256</dt><dd><code>{{ r.manifestDigest }}</code></dd>
            <dt>External checkpoint</dt><dd><audit-status [value]="r.checkpointState" /> {{ r.checkpointId ?? 'No checkpoint recorded' }}@if (r.checkpointVerifiedAt) { · checked {{ r.checkpointVerifiedAt.slice(0, 16).replace('T', ' ') }} UTC }</dd>
            <dt>Checkpoint bound to candidate</dt><dd>{{ r.checkpointMatchesCandidate ? 'Yes' : 'No' }} · read-back matches manifest: {{ r.checkpointReadBackMatchesManifest ? 'Yes' : 'No' }}</dd>
            <dt>Release issue</dt><dd><audit-status [value]="r.issueState" />@if (r.releaseId) { {{ r.releaseId }} · {{ r.releasedAt?.slice(0, 16)?.replace('T', ' ') }} UTC }</dd>
            <dt>Delivery disposition</dt><dd><audit-status [value]="r.deliveryDisposition" /></dd>
            @if (r.releaseManifestDigest) { <dt>Issued manifest SHA-256</dt><dd><code>{{ r.releaseManifestDigest }}</code></dd> }
            @if (r.releaseCheckpointId) { <dt>Issued checkpoint identity</dt><dd>{{ r.releaseCheckpointId }}</dd> }
          </dl>
          @if (r.issueState === 'NOT_ISSUED') { <p role="status">A candidate and checkpoint do not by themselves mean the package was released. This engagement has no persisted release record for the candidate.</p> }
          @if (r.issueState === 'RECONCILIATION_REQUIRED') { <p role="alert">The candidate, checkpoint, and persisted release facts do not agree. Review the exact candidate before taking a release action.</p> }
          @if (r.deliveryDisposition === 'AWAITING_AUTHORIZATION_OR_CONFIGURATION') {
            <p role="status" data-release-delivery-disposition>Release is issued; delivery is awaiting authorization or configuration. No provider delivery or recipient receipt is recorded.</p>
          }
          @if (r.deliveryDisposition === 'OUTCOME_UNCONFIRMED') {
            <p role="alert" data-release-delivery-disposition>Delivery outcome is unconfirmed. Do not treat a delivery intent as provider delivery or recipient receipt.</p>
          }
        } @else {
          <p>No release candidate is recorded for this engagement.</p>
        }
      </section>

      <section class="panel" aria-labelledby="deliverables-heading">
        <h2 id="deliverables-heading">Review, opinion and deliverables</h2>
        <h3>Third-party confirmations</h3><a [routerLink]="['/app/engagements',id(),'confirmations']">Open confirmation dashboard</a>
        <div class="table-scroll"><table aria-label="Confirmations dashboard">
          <thead><tr><th>Type</th><th>Respondent</th><th class="number">Amount</th><th>Monitoring</th><th>Critical</th><th><span class="sr-only">Action</span></th></tr></thead>
          <tbody>@for (c of w.confirmations; track c.caseId) {
            <tr><td>{{ c.type }}</td><td>{{ c.respondent }}</td><td class="number">{{ c.bookedAmount | money }} {{ c.currency }}</td>
              <td><audit-status [value]="c.monitoring" />{{ c.daysSinceDispatch !== null ? ' ' + c.daysSinceDispatch + ' day(s)' : '' }}</td><td>{{ c.critical ? 'Critical' : '—' }}</td>
              <td><a [routerLink]="['/app/engagements',id(),'confirmations']">Review criticality and evidence</a></td></tr>
          } @empty { <tr><td colspan="6">No confirmations are recorded.</td></tr> }</tbody>
        </table></div>

        <h3>Holding letter for critical confirmations (STE 3.3)</h3>
        @if (w.holdingLetter; as h) {
          <p><audit-status [value]="h.state" /> {{ h.message }}</p>
          @if (h.issuedAt) { <p><small>Latest holding letter issued {{ h.issuedAt.slice(0, 16).replace('T', ' ') }}, covering {{ h.outstandingCount }} outstanding critical confirmation(s).</small></p> }
          @if (h.state === 'READY_TO_DISPATCH') {
            <button matButton="filled" (click)="dispatchHoldingLetter()" [disabled]="cmd.busy()">Dispatch holding letter</button>
          }
        }

        <h3>Summary Review Memorandum and Partner clearance</h3>
        <section aria-labelledby="workprogram-approval-heading" class="workprogram-approval">
          <h4 id="workprogram-approval-heading">Manager workprogramme approval</h4>
          <p>This is a distinct Manager approval of the current applicable programme after independent procedure review. It does not adopt a programme or replace Partner clearance.</p>
          @if (programBasis.loading()) { <p role="status">Loading the current applicable workprogramme set…</p> }
          @else if (programBasis.error()) { <p role="alert">The current review set could not be verified: {{ programBasis.error() }}</p> }
          @else if (programBasis.data(); as basis) {
            @if (basis.engagementId !== w.engagementId) { <p role="alert">The programme belongs to a different engagement. Refresh before approving.</p> }
            @else if (!basis.program) { <p role="alert">Approval blocked: no adopted workprogramme is available for this engagement.</p> }
            @else {
              <p>Current adopted programme: <strong>{{ basis.program.programCode }} v{{ basis.program.version }}</strong> · source <code>{{ basis.program.sourceHash }}</code> ·
                {{ applicableWorkprogrammes(basis).length }} applicable workprogramme(s) · {{ reviewedWorkprogrammes(basis) }} independently reviewed.</p>
              <div class="table-scroll"><table aria-label="Current applicable workprogramme review set">
                <thead><tr><th scope="col">Procedure</th><th scope="col">Workprogramme</th><th scope="col">Applicability</th><th scope="col">Independent review</th></tr></thead>
                <tbody>@for (p of applicableWorkprogrammes(basis); track p.sourceProcedureId) {
                  <tr><td>{{ p.sourceProcedureId }}</td><td>{{ p.title }}</td><td>{{ p.applicabilityStatus }}</td><td><audit-status [value]="p.status" /></td></tr>
                } @empty { <tr><td colspan="4">No currently applicable workprogrammes are available.</td></tr> }</tbody>
              </table></div>
              @if (unreviewedWorkprogrammes(basis).length) {
                <div role="alert"><strong>Approval blockers</strong><ul>@for (p of unreviewedWorkprogrammes(basis); track p.sourceProcedureId) {
                  <li>{{ p.sourceProcedureId }} — {{ p.title }}: independent review is {{ p.status }}.</li>
                }</ul></div>
              } @else if (applicableWorkprogrammes(basis).length === 0) {
                <p role="alert">Approval blocked: the current adopted programme has no applicable workprogrammes.</p>
              }
            }
          }
          @if (w.workprogramApproval?.hasApproval) {
            <p [attr.role]="w.workprogramApproval.isCurrent ? 'status' : 'alert'">
              Manager approval {{ w.workprogramApproval.isCurrent ? 'is current' : 'is stale and must be renewed' }}.
              @if (w.workprogramApproval.approvedAt; as approvedAt) { Recorded {{ approvedAt.slice(0, 16).replace('T', ' ') }} UTC. }
            </p>
          } @else { <p role="status">No aggregate Manager workprogramme approval is recorded.</p> }
          @if (w.workprogramApproval?.canApprove && !w.workprogramApproval.isCurrent) {
            <label>Manager rationale <textarea name="workprogramRationale" [(ngModel)]="f.workprogramRationale" rows="2" maxlength="2000" required></textarea></label>
            <label><input type="checkbox" name="workprogramReviewed" [(ngModel)]="f.workprogramReviewed" /> I reviewed the exact current applicable workprogramme set and its independent review states.</label>
            <button matButton="filled" (click)="approveWorkprograms()" [disabled]="cmd.busy() || !f.workprogramReviewed || !f.workprogramRationale.trim() || !canApproveCurrentWorkprograms()">
              Record Manager workprogramme approval
            </button>
          } @else if (!w.workprogramApproval?.canApprove && !w.workprogramApproval?.isCurrent) {
            <p role="status">This account is not authorized for the Manager approval action. The server rechecks Manager rank and engagement scope on submission.</p>
          }
        </section>
        <label>Reviewer recommendations <textarea name="rec" [(ngModel)]="f.recommendations" rows="2" maxlength="8000"></textarea></label>
        <button matButton="outlined" (click)="send(base() + '/srm', { recommendations: f.recommendations }, 'Summary Review Memorandum generated.')" [disabled]="cmd.busy()">Generate Summary Review Memorandum</button>
        @if (srm(w); as s) {
          <p>Latest SRM v{{ s.version }} is <strong>{{ s.current ? 'current' : 'stale' }}</strong>.</p>
          @if (s.current && w.clearance?.summaryReviewMemorandumId === s.id) {
            <dl data-partner-clearance>
              <dt>Current Partner clearance</dt><dd><code>{{ w.clearance.id }}</code></dd>
              <dt>Cleared Summary Review Memorandum</dt><dd><code>{{ w.clearance.summaryReviewMemorandumId }}</code></dd>
              <dt>Engagement Partner user</dt><dd><code>{{ w.clearance.partnerUserId }}</code></dd>
              <dt>Cleared at</dt><dd>{{ w.clearance.clearedAt.slice(0, 16).replace('T', ' ') }} UTC</dd>
            </dl>
          } @else {
            <p>No current Partner clearance is established for this SRM.</p>
          }
          <label>Key risk areas reviewed <input name="risks" [(ngModel)]="f.risks" maxlength="2000" /></label>
          <label>Financial-statement notes reviewed <input name="notes" [(ngModel)]="f.notes" maxlength="2000" /></label>
          <button matButton="filled" (click)="send(base() + '/clearance', { srmId: s.id, keyRiskAreasComment: f.risks, notesComment: f.notes }, 'Partner clearance recorded.')" [disabled]="cmd.busy()">Record Partner clearance</button>
        }

        <h3>Audit opinion</h3>
        <div class="inline-form">
          <label>Opinion <select name="op" [(ngModel)]="f.opinion">@for (t of w.opinionTypes; track t) { <option [value]="t">{{ label(t) }}</option> }</select></label>
          @if (f.opinion !== 'UNMODIFIED') {
            <label>Affected FSLI * <select name="focus" [(ngModel)]="f.focus" required><option value="">Select from the approved reporting taxonomy</option>
              @for (a of w.opinionAreas; track a.id) { <option [value]="a.id">{{ a.code }} — {{ a.name }}</option> }</select></label>
            @if (!w.opinionAreas.length) { <p role="alert">Approve the applicable reporting taxonomy before recording a modified opinion.</p> }
          }
        </div>
        @if (f.opinion !== 'UNMODIFIED') {
          <label>{{ f.opinion === 'QUALIFIED' ? 'Basis for Qualified Opinion' : f.opinion === 'ADVERSE' ? 'Basis for Adverse Opinion' : 'Basis for Disclaimer of Opinion' }}
            <textarea name="basis" [(ngModel)]="f.basis" rows="3" maxlength="4000" required></textarea></label>
        }
        <button matButton="filled" (click)="send(base() + '/opinion', { opinionType: f.opinion, focusArea: f.focus || null, basis: f.basis || null }, label(f.opinion) + ' opinion recorded.')" [disabled]="cmd.busy()">Record opinion (Engagement Partner)</button>
        @if (w.opinion; as o) {
          @if (srm(w); as currentSrm) {
            @if (currentSrm.current && w.clearance?.summaryReviewMemorandumId === currentSrm.id && w.clearance.id === o.partnerClearanceId) {
              <dl class="facts" data-current-opinion>
                <dt>Current opinion</dt><dd><strong>{{ o.label }}</strong>{{ o.focusArea ? ' — ' + o.focusArea : '' }}</dd>
                <dt>Opinion decision ID</dt><dd><code>{{ o.id }}</code></dd>
                <dt>Partner clearance ID</dt><dd><code>{{ o.partnerClearanceId }}</code></dd>
                <dt>Deciding Partner user</dt><dd><code>{{ o.decidedByUserId }}</code></dd>
                <dt>Decided at</dt><dd><time [attr.datetime]="o.decidedAt">{{ o.decidedAt }}</time></dd>
              </dl>
            } @else { <p>No current opinion is established for the current Partner clearance.</p> }
          } @else { <p>No current opinion is established without a current Summary Review Memorandum.</p> }
        }

        <h3>Deliverables</h3>
        <p class="actions">@for (r of reports; track r[0]) { <button matButton="outlined" (click)="report(r[0])" [disabled]="cmd.busy()">Generate {{ r[1] }}</button> }</p>
        @if (w.deliverables.length) {
          <div class="table-scroll"><table aria-label="Generated deliverables">
            <thead><tr><th>Document</th><th>Version</th><th>State</th><th>SHA-256</th><th><span class="sr-only">Actions</span></th></tr></thead>
            <tbody>@for (d of w.deliverables; track d.id) {
              <tr [attr.data-completion-deliverable]="d.id"><td><a [href]="'/api/deliverables/' + d.id + '/download'">{{ d.title }}</a><br /><small>Document ID: <code>{{ d.id }}</code></small></td><td>v{{ d.version }}{{ d.signatureKind === 'CERTIFICATE' ? ' certificate-signed' : d.signed ? ' signed (image only)' : '' }}@if (d.certificateSubject) { <small>{{ d.certificateSubject }}</small> }</td><td>{{ d.current ? 'Current' : 'Stale' }}</td><td><code>{{ d.contentSha256 }}</code></td>
                <td class="actions">
                  @if (!d.signed && d.kind !== 'SUMMARY_REVIEW_MEMORANDUM' && d.kind !== 'HOLDING_LETTER') { <button matButton (click)="send('/api/ui/deliverables/' + d.id + '/share', {}, 'Shared with client management.')" [disabled]="cmd.busy()" [attr.aria-label]="'Share ' + d.title + ' v' + d.version + ' with the client'">Share with client</button> }
                  @if (!d.signed && d.kind === 'INDEPENDENT_AUDITORS_REPORT' && d.current) { <button matButton="filled" (click)="send('/api/ui/deliverables/' + d.id + '/sign', {}, 'Report signed; the signed version and its hash are recorded.')" [disabled]="cmd.busy() || !w.reportSigning.ready">{{ w.reportSigning.mode === 'CERTIFICATE' ? 'Sign with signature and firm certificate' : 'Sign with registered signature' }}</button> }
                  @if (d.signed && d.kind === 'INDEPENDENT_AUDITORS_REPORT') { <button matButton (click)="verifySignature(d.id)" [disabled]="verifying()" [attr.aria-label]="'Check the signature on ' + d.title + ' v' + d.version">Check signature</button> }
                </td></tr> }</tbody>
          </table></div>
        }
        @for (s of w.shared; track s.title) { @for (c of s.openComments; track c.id) {
          <div class="inline-form"><span>Client comment on {{ s.title }}: “{{ c.body }}”</span>
            <label>Resolution <input [(ngModel)]="f.resolution" [name]="'res-' + c.id" [attr.aria-label]="'Resolution for comment on ' + s.title" /></label>
            <button matButton (click)="send('/api/ui/deliverable-comments/' + c.id + '/resolve', { resolution: f.resolution }, 'Comment resolved.')" [disabled]="cmd.busy()">Resolve</button></div> } }

        <h3>Management-signed representation letter</h3>
        <p>Client management uploads the signed PDF for the exact shared version in the portal. The Engagement Partner verifies the signature, authority and completeness before signing the report.</p>
        @for (scan of w.signedLetters; track scan.id) {
          <div [attr.data-signed-letter]="scan.id">
            <a [href]="'/api/representation-scans/' + scan.id + '/download'">Management-signed LOR v{{ scan.version }} — {{ scan.managementSignatory }}</a>
            <audit-status [value]="scan.current ? (scan.verified ? 'VERIFIED' : 'PENDING_REVIEW') : 'STALE'" /> <p><small>Scan ID: <code>{{ scan.id }}</code> · source document: <code>{{ scan.deliverableId }}</code></small><br />SHA-256: <code>{{ scan.contentSha256 }}</code></p>
            @if (scan.current && !scan.verified) {
              <label>Signature verification evidence <textarea [(ngModel)]="f.scanReason" [name]="'sr-' + scan.id" maxlength="2000" aria-label="Signature verification evidence"></textarea></label>
              <label><span><input type="checkbox" [(ngModel)]="f.scanReviewed" [name]="'sv-' + scan.id" /> I reviewed this exact scan and verified management authority, signature and completeness.</span></label>
              <button matButton="outlined" (click)="send('/api/ui/representation-scans/' + scan.id + '/verify', { expectedSha256: scan.contentSha256, reason: f.scanReason, reviewed: f.scanReviewed }, 'Signed representation verified for its exact version.')" [disabled]="cmd.busy() || !f.scanReviewed">Verify signed representation (Partner)</button>
            }
          </div>
        } @empty { <p>No signed representation scan has been uploaded.</p> }

        <h3>Five-part final bundle</h3>
        <p>Signed report and certified financial statements; management letter; representation template and management-signed scan; correspondence audit trail; posted final balance fee note.</p>
        @if (w.bundles; as b) {
          <ul class="blockers">@for (x of b.blockers; track $index) { <li>{{ x }}</li> }</ul>
          @for (x of b.bundles; track x.id) { <p [attr.data-completion-bundle]="x.id"><a [href]="'/api/deliverable-bundles/' + x.id + '/download'">Download five-part final bundle</a><br />
            Bundle ID: <code>{{ x.id }}</code><br />SHA-256: <code>{{ x.sha256 }}</code><br />Assembled {{ x.assembledAt.slice(0, 16).replace('T', ' ') }} UTC</p> }
        }
        @if (signatureCheck(); as check) {
          <p [attr.role]="check.state === 'VALID' ? 'status' : 'alert'" data-signature-check><strong>{{ check.state === 'VALID' ? 'Signature valid' : check.state === 'ALTERED' ? 'Document changed after signing' : check.state === 'NOT_CERTIFICATE_SIGNED' ? 'Image-only signature' : 'Signature unreadable' }}.</strong>
            {{ check.message }}@if (check.subject) { Signed by {{ check.subject }}. }</p>
        }
        <p [attr.role]="w.reportSigning.ready ? null : 'alert'" data-report-signing><strong>Report signing: {{ w.reportSigning.mode === 'CERTIFICATE' ? 'firm certificate' : 'signature image only' }}{{ w.reportSigning.required ? ' (required)' : '' }}.</strong>
          {{ w.reportSigning.message }}@if (w.reportSigning.subject && w.reportSigning.notAfter) { Certificate: {{ w.reportSigning.subject }}, valid until {{ w.reportSigning.notAfter.slice(0, 10) }}. }</p>
        <label><span><input type="checkbox" name="stmts" [(ngModel)]="f.statementsReviewed" /> I reviewed the exact released financial statements for certification with this report.</span></label>
        <button matButton="filled" (click)="send(base() + '/bundle', { statementsReviewed: f.statementsReviewed }, 'Five-part final bundle assembled and available to the assigned client.')" [disabled]="cmd.busy() || !f.statementsReviewed || !w.bundles || w.bundles.blockers.length > 0">Assemble five-part bundle (Partner)</button>
        <h4>Partner signature specimen</h4>
        <p><small>A PNG image placed into the signed report as its visible signature. On its own it is not a cryptographic signature; when the firm certificate is configured the report is also certificate-signed.</small></p>
        <label>Signature PNG <input type="file" accept=".png" (change)="upload($event, '/api/ui/signatures', 'Signature specimen registered.')" /></label>
        <h4>Official firm seal</h4>
        <p><small>A firm-wide Partner or Administrator registers the approved seal. Each report retains the exact seal version used.</small></p>
        <label>Seal PNG <input type="file" accept=".png" (change)="upload($event, '/api/ui/firm-seal', 'Approved firm seal registered.')" /></label>
      </section>

      <section class="panel" aria-labelledby="records-heading">
        <h2 id="records-heading">File freeze and activity trail</h2>
        @if (w.freeze; as fz) {
          <p><audit-status [value]="fz.state" /> Report signed {{ fz.reportSignedAt.slice(0, 10) }}; freeze due {{ fz.dueAt.slice(0, 10) }}{{ fz.state === 'SCHEDULED' ? ' (' + fz.daysRemaining + ' day(s) remaining)' : '' }}.@if (fz.frozenAt) { Frozen at {{ fz.frozenAt.slice(0, 16).replace('T', ' ') }} UTC. }</p>
          <p><small>SharePoint read-only: <strong>{{ fz.externalReadOnly }}</strong>. {{ fz.externalReadOnly === 'BLOCKED_EXTERNAL' ? 'AuditSphere refuses changes to the frozen file; the SharePoint folder is not confirmed read-only until an authorized tenant change is observed.' : '' }}</small></p>
          @if (fz.state === 'FROZEN') {
            <p><small>The original archive is terminal and remains read-only. Supplementary record requests and approvals are retained separately; they do not authorize edits to archived content.</small></p>
            <label>Reason for supplementary record <input name="amend" [(ngModel)]="f.amendment" maxlength="2000" /></label>
            <button matButton="outlined" (click)="send('/api/ui/engagements/' + w.engagementId + '/amendments', { reason: f.amendment }, 'Supplementary record request saved; the original archive remains read-only.')" [disabled]="cmd.busy()">Request supplementary record</button>
          }
          @for (a of fz.amendments; track a.id) {
            <div class="inline-form"><span>{{ a.reason }} — {{ a.closedAt ? 'closed supplement request' : a.openedAt ? 'approved supplement request' : 'awaiting Partner approval' }}</span>
              @if (!a.openedAt) { <button matButton (click)="send('/api/ui/amendments/' + a.id + '/approve', {}, 'Supplementary record request approved; the original archive remains read-only.')" [disabled]="cmd.busy()">Approve supplement request</button> }
              @else if (!a.closedAt) { <button matButton (click)="send('/api/ui/amendments/' + a.id + '/close', {}, 'Supplementary record request closed; the original archive remains read-only.')" [disabled]="cmd.busy()">Close supplement request</button> }</div>
          }
        } @else { <p>The freeze is scheduled when the Engagement Partner signs the Independent Auditor's Report; the file then freezes {{ w.freezeDays }} days later.</p> }
        <h3>Document locks</h3>
        <div class="inline-form">
          <label>Document <input name="lock" [(ngModel)]="f.lockKey" maxlength="200" placeholder="e.g. WP-A1" /></label>
          <button matButton="outlined" (click)="send('/api/ui/engagements/' + w.engagementId + '/locks', { documentKey: f.lockKey }, 'Document locked for you.')" [disabled]="cmd.busy() || w.freeze?.state === 'FROZEN'">Lock for editing</button>
        </div>
        @for (l of w.locks; track l.id) {
          <div class="inline-form"><span>{{ l.documentKey }} locked by {{ l.lockedBy }} since {{ l.lockedAt.slice(0, 16).replace('T', ' ') }}</span>
            <button matButton (click)="send('/api/ui/locks/' + l.id + '/release', {}, 'Lock released.')" [disabled]="cmd.busy()" [attr.aria-label]="'Release lock on ' + l.documentKey">Release</button></div>
        }
        <h3>Activity trail</h3>
        <p><small>{{ w.trailCoverageNote }}</small></p>
        <label>Show <select name="kind" [ngModel]="kind()" (ngModelChange)="kind.set($event)"><option value="">All activity</option>
          @for (k of kinds; track k) { <option [value]="k">{{ k }}</option> }</select></label>
        @if (w.trail; as t) {
          <div class="table-scroll"><table aria-label="Activity trail">
            <thead><tr><th>When</th><th>Kind</th><th>Who</th><th>What</th></tr></thead>
            <tbody>@for (e of filtered(t); track $index) { <tr><td>{{ e.at.slice(0, 19).replace('T', ' ') }}</td><td>{{ e.kind }}</td><td>{{ e.actor }}</td><td>{{ e.description }}</td></tr> }</tbody>
          </table></div>
        } @else { <p>The activity trail is available to Managers, Partners and reviewers.</p> }
      </section>

      <p role="status">{{ w.gates.length }} gate rows · {{ obtained(w) }} / {{ w.representations.length }} representations obtained · release candidate {{ w.releaseCandidateId ? 'recorded' : 'not recorded' }}</p>
      <section class="panel" aria-labelledby="gates-heading">
        <h2 id="gates-heading">Completion gates</h2>
        <p>Gates reflect recorded evidence; applicability and issuance are determined by the authoritative commands. A paid invoice cannot clear any professional gate.</p>
        <div class="table-scroll"><table>
          <thead><tr><th scope="col">Gate</th><th scope="col">Status</th><th scope="col">Authority</th><th scope="col">Date</th></tr></thead>
          <tbody>@for (g of w.gates; track g.name) { <tr><td>{{ g.name }}</td><td><audit-status [value]="g.status" /></td><td>{{ g.authority ?? '—' }}</td><td>{{ g.date ? g.date.slice(0, 16).replace('T', ' ') : '—' }}</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="rep-heading">
        <h2 id="rep-heading">Written representations</h2>
        <p>Management representation records are shown separately from their received status and any signed-letter evidence.</p>
        @for (r of w.representations; track r.id) {
          <article [attr.data-management-representation]="r.id">
            <p><strong>{{ r.code }}</strong> {{ r.title }}: {{ r.narrative }} <audit-status [value]="r.obtained ? 'OBTAINED' : 'PENDING'" /></p>
            <dl class="facts"><dt>Representation record ID</dt><dd><code>{{ r.id }}</code></dd>
              <dt>Signatory</dt><dd>{{ r.signatoryName ?? 'Not recorded' }}</dd>
              <dt>Obtained at</dt><dd>@if (r.obtainedAt; as obtainedAt) { <time [attr.datetime]="obtainedAt">{{ obtainedAt }}</time> } @else { Not recorded }</dd></dl>
          </article>
        }
        @empty { <p>No written representations recorded for this engagement.</p> }
      </section>
      <section class="panel" aria-labelledby="fs-heading">
        <h2 id="fs-heading">Final financial statements</h2>
        <dl class="facts"><dt>Package ID</dt><dd>{{ w.packageId ?? 'None' }}</dd><dt>Package status</dt><dd>{{ w.packageStatus }}</dd>
          <dt>Partner approval</dt><dd>{{ w.partnerApproved ? 'Current approval' : 'Current approval not established' }}</dd>
          <dt>Recorded EQR case status</dt><dd>{{ w.eqrStatus }}</dd></dl>
      </section>
      @if (w.eqrStatus === 'NOT_ASSIGNED') {
        <section class="panel" aria-labelledby="eqr-unassigned-heading">
          <h2 id="eqr-unassigned-heading">Independent EQR review</h2>
          <p>No EQR case is assigned. Applicability is not established here, and this page does not create or assign a case.</p>
        </section>
      } @else {
        <audit-eqr-decision [engagementId]="w.engagementId" [caseStatus]="w.eqrStatus" (decisionRecorded)="refreshCompletion($event)" />
      }
      @if (w.freeze; as fz) {
        @if (fz.state === 'SCHEDULED' && readiness.error() !== partnerOnlyMessage) {
          <section class="panel" aria-labelledby="early-lock-heading">
            <h2 id="early-lock-heading">Early compliance lock (STE 4.4.3)</h2>
            <p>The file locks automatically on {{ fz.dueAt.slice(0, 10) }}, with {{ fz.daysRemaining }} day(s) remaining. A Partner may lock it earlier once the final release and archive readiness are reviewed.</p>
            <p>Provider protection status: <strong>{{ fz.externalReadOnly }}</strong>. The AuditSphere lock does not by itself make the provider copy read-only.</p>
            @if (readiness.data(); as r) {
              <p>Archive readiness at countdown revision {{ r.revision }}: <audit-status [value]="r.state" /></p>
              @if (r.blockers.length > 0) {
                <ul>@for (b of r.blockers; track b) { <li>{{ b }}</li> }</ul>
                <p>Resolve these blockers before the file can be locked early.</p>
              } @else if (r.archiveReadinessDigest) {
                <p><small>Reviewed readiness digest: <code>{{ r.archiveReadinessDigest }}</code></small></p>
                <p><strong>Warning:</strong> an early lock takes effect immediately. The archive becomes permanently read-only in AuditSphere. A supplementary-record request and its approval do not reopen the archive.</p>
                <label><input type="checkbox" name="lockConfirm" [(ngModel)]="early.confirmed" /> I confirm the file is complete and I am locking it now as the Engagement Partner.</label>
                <label>Reason for the early lock <textarea name="lockReason" [(ngModel)]="early.rationale" maxlength="2000" rows="2"></textarea></label>
                <button matButton="filled" (click)="lockEarly(r)" [disabled]="cmd.busy() || !early.confirmed || !early.rationale.trim()">Lock audit file early</button>
              }
              <button matButton="outlined" (click)="readiness.reload()" [disabled]="cmd.busy()">Refresh archive readiness</button>
            } @else if (readiness.error()) {
              <p role="alert">{{ readiness.error() }}</p>
            } @else {
              <p>Loading archive readiness…</p>
            }
          </section>
        }
      }

      <section class="panel" aria-labelledby="actions-heading">
        <h2 id="actions-heading">Actions</h2>
        <p class="actions">
          @if (w.releaseCandidateId) { <a matButton="filled" [routerLink]="['/app/releases', w.releaseCandidateId]">View release candidate</a> }
          @else if (w.canPrepareRelease && w.packageId) { <button matButton="filled" (click)="send(base() + '/release-candidate', { packageId: w.packageId }, 'Release candidate prepared from the exact reviewed package.')" [disabled]="cmd.busy()">{{ cmd.busy() ? 'Preparing…' : 'Prepare release candidate' }}</button> }
          @if (w.packageId) { <a matButton="outlined" [routerLink]="['/app/accounting/packages', w.packageId]">Open financial package</a> }
          <a matButton [routerLink]="['/app/engagements', w.engagementId]">Back to engagement</a>
        </p>
      </section>
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class EngagementCompletion {
  private readonly api = inject(Api);
  readonly id = routeGuid();
  readonly ws = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/completion` : null), decodeCompletion,
    'Sign in with an authorized internal staff identity assigned to this engagement to view its completion checklist.');
  readonly programBasis = this.api.resource(() => (this.id() ? `/api/ui/engagements/${this.id()}/fieldwork` : null), fieldworkBasis,
    'The applicable workprogramme review set is available only within the authorized engagement scope.');
  readonly cmd = new CommandState(this.api);
  readonly kind = signal('');
  readonly kinds = ['UPLOAD', 'EDIT', 'COMMENT', 'SIGN_OFF', 'FREEZE', 'DENIED'];
  /** Shown for a 403/404 readiness response: the early-lock control is then hidden for this user. */
  readonly partnerOnlyMessage = 'Only the Engagement Partner in scope can review archive readiness for an early lock.';
  readonly readiness = this.api.resource(() => (this.id() && this.ws.data()?.freeze?.state === 'SCHEDULED'
    ? `/api/ui/engagements/${this.id()}/early-lock/readiness` : null), archiveReadiness, this.partnerOnlyMessage);
  early = { confirmed: false, rationale: '' };
  readonly reports = REPORTS;
  f = { recommendations: '', risks: '', notes: '', opinion: 'UNMODIFIED', focus: '', basis: '', resolution: '', scanReason: '', scanReviewed: false,
    statementsReviewed: false, amendment: '', lockKey: '', workprogramRationale: '', workprogramReviewed: false };

  private readonly eqrReceipt = signal<{ context: string; decisionId: string } | null>(null);
  private receiptContext: string | null = null;
  readonly confirmedEqrDecision = computed(() => {
    const receipt = this.eqrReceipt();
    return receipt && receipt.context === this.eqrContext() ? receipt.decisionId : null;
  });

  private eqrContext(): string | null {
    const actor = this.signatureSession.current(), id = this.id();
    return actor?.staff && id ? `${actor.firmId.toLowerCase()}:${actor.userId.toLowerCase()}:${actor.generation}:${this.signatureSession.invalidation()}:${id.toLowerCase()}` : null;
  }

  base(): string { return `/api/ui/engagements/${this.id()}/completion`; }
  refreshCompletion(result: { engagementId: string; decisionId: string }): void {
    const context = this.eqrContext();
    if (!context || this.id()?.toLowerCase() !== result.engagementId.toLowerCase() ||
        this.ws.data()?.engagementId.toLowerCase() !== result.engagementId.toLowerCase()) return;
    this.receiptContext = context;
    this.eqrReceipt.set({ context, decisionId: result.decisionId });
    this.ws.reload();
  }
  label(t: string): string { return OPINION_LABELS[t] ?? t; }
  srm(w: Completion) { return w.deliverables.filter((d) => d.kind === 'SUMMARY_REVIEW_MEMORANDUM').sort((a, b) => b.version - a.version)[0] ?? null; }
  obtained(w: Completion): number { return w.representations.filter((r) => r.obtained).length; }
  filtered(t: NonNullable<Completion['trail']>) { const k = this.kind(); return k ? t.filter((e) => e.kind === k) : t; }
  applicableWorkprogrammes(basis: ReturnType<typeof fieldworkBasis>) { return basis.procedures.filter((p) => p.applicabilityStatus === 'APPLICABLE'); }
  reviewedWorkprogrammes(basis: ReturnType<typeof fieldworkBasis>) { return this.applicableWorkprogrammes(basis).filter((p) => p.status === 'REVIEWED').length; }
  unreviewedWorkprogrammes(basis: ReturnType<typeof fieldworkBasis>) { return this.applicableWorkprogrammes(basis).filter((p) => p.status !== 'REVIEWED'); }
  canApproveCurrentWorkprograms(): boolean {
    const state = this.programBasis.data();
    return !!state && state.engagementId === this.id() && !!state.program && state.canManageFieldwork &&
      this.applicableWorkprogrammes(state).length > 0 && this.unreviewedWorkprogrammes(state).length === 0;
  }
  async approveWorkprograms(): Promise<void> {
    const approval = this.ws.data()?.workprogramApproval;
    if (!approval?.canApprove || approval.isCurrent || !this.f.workprogramReviewed || !this.f.workprogramRationale.trim() || !this.canApproveCurrentWorkprograms()) return;
    await this.cmd.run(this.base() + '/workprogram-approval', { rationale: this.f.workprogramRationale.trim() },
      'Manager workprogramme approval submitted. Refreshing the current approval and SRM state from the server.');
    this.ws.reload();
    this.programBasis.reload();
  }
  send(url: string, body: unknown, ok: string): void { void this.cmd.run(url, body, ok).finally(() => this.ws.reload()); }
  readonly signatureCheck = signal<ReturnType<typeof decodeSignatureCheck> | null>(null);
  readonly verifying = signal(false);
  private readonly signatureSession = inject(SessionService);
  private readonly varianceSession = inject(SessionService);
  readonly varianceForm = { accountArea: '', periodReference: '', procedureId: '', expectedAmount: '', actualAmount: '',
    investigationThreshold: '', currency: '', explanation: '', evidenceReferences: '' };
  readonly varianceState = signal<VarianceState | null>(null);
  readonly variancePreview = signal<VariancePreview | null>(null);
  readonly variancePending = signal<VariancePending | null>(null);
  readonly varianceRecoveryUnavailable = signal(false);
  readonly varianceError = signal('');
  readonly varianceMessage = signal('');
  readonly varianceLoading = signal(false);
  readonly varianceBusy = signal(false);
  varianceAssent = false;
  private varianceActorIdentity = '';
  private varianceOperation = 0;
  private varianceLoadedState: VarianceState | null = null;
  private varianceIntent: VarianceIntent | null = null;
  readonly varianceBasisCurrent = signal(true);
  private readonly signatureLifetime = inject(DestroyRef);
  private signatureRequest = 0;
  private signatureDestroyed = false;
  constructor() {
    effect(() => {
      const id = this.id()?.toLowerCase() ?? '';
      const actor = this.varianceSession.current();
      const invalidation = this.varianceSession.invalidation();
      untracked(() => this.resetVarianceContext(id, actor, invalidation));
    });
    effect(() => {
      const context = this.eqrContext();
      untracked(() => {
        if (context !== this.receiptContext) {
          this.receiptContext = context;
          this.eqrReceipt.set(null);
        }
      });
    });
    this.signatureLifetime.onDestroy(() => { this.signatureDestroyed = true; ++this.signatureRequest; this.signatureCheck.set(null); });
    effect(() => {
      this.id(); this.ws.data(); this.signatureSession.current(); this.signatureSession.invalidation();
      untracked(() => { ++this.signatureRequest; this.signatureCheck.set(null); this.verifying.set(false); });
    });
  }
  private varianceStorageKey(owner: { firmId: string; userId: string; engagementId: string }): string {
    return `auditsphere-variance-pending-v1:${owner.firmId.toLowerCase()}:${owner.userId.toLowerCase()}:${owner.engagementId.toLowerCase()}`;
  }
  private varianceOwner() {
    const actor = this.varianceSession.current(), engagementId = this.id()?.toLowerCase() ?? '';
    return actor?.staff && guidPattern.test(engagementId) ? { firmId: actor.firmId.toLowerCase(), userId: actor.userId.toLowerCase(),
      generation: actor.generation, epoch: this.varianceSession.invalidation(), engagementId } : null;
  }
  private varianceContext(owner = this.varianceOwner()): string {
    return owner ? `${owner.firmId}:${owner.userId}:${owner.generation}:${owner.epoch}:${owner.engagementId}` : '';
  }
  private varianceIsCurrent(operation: number, context: string): boolean {
    return operation === this.varianceOperation && context !== '' && context === this.varianceActorIdentity &&
      context === this.varianceContext() && !this.signatureDestroyed;
  }
  private clearVarianceDisplay(message = ''): void {
    ++this.varianceOperation;
    this.varianceState.set(null); this.variancePreview.set(null); this.varianceLoadedState = null; this.varianceIntent = null;
    this.varianceAssent = false; this.varianceLoading.set(false); this.varianceBusy.set(false); this.varianceError.set(''); this.varianceMessage.set(message);
  }
  private resetVarianceContext(engagementId: string, actor: ReturnType<SessionService['current']>, epoch: number): void {
    const next = actor?.staff && guidPattern.test(engagementId)
      ? `${actor.firmId.toLowerCase()}:${actor.userId.toLowerCase()}:${actor.generation}:${epoch}:${engagementId}` : '';
    if (next === this.varianceActorIdentity) return;
    this.varianceActorIdentity = next;
    this.clearVarianceDisplay(next ? '' : 'Sign in with an authorized internal staff identity to load an exact variance basis.');
    Object.assign(this.varianceForm, { accountArea: '', periodReference: '', procedureId: '', expectedAmount: '', actualAmount: '',
      investigationThreshold: '', currency: '', explanation: '', evidenceReferences: '' });
    this.varianceBasisCurrent.set(true);
    this.variancePending.set(null); this.varianceRecoveryUnavailable.set(false);
    const owner = actor?.staff && guidPattern.test(engagementId)
      ? { firmId: actor.firmId.toLowerCase(), userId: actor.userId.toLowerCase(), engagementId } : null;
    if (!owner) return;
    try {
      const raw = sessionStorage.getItem(this.varianceStorageKey(owner));
      if (raw === null) return;
      const pending: unknown = JSON.parse(raw);
      if (!pending || typeof pending !== 'object') throw new Error('invalid');
      const p = pending as Partial<VariancePending>;
      if (p.version !== 2 || p.firmId !== owner.firmId || p.actorId !== owner.userId || p.engagementId !== owner.engagementId ||
          !['PREPARE', 'REVIEW'].includes(p.action ?? '') || !guidPattern.test(p.requestId ?? '') || !/^[0-9a-f]{64}$/.test(p.requestHash ?? '') ||
          !isCanonicalLong(p.actorEpoch ?? '') || !isCanonicalLong(p.expectedRevision ?? '') ||
          !/^[0-9a-f]{64}$/.test(p.reviewBasis ?? '') || typeof p.accountArea !== 'string' || p.accountArea.length > 80 ||
          typeof p.periodReference !== 'string' || p.periodReference.length > 60 ||
          (p.varianceId !== null && p.varianceId !== undefined && !guidPattern.test(p.varianceId)) ||
          (p.basisContentHash !== null && !/^[0-9a-f]{64}$/.test(p.basisContentHash ?? '')) ||
          !/^[0-9a-f]{64}$/.test(p.resultContentHash ?? '')) throw new Error('invalid');
      this.variancePending.set(p as VariancePending);
    } catch {
      this.varianceRecoveryUnavailable.set(true);
      this.varianceError.set('The saved variance request reference cannot be verified. No new request will be sent.');
    }
  }
  varianceSelectionChanged(): void {
    this.clearVarianceDisplay();
    Object.assign(this.varianceForm, { procedureId: '', expectedAmount: '', actualAmount: '', investigationThreshold: '',
      currency: '', explanation: '', evidenceReferences: '' });
    this.varianceBasisCurrent.set(true);
  }
  loadPendingVarianceScope(): Promise<boolean> {
    const pending = this.variancePending();
    if (!pending || this.varianceBusy()) return Promise.resolve(false);
    this.varianceForm.accountArea = pending.accountArea; this.varianceForm.periodReference = pending.periodReference;
    Object.assign(this.varianceForm, { procedureId: '', expectedAmount: '', actualAmount: '', investigationThreshold: '',
      currency: '', explanation: '', evidenceReferences: '' });
    this.clearVarianceDisplay(); this.varianceBasisCurrent.set(true);
    return this.loadVarianceState();
  }
  clearVarianceAssent(): void {
    const invalidatingPreviewRequest = !this.variancePreview() && this.varianceBusy();
    ++this.varianceOperation;
    this.variancePreview.set(null); this.varianceIntent = null; this.varianceAssent = false;
    if (invalidatingPreviewRequest) this.varianceBusy.set(false);
  }
  private selectedVarianceScope(): { accountArea: string; periodReference: string } | null {
    const accountArea = serverTrim(this.varianceForm.accountArea).toUpperCase();
    const periodReference = serverTrim(this.varianceForm.periodReference).toUpperCase();
    return accountArea.length > 0 && accountArea.length <= 80 && periodReference.length > 0 && periodReference.length <= 60
      ? { accountArea, periodReference } : null;
  }
  async loadVarianceState(): Promise<boolean> {
    const owner = this.varianceOwner(), scope = this.selectedVarianceScope();
    if (!owner || !scope || this.varianceLoading()) return false;
    this.clearVarianceDisplay();
    const operation = this.varianceOperation, context = this.varianceContext(owner);
    this.varianceLoading.set(true);
    try {
      const state = await this.api.get(`/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/state?accountArea=${encodeURIComponent(scope.accountArea)}&periodReference=${encodeURIComponent(scope.periodReference)}`, varianceStateDecoder);
      if (!this.varianceIsCurrent(operation, context)) return false;
      if (state.engagementId.toLowerCase() !== owner.engagementId || state.accountArea !== scope.accountArea || state.periodReference !== scope.periodReference ||
          !isCanonicalLong(state.expectedRevision) || state.expectedRevision !== (state.record?.revision ?? '0') || !/^[0-9a-f]{64}$/.test(state.reviewBasis) ||
          state.record && !this.validVarianceRecord(state.record, state)) throw new Error('The server returned a variance basis for a different scope or unsupported revision.');
      this.varianceState.set(state); this.varianceLoadedState = state;
      this.varianceBasisCurrent.set(true);
      this.varianceForm.accountArea = scope.accountArea; this.varianceForm.periodReference = scope.periodReference;
      this.varianceError.set('');
      return true;
    } catch (error) {
      if (this.varianceIsCurrent(operation, context)) this.varianceError.set(error instanceof Error ? error.message : 'The exact variance basis could not be verified.');
      return false;
    } finally { if (this.varianceIsCurrent(operation, context)) this.varianceLoading.set(false); }
  }
  varianceInputsValid(): boolean {
    const amount = (value: string, nonnegative = false) => /^-?\d{1,13}(?:\.\d{1,6})?$/.test(value.trim()) && (!nonnegative || !value.trim().startsWith('-'));
    const references = this.varianceForm.evidenceReferences.split(/\r?\n/).map(serverTrim).filter(Boolean);
    return !!this.varianceState() && this.varianceState() === this.varianceLoadedState && !this.variancePending() && !this.varianceRecoveryUnavailable() &&
      this.varianceState()!.canPrepare && amount(this.varianceForm.expectedAmount) && amount(this.varianceForm.actualAmount) &&
      amount(this.varianceForm.investigationThreshold, true) && /^[A-Za-z]{3}$/.test(serverTrim(this.varianceForm.currency)) &&
      (!serverTrim(this.varianceForm.procedureId) || guidPattern.test(serverTrim(this.varianceForm.procedureId))) &&
      serverTrim(this.varianceForm.explanation).length <= 4000 && references.length <= 100 && references.every(x => x.length <= 2000) &&
      new Set(references).size === references.length;
  }
  private varianceRequestId(): string { return crypto.randomUUID(); }
  private async previewVariance(action: 'PREPARE' | 'REVIEW'): Promise<void> {
    const owner = this.varianceOwner(), state = this.varianceState(), scope = this.selectedVarianceScope();
    if (!owner || !state || state !== this.varianceLoadedState || !scope || this.variancePending() || this.varianceRecoveryUnavailable() || this.varianceBusy() ||
        state.engagementId.toLowerCase() !== owner.engagementId || state.accountArea !== scope.accountArea || state.periodReference !== scope.periodReference ||
        action === 'PREPARE' && !this.varianceInputsValid() || action === 'REVIEW' && (!state.canReview || !state.record)) return;
    const operation = ++this.varianceOperation, context = this.varianceContext(owner);
    this.varianceBusy.set(true); this.varianceError.set(''); this.varianceMessage.set(''); this.variancePreview.set(null); this.varianceAssent = false;
    const fields = action === 'PREPARE' ? { procedureId: serverTrim(this.varianceForm.procedureId) || null, accountArea: scope.accountArea,
      periodReference: scope.periodReference, expectedAmount: this.varianceForm.expectedAmount.trim(), actualAmount: this.varianceForm.actualAmount.trim(),
      investigationThreshold: this.varianceForm.investigationThreshold.trim(), currency: serverTrim(this.varianceForm.currency).toUpperCase(),
      explanation: serverTrim(this.varianceForm.explanation) || null,
      evidenceReferences: this.varianceForm.evidenceReferences.split(/\r?\n/).map(serverTrim).filter(Boolean) } : null;
    const body = { requestId: this.varianceRequestId(), expectedRevision: state.expectedRevision, reviewBasis: state.reviewBasis, fields };
    try {
      const url = action === 'PREPARE' ? `/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/prepare/preview`
        : `/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/${state.record!.id}/review/preview`;
      const outcome = await this.api.command<VariancePreview>(url, body);
      if (!this.varianceIsCurrent(operation, context)) return;
      if (!outcome.ok) { this.varianceError.set(outcome.message); return; }
      const preview = decode(variancePreviewDecoder, outcome.value);
      if (preview.requestId !== body.requestId || preview.action !== action || !/^[0-9a-f]{64}$/.test(preview.requestHash) ||
          preview.expectedRevision !== state.expectedRevision || preview.reviewBasis !== state.reviewBasis ||
          !this.sameVarianceRecord(preview.currentRecord, state.record) ||
          preview.currentRecord && !this.validVarianceRecord(preview.currentRecord, state)) throw new Error('The preview does not match the exact current variance intent. Refresh before continuing.');
      const basisContent = state.record ? this.varianceRecordContent(state.record) : null;
      const resultContent = action === 'PREPARE' && body.fields ? this.varianceFieldsContent(body.fields, state) : basisContent;
      const [basisContentHash, resultContentHash] = await Promise.all([
        this.varianceContentHash(basisContent), this.varianceContentHash(resultContent),
      ]);
      if (action === 'REVIEW' && !basisContentHash || !resultContentHash) throw new Error('The exact variance content could not be bound to this preview. Refresh before continuing.');
      if (!this.varianceIsCurrent(operation, context)) return;
      this.varianceIntent = { action, body, state, preview, basisContentHash, resultContentHash };
      this.variancePreview.set(preview);
    } catch (error) {
      if (this.varianceIsCurrent(operation, context)) this.varianceError.set(error instanceof Error ? error.message : 'The exact preview could not be verified.');
    } finally { if (this.varianceIsCurrent(operation, context)) this.varianceBusy.set(false); }
  }
  private sameVarianceRecord(a: VarianceRecord | null, b: VarianceRecord | null): boolean {
    return a === null && b === null || !!a && !!b && a.id.toLowerCase() === b.id.toLowerCase() && a.revision === b.revision &&
      a.clientId.toLowerCase() === b.clientId.toLowerCase() && a.engagementId.toLowerCase() === b.engagementId.toLowerCase() &&
      a.accountArea === b.accountArea && a.periodReference === b.periodReference &&
      JSON.stringify(this.varianceRecordContent(a)) === JSON.stringify(this.varianceRecordContent(b));
  }
  private varianceRecordContent(record: VarianceRecord): VarianceContentIdentity | null {
    const expectedAmount = canonicalFixed(record.expectedAmount, 6), actualAmount = canonicalFixed(record.actualAmount, 6);
    const differenceAmount = canonicalFixed(record.differenceAmount, 6), investigationThreshold = canonicalFixed(record.investigationThreshold, 6);
    const differencePercent = record.differencePercent === null ? null : canonicalFixed(record.differencePercent, 4);
    if (!expectedAmount || !actualAmount || !differenceAmount || !investigationThreshold ||
        record.differencePercent !== null && !differencePercent) return null;
    return { clientId: record.clientId.toLowerCase(), engagementId: record.engagementId.toLowerCase(),
      procedureId: record.procedureId?.toLowerCase() ?? null, accountArea: record.accountArea, periodReference: record.periodReference,
      expectedAmount, actualAmount, differenceAmount, differencePercent, investigationThreshold,
      exceedsThreshold: record.exceedsThreshold, explanation: record.explanation, conclusion: record.conclusion,
      currency: record.currency, evidenceReferences: [...record.evidenceReferences] };
  }
  private varianceFieldsContent(fields: VarianceFields, state: VarianceState): VarianceContentIdentity | null {
    const expectedAmount = canonicalFixed(fields.expectedAmount, 6), actualAmount = canonicalFixed(fields.actualAmount, 6);
    const investigationThreshold = canonicalFixed(fields.investigationThreshold, 6);
    if (!expectedAmount || !actualAmount || !investigationThreshold) return null;
    const expected = scaledInteger(expectedAmount, 6), actual = scaledInteger(actualAmount, 6);
    const difference = actual - expected, threshold = scaledInteger(investigationThreshold, 6);
    const differencePercent = expected === 0n ? null : fixedFromScaled(
      roundedRatioToEven(difference * 100n * 10_000n, expected < 0n ? -expected : expected), 4);
    const exceedsThreshold = (difference < 0n ? -difference : difference) > threshold;
    const explanation = fields.explanation ? serverTrim(fields.explanation) || null : null;
    return { clientId: state.clientId.toLowerCase(), engagementId: state.engagementId.toLowerCase(),
      procedureId: fields.procedureId?.toLowerCase() ?? null, accountArea: serverTrim(fields.accountArea).toUpperCase(),
      periodReference: serverTrim(fields.periodReference).toUpperCase(), expectedAmount, actualAmount,
      differenceAmount: fixedFromScaled(difference, 6), differencePercent, investigationThreshold, exceedsThreshold,
      explanation, conclusion: !exceedsThreshold ? 'EXPLAINED' : explanation ? 'CORROBORATED' : 'UNEXPLAINED',
      currency: serverTrim(fields.currency).toUpperCase(), evidenceReferences: fields.evidenceReferences.map(serverTrim) };
  }
  private async varianceContentHash(content: VarianceContentIdentity | null): Promise<string | null> {
    if (!content) return null;
    const stable = JSON.stringify([content.clientId, content.engagementId, content.procedureId, content.accountArea,
      content.periodReference, content.expectedAmount, content.actualAmount, content.differenceAmount, content.differencePercent,
      content.investigationThreshold, content.exceedsThreshold, content.explanation, content.conclusion,
      content.currency, content.evidenceReferences]);
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(stable));
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
  }
  private async varianceRecordContentHash(record: VarianceRecord | null): Promise<string | null> {
    return record ? this.varianceContentHash(this.varianceRecordContent(record)) : null;
  }
  private validVarianceRecord(record: VarianceRecord, state: VarianceState): boolean {
    const exactMoney = (value: string) => /^-?\d{1,13}(?:\.\d{1,6})?$/.test(value);
    return record.clientId.toLowerCase() === state.clientId.toLowerCase() && record.engagementId.toLowerCase() === state.engagementId.toLowerCase() &&
      record.accountArea === state.accountArea && record.periodReference === state.periodReference &&
      isCanonicalLong(record.revision) && exactMoney(record.expectedAmount) && exactMoney(record.actualAmount) &&
      exactMoney(record.differenceAmount) && exactMoney(record.investigationThreshold) &&
      (record.differencePercent === null || /^-?\d{1,13}(?:\.\d{1,4})?$/.test(record.differencePercent)) && /^[A-Z]{3}$/.test(record.currency) &&
      (record.reviewedRevision === null || isCanonicalLong(record.reviewedRevision));
  }
  isVarianceReviewCurrent(record: VarianceRecord): boolean {
    return !!record.reviewedByUserId && !!record.reviewedAt && !!record.reviewedRevision &&
      isCanonicalLong(record.reviewedRevision) && isCanonicalLong(record.revision) &&
      BigInt(record.revision) === BigInt(record.reviewedRevision) + 1n;
  }
  varianceIntentFields(): VarianceFields | null { return this.varianceIntent?.body.fields ?? null; }
  previewVariancePreparation(): Promise<void> { return this.previewVariance('PREPARE'); }
  previewVarianceReview(): Promise<void> { return this.previewVariance('REVIEW'); }
  cancelVariancePreview(): void { this.clearVarianceAssent(); }
  async submitVarianceCommand(): Promise<void> {
    const owner = this.varianceOwner(), intent = this.varianceIntent, preview = this.variancePreview(), state = this.varianceState();
    const scope = this.selectedVarianceScope();
    if (!owner || !intent || !preview || state !== intent.state || !scope || !this.varianceAssent || !preview.canProceed || this.varianceBusy() ||
        preview !== intent.preview || !this.sameVarianceRecord(state.record, intent.state.record) || state.reviewBasis !== preview.reviewBasis ||
        state.expectedRevision !== preview.expectedRevision || scope.accountArea !== state.accountArea || scope.periodReference !== state.periodReference ||
        intent.action === 'PREPARE' && !this.varianceInputsValid()) return;
    const context = this.varianceContext(owner), operation = ++this.varianceOperation;
    this.varianceBusy.set(true); this.varianceError.set('');
    const pending: VariancePending = { version: 2, firmId: owner.firmId, actorId: owner.userId, engagementId: owner.engagementId,
      accountArea: state.accountArea, periodReference: state.periodReference, requestId: preview.requestId, requestHash: preview.requestHash,
      action: intent.action, varianceId: state.record?.id ?? null, actorEpoch: owner.generation,
      expectedRevision: preview.expectedRevision, reviewBasis: preview.reviewBasis, basisContentHash: intent.basisContentHash,
      resultContentHash: intent.resultContentHash };
    const key = this.varianceStorageKey(owner);
    let raw: string;
    try {
      if (sessionStorage.getItem(key) !== null) throw new Error('An unresolved variance request already exists. Look it up before starting another request.');
      raw = JSON.stringify(pending); sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('The recovery reference could not be verified.');
    } catch (error) {
      this.varianceRecoveryUnavailable.set(true);
      this.varianceError.set(error instanceof Error ? error.message : 'The recovery reference could not be stored. No command was sent.');
      return;
    }
    this.variancePending.set(pending); this.varianceError.set('');
    this.varianceBasisCurrent.set(false);
    const url = intent.action === 'PREPARE' ? `/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/prepare`
      : `/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/${state.record!.id}/review`;
    try {
      const body = { ...intent.body, expectedRequestHash: preview.requestHash, reviewed: true };
      const outcome = await this.api.command<VarianceReceipt>(url, body);
      if (!this.varianceIsCurrent(operation, context)) return;
      if (!outcome.ok) {
        if (!outcome.unknown && this.definitiveVarianceRefusal(outcome) && this.clearVarianceRecovery(owner, raw)) {
          const refusalMessage = outcome.message;
          this.varianceBusy.set(false);
          const refreshed = await this.loadVarianceState();
          if (context === this.varianceContext()) {
            this.varianceMessage.set(refreshed
              ? 'The server definitively refused the first request before recording a variance change. Review the refreshed basis before forming a new intent.'
              : 'The server definitively refused the first request before recording a variance change. The current basis is unavailable; load it before forming a new intent.');
            this.varianceError.set(refusalMessage);
          }
          return;
        }
        this.varianceError.set(outcome.unknown ? 'The command outcome is unconfirmed. Look up this exact request; do not create a replacement.' : outcome.message);
        return;
      }
      const receipt = decode(varianceReceiptDecoder, outcome.value);
      const receiptMatches = await this.validVarianceReceipt(receipt, pending, state, owner);
      if (!this.varianceIsCurrent(operation, context)) return;
      if (!receiptMatches) throw new Error('The response cannot confirm this exact variance command. Its recovery reference is retained.');
      this.variancePreview.set(null); this.varianceIntent = null; this.varianceAssent = false;
      if (!this.clearVarianceRecovery(owner, raw)) return;
      this.varianceBusy.set(false);
      const refreshed = await this.loadVarianceState();
      if (context === this.varianceContext()) {
        const selected = this.selectedVarianceScope();
        this.varianceMessage.set(refreshed && selected?.accountArea === pending.accountArea && selected.periodReference === pending.periodReference
          ? `The server confirmed ${intent.action.toLowerCase()} request ${receipt.requestId}; the exact variance state was refreshed.`
          : `The server confirmed ${intent.action.toLowerCase()} request ${receipt.requestId}. Current state was not refreshed for the selected scope; reload it before relying on the display.`);
        if (refreshed && selected?.accountArea === pending.accountArea && selected.periodReference === pending.periodReference &&
            this.ws.data()?.engagementId.toLowerCase() === owner.engagementId && !this.ws.error()) this.ws.reload();
      }
    } catch (error) {
      if (this.varianceIsCurrent(operation, context)) this.varianceError.set(error instanceof Error ? error.message : 'The command outcome is unconfirmed. Look up the retained request.');
    } finally { if (this.varianceIsCurrent(operation, context)) this.varianceBusy.set(false); }
  }
  private async validVarianceReceipt(receipt: VarianceReceipt, pending: VariancePending, state: VarianceState,
    owner: VarianceOwner): Promise<boolean> {
    const identityMatches = receipt.requestId === pending.requestId && receipt.requestHash === pending.requestHash && receipt.action === pending.action && receipt.preview.canProceed &&
      pending.actorEpoch === owner.generation && receipt.actorId.toLowerCase() === owner.userId && receipt.actorEpoch === pending.actorEpoch && receipt.engagementId.toLowerCase() === owner.engagementId &&
      receipt.preview.requestId === pending.requestId && receipt.preview.requestHash === pending.requestHash && receipt.preview.action === pending.action &&
      receipt.preview.reviewBasis === pending.reviewBasis && receipt.preview.expectedRevision === pending.expectedRevision &&
      state.engagementId.toLowerCase() === pending.engagementId && state.accountArea === pending.accountArea && state.periodReference === pending.periodReference &&
      (receipt.preview.currentRecord?.id.toLowerCase() ?? null) === (pending.varianceId?.toLowerCase() ?? null) &&
      (receipt.preview.currentRecord?.revision ?? null) === (pending.varianceId ? pending.expectedRevision : null) &&
      receipt.varianceInvestigationId.toLowerCase() === receipt.result.id.toLowerCase() && receipt.result.engagementId.toLowerCase() === owner.engagementId &&
      receipt.result.accountArea === state.accountArea && receipt.result.periodReference === state.periodReference &&
      receipt.result.clientId.toLowerCase() === state.clientId.toLowerCase() && this.validVarianceRecord(receipt.result, state) &&
      (!pending.varianceId || receipt.result.id.toLowerCase() === pending.varianceId.toLowerCase()) &&
      (pending.action === 'PREPARE' ? receipt.result.recordedByUserId.toLowerCase() === owner.userId && receipt.result.reviewedByUserId === null
        : receipt.varianceInvestigationId.toLowerCase() === pending.varianceId?.toLowerCase() && receipt.result.recordedByUserId.toLowerCase() !== owner.userId &&
          receipt.result.conclusion !== 'UNEXPLAINED' && receipt.result.reviewedByUserId?.toLowerCase() === owner.userId &&
          receipt.result.reviewedAt !== null && receipt.result.reviewedRevision === pending.expectedRevision &&
          BigInt(receipt.result.revision) === BigInt(receipt.result.reviewedRevision) + 1n) &&
      (pending.action === 'PREPARE' ? BigInt(receipt.result.revision) === BigInt(pending.expectedRevision) + 1n : true);
    if (!identityMatches) return false;
    const [basisHash, resultHash] = await Promise.all([
      this.varianceRecordContentHash(receipt.preview.currentRecord), this.varianceRecordContentHash(receipt.result),
    ]);
    return basisHash === pending.basisContentHash && resultHash === pending.resultContentHash;
  }
  private definitiveVarianceRefusal(outcome: { ok: false; unknown: false; code: string; status: number }): boolean {
    return (outcome.status === 400 && ['mapping.invalid', 'gate.blocked', 'revision.stale', 'generation.stale'].includes(outcome.code)) ||
      (outcome.status === 403 && outcome.code === 'scope.denied') ||
      (outcome.status === 409 && ['revision.stale', 'generation.stale'].includes(outcome.code));
  }
  private clearVarianceRecovery(owner: { firmId: string; userId: string; engagementId: string }, expected: string): boolean {
    try {
      const key = this.varianceStorageKey(owner);
      if (sessionStorage.getItem(key) !== expected) throw new Error('reference changed');
      sessionStorage.removeItem(key);
      if (sessionStorage.getItem(key) !== null) throw new Error('remove failed');
      this.variancePending.set(null); this.varianceRecoveryUnavailable.set(false);
      return true;
    } catch {
      this.varianceError.set('The server confirmed the exact request, but its local recovery reference could not be cleared safely. It remains unresolved.');
      return false;
    }
  }
  async lookupVarianceRequest(): Promise<void> {
    const owner = this.varianceOwner(), pending = this.variancePending();
    if (!owner || !pending || this.varianceBusy() || this.varianceRecoveryUnavailable()) return;
    const state = this.varianceState(), key = this.varianceStorageKey(owner);
    if (!state || state.engagementId.toLowerCase() !== pending.engagementId || state.accountArea !== pending.accountArea || state.periodReference !== pending.periodReference) {
      this.varianceError.set('Load the exact account-area and period recorded with this request before looking it up.'); return;
    }
    let raw: string | null;
    try {
      raw = sessionStorage.getItem(key);
      if (raw === null || JSON.stringify(JSON.parse(raw)) !== JSON.stringify(pending)) throw new Error('reference changed');
    } catch { this.varianceRecoveryUnavailable.set(true); this.varianceError.set('The original recovery reference changed or is unavailable. No request was sent.'); return; }
    const operation = ++this.varianceOperation, context = this.varianceContext(owner);
    this.varianceBusy.set(true); this.varianceError.set(''); this.varianceMessage.set('');
    try {
      const lookup = await this.api.get(`/api/ui/engagements/${owner.engagementId}/completion/variance-investigations/receipts/${pending.requestId}?requestHash=${pending.requestHash}`, varianceLookupDecoder);
      if (!this.varianceIsCurrent(operation, context)) return;
      if (!lookup.found || !lookup.receipt) { this.varianceMessage.set('No receipt is currently available for this exact request. The unresolved reference remains; no new intent was sent.'); return; }
      const receiptMatches = await this.validVarianceReceipt(lookup.receipt, pending, state, owner);
      if (!this.varianceIsCurrent(operation, context)) return;
      if (!receiptMatches) throw new Error('The retained receipt does not match this exact actor, epoch, engagement and request. Its reference remains unresolved.');
      if (!this.clearVarianceRecovery(owner, raw)) return;
      this.varianceBusy.set(false);
      const refreshed = await this.loadVarianceState();
      if (context === this.varianceContext()) {
        const selected = this.selectedVarianceScope();
        this.varianceMessage.set(refreshed && selected?.accountArea === pending.accountArea && selected.periodReference === pending.periodReference
          ? `The server confirmed the retained ${pending.action.toLowerCase()} request ${pending.requestId}; the exact variance state was refreshed.`
          : `The server confirmed the retained ${pending.action.toLowerCase()} request ${pending.requestId}. Current state was not refreshed for the selected scope; reload it before relying on the display.`);
        if (refreshed && selected?.accountArea === pending.accountArea && selected.periodReference === pending.periodReference &&
            this.ws.data()?.engagementId.toLowerCase() === owner.engagementId && !this.ws.error()) this.ws.reload();
      }
    } catch (error) {
      if (this.varianceIsCurrent(operation, context)) this.varianceError.set(error instanceof Error ? error.message : 'The exact request could not be recovered. Its reference remains saved.');
    } finally { if (this.varianceIsCurrent(operation, context)) this.varianceBusy.set(false); }
  }
  /** Asks the server to re-check the stored signed report; the result is shown, never inferred in the browser. */
  async verifySignature(id: string): Promise<void> {
    const workspace = this.ws.data(), actor = this.signatureSession.current(), epoch = this.signatureSession.invalidation(), engagement = this.id();
    if (this.signatureDestroyed || this.verifying() || !actor?.staff || !workspace?.deliverables.some(d => d.id === id && d.signed && d.kind === 'INDEPENDENT_AUDITORS_REPORT')) return;
    const request = ++this.signatureRequest;
    const current = () => !this.signatureDestroyed && request === this.signatureRequest && workspace === this.ws.data() &&
      engagement === this.id() && actor === this.signatureSession.current() && epoch === this.signatureSession.invalidation();
    this.verifying.set(true); this.signatureCheck.set(null);
    try {
      const result = await this.api.get(`/api/ui/deliverables/${id}/signature`, decodeSignatureCheck);
      if (current()) this.signatureCheck.set(result);
    }
    catch { if (current()) this.signatureCheck.set({ signatureKind: 'VISUAL', state: 'UNREADABLE', subject: null, thumbprintSha256: null, message: 'The signature could not be checked. Refresh and try again.' }); }
    finally { if (current()) this.verifying.set(false); }
  }
  /** Explicit, idempotent dispatch of the current holding letter; the server refuses a superseded letter. */
  dispatchHoldingLetter(): void {
    this.send(`/api/ui/engagements/${this.id()}/holding-letter/dispatch`, {},
      'The holding letter is queued for the client-management recipient. It is not delivered until the mail provider confirms delivery.');
  }
  /** One attempt only: an unknown outcome is never retried. A stale refusal reloads the readiness and keeps the reason. */
  async lockEarly(r: { revision: number; archiveReadinessDigest: string | null }): Promise<void> {
    if (!r.archiveReadinessDigest) return;
    const locked = await this.cmd.run(`/api/ui/engagements/${this.id()}/early-lock`, { expectedRevision: String(r.revision),
      partnerConfirmed: this.early.confirmed, rationale: this.early.rationale.trim(), archiveReadinessDigest: r.archiveReadinessDigest },
      'The audit file is locked early and is read-only in AuditSphere. Provider protection is recorded separately.');
    if (locked) {
      this.early = { confirmed: false, rationale: '' };
      this.ws.reload();
    } else if (this.cmd.code() === 'generation.stale') {
      this.early = { confirmed: false, rationale: this.early.rationale };
      this.readiness.reload();
    }
  }
  report(kind: string): void {
    void this.cmd.run<{ message: string }>(this.base() + '/reports', { kind }, '', (v) => this.cmd.message.set(v.message)).finally(() => this.ws.reload());
  }
  upload(event: Event, url: string, ok: string): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file || file.size > 512 * 1024) { this.cmd.failed.set(true); this.cmd.message.set('Choose a PNG of at most 512 KB.'); return; }
    const form = new FormData(); form.set('file', file);
    void this.cmd.run(url, form, ok).finally(() => this.ws.reload());
  }
}
