import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, dec, guid, instant, int, nullable, obj, text } from '../../core/decode';
import { guidPattern } from '../../core/contracts';
import { SHARED } from '../../core/ui';

const MAX_LINES = 500;
const MAX_SOURCE_BYTES = 5_000_000;
const MAX_PREVIEW_LINES = 25;
const PAGE_SIZE = 25;
const LINE_PAGE_SIZE = 100;
const SCALE = 1_000_000n;
const MAX_AMOUNT_SCALED = BigInt('9'.repeat(19));

const lineShape = obj({ id: guid, taxonomyCode: text, amount: dec, currency: text, sourceLineReference: text });
const packShape = obj({
  id: guid, clientId: guid, clientLegalName: nullable(text), engagementId: guid, priorPackId: nullable(guid), version: int,
  periodStart: date, periodEnd: date, framework: text, reportingCurrency: text, periodBasis: text,
  taxonomyVersion: text, mappingVersion: text, sourceReference: text, rawSourceHash: text,
  normalizedSourceDigest: text, packDigest: text, declaredSignedTotal: dec, reconciledSignedTotal: dec,
  reconciliationStatus: text, reconciliationReference: text, reconciledByUserId: nullable(guid), reconciledAt: nullable(instant),
  compatibilityBridgeStatus: text, compatibilityBridgeReference: text,
  compatibilityBridgeApprovedByUserId: nullable(guid), compatibilityBridgeApprovedAt: nullable(instant),
  status: text, returnReason: text, submittedByUserId: guid, submittedAt: instant,
  returnedByUserId: nullable(guid), returnedAt: nullable(instant), approvedByUserId: nullable(guid), approvedAt: nullable(instant),
  canReconcile: bool, canReturn: bool, canApprove: bool, canPin: bool, currentBlockers: arr(text, 20),
});
const clientShape = obj({ clientId: guid, legalName: text, membershipEffectiveFrom: date });
const engagementShape = obj({
  engagementId: guid, clientId: guid, serviceRoute: text, periodStart: text, periodEnd: text, status: text,
});
const pageShape = obj({
  scopeVersionId: guid, groupId: guid, pinnedGroupRevision: int, currentGroupRevision: int,
  scopeStatus: text, reportingCurrency: text, method: text, currentUserId: guid,
  canSubmitPack: bool, canReviewPacks: bool, canPinApprovedPacks: bool,
  items: arr(packShape, PAGE_SIZE), totalCount: int, page: int, pageSize: int,
  eligibleClients: arr(clientShape, 100), eligibleClientTotalCount: int, eligibleClientPage: int,
  eligibleClientPageSize: int, clientSearch: text, resolvedClient: nullable(clientShape),
  eligibleEngagements: arr(engagementShape, 100), eligibleEngagementTotalCount: int, eligibleEngagementPage: int,
  eligibleEngagementPageSize: int, engagementSearch: text, resolvedEngagement: nullable(engagementShape),
});
const detailShape = obj({
  scopeVersionId: guid, groupId: guid, pinnedGroupRevision: int, currentGroupRevision: int,
  scopeStatus: text, currentUserId: guid, pack: packShape, lines: arr(lineShape, LINE_PAGE_SIZE),
  totalLineCount: int, linePage: int, linePageSize: int, hasPreviousLines: bool, hasNextLines: bool,
});

export function decodeExternalPackPage(value: unknown, path = '') {
  const page = pageShape(value, path);
  if (page.pinnedGroupRevision < 1 || page.currentGroupRevision < 1 || page.page < 1 || page.pageSize < 1 || page.pageSize > 100 ||
      page.items.length > page.pageSize || page.totalCount < page.items.length ||
      page.eligibleClientPage < 1 || page.eligibleClientPageSize < 1 || page.eligibleClientPageSize > 100 ||
      page.eligibleClients.length > page.eligibleClientPageSize || page.eligibleClientTotalCount < page.eligibleClients.length ||
      page.eligibleEngagementPage < 1 || page.eligibleEngagementPageSize < 1 || page.eligibleEngagementPageSize > 100 ||
      page.eligibleEngagements.length > page.eligibleEngagementPageSize || page.eligibleEngagementTotalCount < page.eligibleEngagements.length ||
      new Set(page.items.map(x => x.id.toLowerCase())).size !== page.items.length ||
      new Set(page.eligibleClients.map(x => x.clientId.toLowerCase())).size !== page.eligibleClients.length ||
      new Set(page.eligibleEngagements.map(x => x.engagementId.toLowerCase())).size !== page.eligibleEngagements.length ||
      page.canReviewPacks && page.pinnedGroupRevision !== page.currentGroupRevision ||
      page.canSubmitPack && (page.scopeStatus !== 'DRAFT' || page.pinnedGroupRevision !== page.currentGroupRevision) ||
      page.canPinApprovedPacks && !page.canSubmitPack ||
      page.items.some(x => x.version < 1 || x.periodEnd < x.periodStart ||
        x.canApprove && x.reconciliationStatus !== 'RECONCILED' ||
        x.canPin && (x.status !== 'APPROVED' || x.reconciliationStatus !== 'RECONCILED') ||
        (x.canReconcile || x.canReturn || x.canApprove || x.canPin) &&
          (page.scopeStatus !== 'DRAFT' || page.pinnedGroupRevision !== page.currentGroupRevision) ||
        !/^[0-9a-f]{64}$/.test(x.rawSourceHash) || !/^[0-9a-f]{64}$/.test(x.normalizedSourceDigest) ||
        !/^[0-9a-f]{64}$/.test(x.packDigest)))
    throw new Error('Unsupported external component pack workspace.');
  return page;
}

export function decodeExternalPackDetail(value: unknown, path = '') {
  const detail = detailShape(value, path);
  if (detail.pinnedGroupRevision < 1 || detail.currentGroupRevision < 1 || detail.linePage < 1 ||
      detail.linePageSize < 1 || detail.linePageSize > LINE_PAGE_SIZE || detail.lines.length > detail.linePageSize ||
      detail.totalLineCount < detail.lines.length || detail.lines.some(x => x.currency !== detail.pack.reportingCurrency) ||
      (detail.pack.canReconcile || detail.pack.canReturn || detail.pack.canApprove || detail.pack.canPin) &&
        (detail.scopeStatus !== 'DRAFT' || detail.pinnedGroupRevision !== detail.currentGroupRevision) ||
      !/^[0-9a-f]{64}$/.test(detail.pack.rawSourceHash) || !/^[0-9a-f]{64}$/.test(detail.pack.normalizedSourceDigest) ||
      !/^[0-9a-f]{64}$/.test(detail.pack.packDigest))
    throw new Error('Unsupported external component pack detail.');
  return detail;
}

type PackPage = ReturnType<typeof decodeExternalPackPage>;
type PackDetail = ReturnType<typeof decodeExternalPackDetail>;
type PackSummary = PackPage['items'][number];
type ClientOption = PackPage['eligibleClients'][number];
type EngagementOption = PackPage['eligibleEngagements'][number];
type SourceLine = { taxonomyCode: string; amount: string; currency: string; sourceLineReference: string };

function parseScaledAmount(value: string): bigint | null {
  const normalized = value.trim();
  const match = /^(-?)(\d{1,13})(?:\.(\d{1,6}))?$/.exec(normalized);
  if (!match || normalized.length > 21) return null;
  const magnitude = BigInt(match[2]) * SCALE + BigInt((match[3] ?? '').padEnd(6, '0') || '0');
  if (magnitude > MAX_AMOUNT_SCALED) return null;
  return match[1] ? -magnitude : magnitude;
}

function formatScaledAmount(value: bigint): string {
  const sign = value < 0n ? '-' : '';
  const magnitude = value < 0n ? -value : value;
  return `${sign}${magnitude / SCALE}.${(magnitude % SCALE).toString().padStart(6, '0')}`;
}

function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00.000Z`);
  return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}

function ordinal(a: string, b: string): number { return a < b ? -1 : a > b ? 1 : 0; }

function parseSourceLines(value: unknown): SourceLine[] {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !Array.isArray((value as { lines?: unknown }).lines))
    throw new Error('Choose a JSON source file with a lines array.');
  const rawLines = (value as { lines: unknown[] }).lines;
  if (rawLines.length < 1 || rawLines.length > MAX_LINES)
    throw new Error(`The source must contain between 1 and ${MAX_LINES} lines.`);
  return rawLines.map((raw, index) => {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error(`Source line ${index + 1} is not an object.`);
    const row = raw as Record<string, unknown>;
    const amount = typeof row['amount'] === 'string' ? parseScaledAmount(row['amount']) : null;
    if (typeof row['taxonomyCode'] !== 'string' || !row['taxonomyCode'].trim() || row['taxonomyCode'].length > 100 ||
        amount === null || typeof row['currency'] !== 'string' || !/^[A-Z]{3}$/.test(row['currency']) ||
        typeof row['sourceLineReference'] !== 'string' || !row['sourceLineReference'].trim() || row['sourceLineReference'].length > 200)
      throw new Error(`Source line ${index + 1} needs a taxonomy code, exact decimal string, uppercase currency and source-line reference.`);
    return {
      taxonomyCode: row['taxonomyCode'].trim(), amount: formatScaledAmount(amount),
      currency: row['currency'], sourceLineReference: row['sourceLineReference'].trim(),
    };
  });
}

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const copy = new ArrayBuffer(bytes.byteLength);
  new Uint8Array(copy).set(bytes);
  const digest = await globalThis.crypto.subtle.digest('SHA-256', copy);
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

function normalizedLineDigestInput(lines: readonly SourceLine[]): Uint8Array {
  const canonical = [...lines].sort((a, b) => ordinal(a.sourceLineReference, b.sourceLineReference) ||
    ordinal(a.taxonomyCode, b.taxonomyCode) || ordinal(a.currency, b.currency) || ordinal(a.amount, b.amount))
    .map(line => [line.taxonomyCode, line.amount, line.currency, line.sourceLineReference].join('|')).join('\n');
  return new TextEncoder().encode(canonical);
}

function isHash(value: string): boolean { return /^[0-9a-f]{64}$/.test(value); }

@Component({
  selector: 'audit-consolidation-external-packs',
  imports: [FormsModule, ...SHARED],
  template: `
    <section class="panel" id="external-pack-workbench" aria-labelledby="external-pack-workbench-heading">
      <h2 id="external-pack-workbench-heading">External component packs</h2>
      <p>Source packs and every returned or resubmitted version stay visible with their source hashes, lines, reconciliation evidence and bridge lineage. Intake never writes to a component book or calls an external provider.</p>
      <audit-state [loading]="workspace.loading()" [error]="workspace.error()" label="external component pack history" />

      @if (workspace.data(); as w) {
        <p>Scope <code>{{ w.scopeVersionId }}</code> · group <code>{{ w.groupId }}</code> · pinned revision {{ w.pinnedGroupRevision }} · current revision {{ w.currentGroupRevision }}</p>
        @if (!scopeCurrent(w)) {
          <p role="alert">This scope is not a current draft at its pinned group revision. Create a new scope version before submitting, reviewing or pinning a pack.</p>
        }

        <div class="table-scroll">
          <table>
            <caption>Retained external component source pack history</caption>
            <thead><tr><th>Entity / version</th><th>Source identity</th><th>Period / basis</th><th>Declared / reconciled total</th><th>Review / compatibility</th><th>Status / actions</th></tr></thead>
            <tbody>@for (pack of w.items; track pack.id) {
              <tr>
                <td>{{ pack.clientLegalName || 'Entity name unavailable' }}<small><code>{{ pack.clientId }}</code></small><small>Pack v{{ pack.version }} · <code>{{ pack.id }}</code></small>
                  @if (pack.priorPackId) { <small>Resubmits <code>{{ pack.priorPackId }}</code></small> }
                </td>
                <td>{{ pack.sourceReference }}<small>Raw source SHA-256 <code>{{ pack.rawSourceHash }}</code></small><small>Normalized lines SHA-256 <code>{{ pack.normalizedSourceDigest }}</code></small><small>Pack SHA-256 <code>{{ pack.packDigest }}</code></small></td>
                <td>{{ pack.periodStart }} – {{ pack.periodEnd }}<small>{{ pack.framework }} · {{ pack.reportingCurrency }}</small><small>{{ pack.periodBasis }} · taxonomy {{ pack.taxonomyVersion }} · mapping {{ pack.mappingVersion }}</small></td>
                <td>{{ pack.declaredSignedTotal }} {{ pack.reportingCurrency }}<small>{{ pack.reconciliationStatus }} · {{ pack.reconciledSignedTotal }} {{ pack.reportingCurrency }}</small></td>
                <td>{{ pack.reconciliationReference || 'No reconciliation evidence' }}<small>{{ pack.compatibilityBridgeStatus }} · {{ pack.compatibilityBridgeReference || 'No bridge reference' }}</small>
                  @if (pack.returnReason) { <small>Returned: {{ pack.returnReason }}</small> }
                  <small>Submitted {{ pack.submittedAt }} by <code>{{ pack.submittedByUserId }}</code></small>
                </td>
                <td><audit-status [value]="pack.status" />
                  <button type="button" (click)="selectPack(pack.id)" [disabled]="selectedPackId() === pack.id">View retained lines and review</button>
                  @if (pack.canReconcile) { <small>Eligible for independent reconciliation</small> }
                  @if (pack.canApprove) { <small>Eligible for independent approval</small> }
                  @for (blocker of pack.currentBlockers; track blocker) { <small>{{ blocker }}</small> }
                </td>
              </tr>
            } @empty { <tr><td colspan="6">No external component packs have been submitted in this scope.</td></tr> }</tbody>
          </table>
        </div>
        <nav class="actions" aria-label="External pack history pages">
          <button type="button" (click)="changePage(-1)" [disabled]="w.page <= 1 || workspace.loading()">Previous packs</button>
          <span>Page {{ w.page }} · {{ w.totalCount }} retained pack versions</span>
          <button type="button" (click)="changePage(1)" [disabled]="w.page * w.pageSize >= w.totalCount || workspace.loading()">Next packs</button>
          <button type="button" (click)="workspace.reload()" [disabled]="workspace.loading()">Refresh pack history</button>
        </nav>

        @if (selectedPackId()) {
          <section class="panel" aria-labelledby="external-pack-detail-heading">
            <h3 id="external-pack-detail-heading">Selected retained pack</h3>
            <audit-state [loading]="detail.loading()" [error]="detail.error()" label="the selected external component pack" />
            @if (detail.data(); as d) {
              @if (!detailCurrent(d, w)) {
                <p role="alert">This retained detail is read-only. Changes require a current draft scope and matching group revision; refresh the evidence if its revision changed.</p>
              }
              <dl>
                <dt>Pack identity</dt><dd><code>{{ d.pack.id }}</code> · version {{ d.pack.version }} · {{ d.pack.status }}</dd>
                <dt>Entity and engagement</dt><dd>{{ d.pack.clientLegalName || 'Entity name unavailable' }} · <code>{{ d.pack.clientId }}</code> · <code>{{ d.pack.engagementId }}</code></dd>
                <dt>Original source reference and raw hash</dt><dd>{{ d.pack.sourceReference }} · <code>{{ d.pack.rawSourceHash }}</code></dd>
                <dt>Normalized line digest / pack digest</dt><dd><code>{{ d.pack.normalizedSourceDigest }}</code> · <code>{{ d.pack.packDigest }}</code></dd>
                <dt>Period / basis / taxonomy / mapping</dt><dd>{{ d.pack.periodStart }}–{{ d.pack.periodEnd }} · {{ d.pack.periodBasis }} · {{ d.pack.taxonomyVersion }} · {{ d.pack.mappingVersion }}</dd>
                <dt>Declared / reconciled totals</dt><dd>{{ d.pack.declaredSignedTotal }} / {{ d.pack.reconciledSignedTotal }} {{ d.pack.reportingCurrency }}</dd>
                <dt>Reconciliation</dt><dd>{{ d.pack.reconciliationStatus }} · {{ d.pack.reconciliationReference || 'No evidence reference' }} · {{ d.pack.reconciledByUserId || 'Not reconciled' }} · {{ d.pack.reconciledAt || '' }}</dd>
                <dt>Compatibility bridge</dt><dd>{{ d.pack.compatibilityBridgeStatus }} · {{ d.pack.compatibilityBridgeReference || 'No bridge reference' }} · {{ d.pack.compatibilityBridgeApprovedByUserId || 'Not approved' }}</dd>
                <dt>Return / resubmission lineage</dt><dd>{{ d.pack.returnReason || 'No return reason' }} · predecessor {{ d.pack.priorPackId || 'None' }} · returned by {{ d.pack.returnedByUserId || 'Not returned' }}</dd>
                <dt>Pack approval</dt><dd>{{ d.pack.approvedByUserId || 'Not approved' }} · {{ d.pack.approvedAt || '' }}</dd>
              </dl>
              <div class="table-scroll">
                <table>
                  <caption>Normalized source lines · page {{ d.linePage }} · {{ d.totalLineCount }} retained lines</caption>
                  <thead><tr><th>Taxonomy</th><th>Exact amount</th><th>Currency</th><th>Source line reference</th></tr></thead>
                  <tbody>@for (line of d.lines; track line.id) {
                    <tr><td>{{ line.taxonomyCode }}</td><td>{{ line.amount }}</td><td>{{ line.currency }}</td><td>{{ line.sourceLineReference }}</td></tr>
                  } @empty { <tr><td colspan="4">No source lines are available on this page.</td></tr> }</tbody>
                </table>
              </div>
              <nav class="actions" aria-label="Retained source line pages">
                <button type="button" (click)="changeLinePage(-1)" [disabled]="!d.hasPreviousLines || detail.loading()">Previous lines</button>
                <span>Page {{ d.linePage }} · {{ d.totalLineCount }} source lines</span>
                <button type="button" (click)="changeLinePage(1)" [disabled]="!d.hasNextLines || detail.loading()">Next lines</button>
              </nav>

              @if (detailCurrent(d, w) && (d.pack.canReconcile || d.pack.canReturn || d.pack.canApprove)) {
                <section class="panel" aria-labelledby="external-pack-review-heading">
                  <h4 id="external-pack-review-heading">Independent pack review</h4>
                  <p>Review every retained line page, the original hashes, totals and references before recording a decision.</p>
                  @if (d.pack.canReconcile && w.canReviewPacks) {
                    <label>Reconciliation evidence reference * <input name="reconciliationEvidence" [ngModel]="reconciliationEvidence()" (ngModelChange)="reconciliationEvidence.set($event); reconcileReviewed.set(false)" maxlength="2000" /></label>
                    <label><input type="checkbox" name="reconcileReviewed" [ngModel]="reconcileReviewed()" (ngModelChange)="reconcileReviewed.set($event)" /> I reviewed the retained source and every line page at group revision {{ w.currentGroupRevision }}.</label>
                    <button type="button" (click)="reconcilePack(d.pack)" [disabled]="mutationBlocked() || cmd().busy() || cmd().uncertain() || !d.pack.canReconcile || !reconcileReviewed() || !reconciliationEvidence().trim()">Reconcile source lines</button>
                  }
                  @if (d.pack.canReturn && w.canReviewPacks) {
                    <label>Return reason * <textarea name="returnReason" [ngModel]="returnReason()" (ngModelChange)="returnReason.set($event); returnReviewed.set(false)" maxlength="2000"></textarea></label>
                    <label><input type="checkbox" name="returnReviewed" [ngModel]="returnReviewed()" (ngModelChange)="returnReviewed.set($event)" /> I reviewed this pack and its return reason at group revision {{ w.currentGroupRevision }}.</label>
                    <button type="button" (click)="returnPack(d.pack)" [disabled]="mutationBlocked() || cmd().busy() || cmd().uncertain() || !d.pack.canReturn || !returnReviewed() || !returnReason().trim()">Return this pack</button>
                  }
                  @if (d.pack.canApprove && w.canReviewPacks) {
                    <label><input type="checkbox" name="approveReviewed" [ngModel]="approveReviewed()" (ngModelChange)="approveReviewed.set($event)" /> I independently reviewed all retained source lines, reconciliation and bridge state at group revision {{ w.currentGroupRevision }}.</label>
                    <button type="button" (click)="approvePack(d.pack)" [disabled]="mutationBlocked() || cmd().busy() || cmd().uncertain() || !d.pack.canApprove || !approveReviewed()">Approve source pack</button>
                  }
                </section>
              }

              @if (detailCurrent(d, w) && d.pack.canPin && w.canPinApprovedPacks) {
                <section class="panel" aria-labelledby="external-pack-pin-heading">
                  <h4 id="external-pack-pin-heading">Pin approved source as a component</h4>
                  <p>The existing consolidation service requires 100 ownership percentage points and CONTROLLED for this pin action. Confirm both values explicitly.</p>
                  <label>Ownership percentage points * <input name="pinOwnership" inputmode="decimal" [ngModel]="pinOwnershipPercent()" (ngModelChange)="pinOwnershipPercent.set($event); pinReviewed.set(false)" maxlength="10" /></label>
                  <label>Control method * <select name="pinControl" [ngModel]="pinControlMethod()" (ngModelChange)="pinControlMethod.set($event); pinReviewed.set(false)"><option value="">Choose the evidenced control method</option><option value="CONTROLLED">CONTROLLED</option></select></label>
                  <label><input type="checkbox" name="pinReviewed" [ngModel]="pinReviewed()" (ngModelChange)="pinReviewed.set($event)" /> I reviewed this approved pack, exact ownership and control values at group revision {{ w.currentGroupRevision }}.</label>
                  <button type="button" (click)="pinPack(d.pack)" [disabled]="mutationBlocked() || cmd().busy() || cmd().uncertain() || !d.pack.canPin || !pinReviewed() || !validPinValues()">Submit external component</button>
                </section>
              }
            }
          </section>
        }

        @if (w.canSubmitPack && scopeCurrent(w)) {
          <form class="panel" (ngSubmit)="submitPack(w)" aria-labelledby="external-pack-intake-heading">
            <h3 id="external-pack-intake-heading">Submit external source evidence</h3>
            <fieldset [disabled]="cmd().busy() || cmd().uncertain() || sourceBusy()">
              <legend>Exact source identity and typed line data</legend>
              <label>Approved group entity * <select name="clientId" [ngModel]="form.clientId" (ngModelChange)="clientChanged($event)" required>
                <option value="">Choose an approved member</option>
                @if (form.clientId && !clientResolved(w)) { <option [value]="form.clientId" disabled>Selection is no longer confirmed by the current group grant</option> }
                @for (client of clientOptions(w); track client.clientId) { <option [value]="client.clientId">{{ client.legalName }} · {{ client.clientId }} · membership from {{ client.membershipEffectiveFrom }}</option> }
              </select></label>
              <div class="option-pages">
                <label>Find eligible entity <input name="clientSearch" [(ngModel)]="clientSearchInput" (keydown.enter)="$event.preventDefault(); searchClients(w)" maxlength="100" /></label>
                <button type="button" (click)="searchClients(w)" [disabled]="workspace.loading()">Search entities</button>
                <button type="button" (click)="changeClientPage(w, -1)" [disabled]="w.eligibleClientPage <= 1 || workspace.loading()">Previous entities</button>
                <span>Entity page {{ w.eligibleClientPage }} · {{ w.eligibleClientTotalCount }} eligible</span>
                <button type="button" (click)="changeClientPage(w, 1)" [disabled]="w.eligibleClientPage * w.eligibleClientPageSize >= w.eligibleClientTotalCount || workspace.loading()">Next entities</button>
              </div>
              <label>Entity engagement * <select name="engagementId" [ngModel]="form.engagementId" (ngModelChange)="engagementChanged($event)" required>
                <option value="">Choose the source engagement</option>
                @if (form.engagementId && !engagementResolved(w)) { <option [value]="form.engagementId" disabled>Selection is no longer confirmed for this entity</option> }
                @for (engagement of engagementOptions(w); track engagement.engagementId) { <option [value]="engagement.engagementId">{{ engagement.serviceRoute }} · {{ engagement.periodStart }}–{{ engagement.periodEnd }} · {{ engagement.status }} · {{ engagement.engagementId }}</option> }
              </select></label>
              <div class="option-pages">
                <label>Find eligible engagement <input name="engagementSearch" [(ngModel)]="engagementSearchInput" (keydown.enter)="$event.preventDefault(); searchEngagements(w)" maxlength="100" /></label>
                <button type="button" (click)="searchEngagements(w)" [disabled]="workspace.loading() || !clientResolved(w)">Search engagements</button>
                <button type="button" (click)="changeEngagementPage(w, -1)" [disabled]="w.eligibleEngagementPage <= 1 || workspace.loading()">Previous engagements</button>
                <span>Engagement page {{ w.eligibleEngagementPage }} · {{ w.eligibleEngagementTotalCount }} eligible</span>
                <button type="button" (click)="changeEngagementPage(w, 1)" [disabled]="w.eligibleEngagementPage * w.eligibleEngagementPageSize >= w.eligibleEngagementTotalCount || workspace.loading()">Next engagements</button>
              </div>
              <label>Period start * <input name="periodStart" type="date" [(ngModel)]="form.periodStart" (ngModelChange)="prepareReviewed.set(false)" required /></label>
              <label>Period end * <input name="periodEnd" type="date" [(ngModel)]="form.periodEnd" (ngModelChange)="prepareReviewed.set(false)" required /></label>
              <label>Framework * <input name="framework" [(ngModel)]="form.framework" (ngModelChange)="prepareReviewed.set(false)" maxlength="100" required /></label>
              <label>Reporting currency * <input name="currency" [(ngModel)]="form.reportingCurrency" (ngModelChange)="prepareReviewed.set(false)" maxlength="3" required /></label>
              <label>Period basis * <input name="periodBasis" [(ngModel)]="form.periodBasis" (ngModelChange)="prepareReviewed.set(false)" maxlength="100" required /></label>
              <label>Taxonomy version * <input name="taxonomy" [(ngModel)]="form.taxonomyVersion" (ngModelChange)="prepareReviewed.set(false)" maxlength="100" required /></label>
              <label>Mapping version * <input name="mapping" [(ngModel)]="form.mappingVersion" (ngModelChange)="prepareReviewed.set(false)" maxlength="100" required /></label>
              <label>Source evidence reference * <input name="sourceReference" [(ngModel)]="form.sourceReference" (ngModelChange)="prepareReviewed.set(false)" maxlength="2000" required /></label>
              <label>Source JSON file * <input name="sourceFile" type="file" accept="application/json,.json" (change)="sourceFileSelected($event)" /></label>
              @if (sourceBusy()) { <p role="status">Hashing the selected source bytes and normalized lines…</p> }
              @if (sourceError()) { <p role="alert">{{ sourceError() }}</p> }
              @if (sourceName()) {
                <dl><dt>Selected source file</dt><dd>{{ sourceName() }}</dd>
                  <dt>Raw source SHA-256</dt><dd><code>{{ rawSourceHash() }}</code></dd>
                  <dt>Normalized lines SHA-256</dt><dd><code>{{ normalizedSourceDigest() }}</code></dd></dl>
              }
              <label>Returned pack to resubmit * <select name="priorPackId" [ngModel]="form.priorPackId" (ngModelChange)="priorPackChanged($event, w)">
                <option value="">No prior returned version</option>
                @if (form.priorPackId && !priorPackResolved(w)) { <option [value]="form.priorPackId" disabled>Returned version is no longer confirmed for this entity</option> }
                @for (prior of returnedPacksFor(w, form.clientId); track prior.id) { <option [value]="prior.id">Version {{ prior.version }} · {{ prior.periodStart }}–{{ prior.periodEnd }} · {{ prior.id }}</option> }
              </select></label>
              <p>Choose a returned version from retained pack history before resubmitting it. A resubmission is a new version; the returned source remains unchanged.</p>
              <label>Compatibility bridge reference <input name="bridgeReference" [(ngModel)]="form.compatibilityBridgeReference" (ngModelChange)="prepareReviewed.set(false)" maxlength="2000" /></label>
              <div class="table-scroll">
                <table><caption>Typed source preview · showing {{ sourcePreview().length }} of {{ sourceLines().length }} normalized lines · {{ MAX_PREVIEW_LINES }}-line preview bound</caption>
                  <thead><tr><th>Taxonomy</th><th>Exact amount</th><th>Currency</th><th>Source line reference</th></tr></thead>
                  <tbody>@for (line of sourcePreview(); track line.sourceLineReference + ':' + line.taxonomyCode + ':' + $index) {
                    <tr><td>{{ line.taxonomyCode }}</td><td>{{ line.amount }}</td><td>{{ line.currency }}</td><td>{{ line.sourceLineReference }}</td></tr>
                  } @empty { <tr><td colspan="4">Select the actual source JSON to preview normalized lines.</td></tr> }</tbody>
                </table>
              </div>
              @if (sourceLines().length > MAX_PREVIEW_LINES) { <p>All {{ sourceLines().length }} validated lines are included in the hash and submission; only the first {{ MAX_PREVIEW_LINES }} are shown here.</p> }
              @if (sourceLines().length) { <p>Exact declared signed total: <strong>{{ declaredSignedTotal() }} {{ form.reportingCurrency || 'source currency' }}</strong></p> }
              @if (hasCurrencyMismatch()) { <p role="alert">Every normalized line currency must match the pack reporting currency exactly.</p> }
              <label><input type="checkbox" name="prepareReviewed" [ngModel]="prepareReviewed()" (ngModelChange)="prepareReviewed.set($event)" /> I reviewed the selected source bytes, calculated hashes, typed-line preview, exact amounts and current scope/group revision.</label>
              <button type="submit" [disabled]="mutationBlocked() || !w.canSubmitPack || !scopeCurrent(w) || cmd().busy() || cmd().uncertain() || sourceBusy() || !prepareReviewed() || !validPackForm(w)">Submit immutable source pack</button>
            </fieldset>
          </form>
        } @else {
          <p>Pack intake requires server-confirmed authority on the current draft scope and group revision.</p>
        }
      }
      <audit-command-message [message]="cmd().message()" [failed]="cmd().failed()" />
      @if (cmd().uncertain()) {
        <p role="alert">The result is unknown. Refresh retained pack history and confirm the exact pack before repeating any action.</p>
      }
    </section>
  `,
  styles: [`:host { display: block; min-width: 0; } label { display: block; margin-block: .65rem; }
    input:not([type=checkbox]), select, textarea { display: block; max-width: 100%; } small { display: block; overflow-wrap: anywhere; }
    code { overflow-wrap: anywhere; } .option-pages { display: flex; flex-wrap: wrap; align-items: end; gap: .5rem; margin-block: .5rem 1rem; }
    .option-pages label { margin: 0; }`],
})
export class ConsolidationExternalPacks {
  private readonly api = inject(Api);
  private lastScopeId = '';
  private sourceGeneration = 0;
  readonly scopeId = input.required<string>();
  readonly mutationBlocked = input(false);
  readonly componentPinned = output<void>();
  readonly page = signal(1);
  readonly clientPage = signal(1);
  readonly engagementPage = signal(1);
  readonly linePage = signal(1);
  readonly selectedClientId = signal('');
  readonly selectedEngagementId = signal('');
  readonly clientSearch = signal('');
  readonly engagementSearch = signal('');
  clientSearchInput = '';
  engagementSearchInput = '';
  readonly selectedPackId = signal('');
  readonly priorPackChoice = signal<PackSummary | null>(null);
  readonly sourceBusy = signal(false);
  readonly sourceError = signal('');
  readonly sourceName = signal('');
  readonly sourceLines = signal<SourceLine[]>([]);
  readonly sourcePreview = computed(() => this.sourceLines().slice(0, MAX_PREVIEW_LINES));
  readonly rawSourceHash = signal('');
  readonly normalizedSourceDigest = signal('');
  readonly prepareReviewed = signal(false);
  readonly reconcileReviewed = signal(false);
  readonly returnReviewed = signal(false);
  readonly approveReviewed = signal(false);
  readonly pinReviewed = signal(false);
  readonly reconciliationEvidence = signal('');
  readonly returnReason = signal('');
  readonly pinOwnershipPercent = signal('');
  readonly pinControlMethod = signal('');
  readonly cmd = signal(new CommandState(this.api));
  readonly MAX_PREVIEW_LINES = MAX_PREVIEW_LINES;
  readonly declaredSignedTotal = computed(() => {
    const lines = this.sourceLines();
    if (!lines.length) return '';
    const total = lines.reduce((sum, line) => sum + parseScaledAmount(line.amount)!, 0n);
    return total > MAX_AMOUNT_SCALED || total < -MAX_AMOUNT_SCALED ? '' : formatScaledAmount(total);
  });
  readonly workspace = this.api.resource<PackPage>(() => {
    if (!guidPattern.test(this.scopeId())) return null;
    const query = new URLSearchParams({
      page: String(this.page()), pageSize: String(PAGE_SIZE),
      clientPage: String(this.clientPage()), clientPageSize: String(PAGE_SIZE), clientSearch: this.clientSearch(),
      engagementPage: String(this.engagementPage()), engagementPageSize: String(PAGE_SIZE), engagementSearch: this.engagementSearch(),
    });
    if (this.selectedClientId()) query.set('selectedClientId', this.selectedClientId().toLowerCase());
    if (this.selectedEngagementId()) query.set('selectedEngagementId', this.selectedEngagementId().toLowerCase());
    return `/api/ui/consolidation/scopes/${this.scopeId().toLowerCase()}/external-packs?${query}`;
  }, (value, path) => {
    const page = decodeExternalPackPage(value, path);
    if (page.scopeVersionId.toLowerCase() !== this.scopeId().toLowerCase() || page.page !== this.page() ||
        page.pageSize !== PAGE_SIZE || page.eligibleClientPage !== this.clientPage() || page.eligibleClientPageSize !== PAGE_SIZE ||
        page.clientSearch !== this.clientSearch() || page.eligibleEngagementPage !== this.engagementPage() ||
        page.eligibleEngagementPageSize !== PAGE_SIZE || page.engagementSearch !== this.engagementSearch())
      throw new Error('External pack page context changed.');
    return page;
  }, 'External pack history is unavailable in your current group grant.');
  readonly detail = this.api.resource<PackDetail>(() =>
    guidPattern.test(this.scopeId()) && guidPattern.test(this.selectedPackId())
      ? `/api/ui/consolidation/scopes/${this.scopeId().toLowerCase()}/external-packs/${this.selectedPackId().toLowerCase()}?linePage=${this.linePage()}&linePageSize=${LINE_PAGE_SIZE}`
      : null,
  (value, path) => {
    const detail = decodeExternalPackDetail(value, path);
    const page = this.workspace.data();
    if (detail.scopeVersionId.toLowerCase() !== this.scopeId().toLowerCase() ||
        detail.pack.id.toLowerCase() !== this.selectedPackId().toLowerCase() || detail.linePage !== this.linePage() ||
        detail.linePageSize !== LINE_PAGE_SIZE || page && detail.groupId.toLowerCase() !== page.groupId.toLowerCase())
      throw new Error('External pack detail context changed.');
    return detail;
  }, 'The selected external pack is unavailable in this scope.');

  form = {
    clientId: '', engagementId: '', periodStart: '', periodEnd: '', framework: '', reportingCurrency: '',
    periodBasis: '', taxonomyVersion: '', mappingVersion: '', sourceReference: '', priorPackId: '',
    compatibilityBridgeReference: '',
  };

  constructor() {
    effect(() => {
      const scopeId = this.scopeId().toLowerCase();
      if (scopeId === this.lastScopeId) return;
      this.lastScopeId = scopeId;
      untracked(() => this.resetForScope());
    });
  }

  scopeCurrent(page: PackPage): boolean {
    return page.scopeStatus === 'DRAFT' && page.pinnedGroupRevision === page.currentGroupRevision;
  }

  detailCurrent(detail: PackDetail, page: PackPage): boolean {
    return this.scopeCurrent(page) && detail.scopeVersionId.toLowerCase() === page.scopeVersionId.toLowerCase() &&
      detail.groupId.toLowerCase() === page.groupId.toLowerCase() &&
      detail.pinnedGroupRevision === page.pinnedGroupRevision && detail.currentGroupRevision === page.currentGroupRevision &&
      detail.scopeStatus === page.scopeStatus;
  }

  clientResolved(page: PackPage): boolean {
    return !!this.form.clientId && page.resolvedClient?.clientId.toLowerCase() === this.form.clientId.toLowerCase() &&
      page.resolvedClient.clientId.toLowerCase() === this.selectedClientId().toLowerCase();
  }

  engagementResolved(page: PackPage): boolean {
    const resolved = page.resolvedEngagement;
    return !!this.form.engagementId && !!resolved && resolved.engagementId.toLowerCase() === this.form.engagementId.toLowerCase() &&
      resolved.engagementId.toLowerCase() === this.selectedEngagementId().toLowerCase() &&
      resolved.clientId.toLowerCase() === this.form.clientId.toLowerCase();
  }

  clientOptions(page: PackPage): ClientOption[] {
    const options = [...page.eligibleClients];
    const selected = page.resolvedClient;
    if (selected && selected.clientId.toLowerCase() === this.form.clientId.toLowerCase() &&
        !options.some(x => x.clientId.toLowerCase() === selected.clientId.toLowerCase())) options.push(selected);
    return options;
  }

  engagementOptions(page: PackPage): EngagementOption[] {
    const options = page.eligibleEngagements.filter(x => x.clientId.toLowerCase() === this.form.clientId.toLowerCase());
    const selected = page.resolvedEngagement;
    if (selected && selected.clientId.toLowerCase() === this.form.clientId.toLowerCase() &&
        !options.some(x => x.engagementId.toLowerCase() === selected.engagementId.toLowerCase())) options.push(selected);
    return options;
  }

  returnedPacksFor(page: PackPage, clientId: string): PackSummary[] {
    const returned = page.items.filter(x => x.status === 'RETURNED' && x.clientId.toLowerCase() === clientId.toLowerCase());
    const selected = this.priorPackChoice();
    if (selected && selected.status === 'RETURNED' && selected.clientId.toLowerCase() === clientId.toLowerCase() &&
        !returned.some(x => x.id.toLowerCase() === selected.id.toLowerCase())) returned.push(selected);
    return returned;
  }

  priorPackResolved(page: PackPage): boolean {
    const prior = this.returnedPacksFor(page, this.form.clientId).find(x => x.id.toLowerCase() === this.form.priorPackId.toLowerCase());
    return !!prior && prior.status === 'RETURNED' && prior.clientId.toLowerCase() === this.form.clientId.toLowerCase();
  }

  selectPack(id: string): void {
    this.selectedPackId.set(id);
    this.linePage.set(1);
    this.clearReviewAssent();
  }

  clientChanged(clientId: string): void {
    this.prepareReviewed.set(false);
    this.form.clientId = clientId;
    this.form.engagementId = '';
    this.form.priorPackId = '';
    this.priorPackChoice.set(null);
    this.selectedClientId.set(clientId);
    this.selectedEngagementId.set('');
    this.engagementPage.set(1);
    this.engagementSearch.set('');
    this.clearReviewAssent();
  }

  engagementChanged(engagementId: string): void {
    this.prepareReviewed.set(false);
    this.form.engagementId = engagementId;
    this.selectedEngagementId.set(engagementId);
    this.clearReviewAssent();
  }

  priorPackChanged(packId: string, page: PackPage): void {
    this.form.priorPackId = packId;
    this.priorPackChoice.set(page.items.find(x => x.id.toLowerCase() === packId.toLowerCase() &&
      x.status === 'RETURNED' && x.clientId.toLowerCase() === this.form.clientId.toLowerCase()) ?? null);
    this.prepareReviewed.set(false);
  }

  searchClients(page: PackPage): void {
    this.clientSearch.set(this.clientSearchInput.trim().slice(0, 100));
    this.clientPage.set(1);
    this.prepareReviewed.set(false);
    if (page.clientSearch === this.clientSearch()) this.workspace.reload();
  }

  searchEngagements(page: PackPage): void {
    this.engagementSearch.set(this.engagementSearchInput.trim().slice(0, 100));
    this.engagementPage.set(1);
    this.prepareReviewed.set(false);
    if (page.engagementSearch === this.engagementSearch()) this.workspace.reload();
  }

  changeClientPage(page: PackPage, delta: number): void {
    const next = page.eligibleClientPage + delta;
    if (next > 0 && next <= Math.ceil(page.eligibleClientTotalCount / page.eligibleClientPageSize)) {
      this.clientPage.set(next);
      this.prepareReviewed.set(false);
    }
  }

  changeEngagementPage(page: PackPage, delta: number): void {
    const next = page.eligibleEngagementPage + delta;
    if (next > 0 && next <= Math.ceil(page.eligibleEngagementTotalCount / page.eligibleEngagementPageSize)) {
      this.engagementPage.set(next);
      this.prepareReviewed.set(false);
    }
  }

  changePage(delta: number): void {
    const target = this.page() + delta;
    const data = this.workspace.data();
    if (data && target > 0 && target <= Math.ceil(data.totalCount / data.pageSize)) {
      this.page.set(target);
      this.selectedPackId.set('');
      this.clearReviewAssent();
    }
  }

  changeLinePage(delta: number): void {
    const detail = this.detail.data();
    if (detail && this.linePage() + delta > 0 &&
        (delta < 0 ? detail.hasPreviousLines : detail.hasNextLines)) this.linePage.update(x => x + delta);
  }

  async sourceFileSelected(event: Event): Promise<void> {
    const file = (event.currentTarget as HTMLInputElement).files?.[0];
    const generation = ++this.sourceGeneration;
    this.sourceBusy.set(false);
    this.sourceError.set('');
    this.sourceName.set('');
    this.sourceLines.set([]);
    this.rawSourceHash.set('');
    this.normalizedSourceDigest.set('');
    this.prepareReviewed.set(false);
    if (!file) return;
    if (file.size > MAX_SOURCE_BYTES) {
      this.sourceError.set(`The source file exceeds the ${MAX_SOURCE_BYTES.toLocaleString()} byte limit.`);
      return;
    }

    this.sourceBusy.set(true);
    try {
      const bytes = await file.arrayBuffer();
      const raw = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
      const lines = parseSourceLines(JSON.parse(raw) as unknown);
      const rawHash = await sha256Hex(new Uint8Array(bytes));
      const normalizedDigest = await sha256Hex(normalizedLineDigestInput(lines));
      if (generation !== this.sourceGeneration) return;
      this.sourceName.set(file.name);
      this.sourceLines.set(lines);
      this.rawSourceHash.set(rawHash);
      this.normalizedSourceDigest.set(normalizedDigest);
    } catch (error) {
      if (generation === this.sourceGeneration)
        this.sourceError.set(error instanceof Error ? error.message : 'The selected source could not be read or hashed.');
    } finally {
      if (generation === this.sourceGeneration) this.sourceBusy.set(false);
    }
  }

  validPackForm(page: PackPage): boolean {
    const fileReady = !!this.sourceName() && isHash(this.rawSourceHash()) && isHash(this.normalizedSourceDigest()) &&
      this.sourceLines().length > 0 && !!this.declaredSignedTotal();
    const selectedPrior = !this.form.priorPackId || this.priorPackResolved(page);
    return page.canSubmitPack && this.scopeCurrent(page) && fileReady && this.clientResolved(page) && this.engagementResolved(page) && selectedPrior &&
      validDate(this.form.periodStart) && validDate(this.form.periodEnd) && this.form.periodEnd >= this.form.periodStart &&
      !!this.form.framework.trim() && this.form.framework.trim().length <= 100 &&
      /^[A-Z]{3}$/.test(this.form.reportingCurrency) && this.sourceLines().every(x => x.currency === this.form.reportingCurrency) &&
      !!this.form.periodBasis.trim() && this.form.periodBasis.trim().length <= 100 &&
      !!this.form.taxonomyVersion.trim() && this.form.taxonomyVersion.trim().length <= 100 &&
      !!this.form.mappingVersion.trim() && this.form.mappingVersion.trim().length <= 100 &&
      !!this.form.sourceReference.trim() && this.form.sourceReference.trim().length <= 2000 &&
      this.form.compatibilityBridgeReference.trim().length <= 2000;
  }

  validPinValues(): boolean {
    return parseScaledAmount(this.pinOwnershipPercent()) === 100n * SCALE && this.pinControlMethod() === 'CONTROLLED';
  }

  hasCurrencyMismatch(): boolean {
    return !!this.form.reportingCurrency && this.sourceLines().some(line => line.currency !== this.form.reportingCurrency);
  }

  async submitPack(page: PackPage): Promise<void> {
    if (this.mutationBlocked() || !page.canSubmitPack || !this.scopeCurrent(page) || !this.prepareReviewed() || !this.validPackForm(page) || this.sourceBusy()) return;
    const body = {
      clientId: this.form.clientId, engagementId: this.form.engagementId,
      periodStart: this.form.periodStart, periodEnd: this.form.periodEnd,
      framework: this.form.framework.trim(), reportingCurrency: this.form.reportingCurrency,
      periodBasis: this.form.periodBasis.trim(), taxonomyVersion: this.form.taxonomyVersion.trim(),
      mappingVersion: this.form.mappingVersion.trim(), sourceReference: this.form.sourceReference.trim(),
      rawSourceHash: this.rawSourceHash(), normalizedSourceDigest: this.normalizedSourceDigest(),
      declaredSignedTotal: this.declaredSignedTotal(),
      lines: this.sourceLines().map(line => ({ ...line })),
      priorPackId: this.form.priorPackId || null,
      compatibilityBridgeReference: this.form.compatibilityBridgeReference.trim() || null,
      expectedGroupRevision: page.currentGroupRevision, reviewed: true,
    };
    const ok = await this.cmd().run<string>(
      `/api/ui/consolidation/scopes/${this.scopeId()}/external-packs`, body,
      'Immutable external component pack submitted.', id => {
        this.selectedPackId.set(id);
        this.linePage.set(1);
        this.workspace.reload();
        this.detail.reload();
      });
    if (!ok) this.refreshIfStale();
  }

  async reconcilePack(pack: PackDetail['pack']): Promise<void> {
    if (this.mutationBlocked()) return;
    const page = this.workspace.data();
    const detail = this.detail.data();
    if (!page || !detail || !this.detailCurrent(detail, page) || detail.pack.id !== pack.id || !page.canReviewPacks ||
        !pack.canReconcile || !this.reconcileReviewed() || !this.reconciliationEvidence().trim()) return;
    await this.reviewCommand(pack.id, 'reconcile', {
      expectedGroupRevision: page.currentGroupRevision, reviewed: true,
      evidenceReference: this.reconciliationEvidence().trim(),
    }, 'External component source reconciled.', () => {
      this.reconcileReviewed.set(false); this.reconciliationEvidence.set('');
    });
  }

  async returnPack(pack: PackDetail['pack']): Promise<void> {
    if (this.mutationBlocked()) return;
    const page = this.workspace.data();
    const detail = this.detail.data();
    if (!page || !detail || !this.detailCurrent(detail, page) || detail.pack.id !== pack.id || !page.canReviewPacks ||
        !pack.canReturn || !this.returnReviewed() || !this.returnReason().trim()) return;
    await this.reviewCommand(pack.id, 'return', {
      expectedGroupRevision: page.currentGroupRevision, reviewed: true, reason: this.returnReason().trim(),
    }, 'External component pack returned with retained lineage.', () => {
      this.returnReviewed.set(false); this.returnReason.set('');
    });
  }

  async approvePack(pack: PackDetail['pack']): Promise<void> {
    if (this.mutationBlocked()) return;
    const page = this.workspace.data();
    const detail = this.detail.data();
    if (!page || !detail || !this.detailCurrent(detail, page) || detail.pack.id !== pack.id || !page.canReviewPacks ||
        !pack.canApprove || !this.approveReviewed()) return;
    await this.reviewCommand(pack.id, 'approve', {
      expectedGroupRevision: page.currentGroupRevision, reviewed: true,
    }, 'External component pack independently approved.', () => this.approveReviewed.set(false));
  }

  async pinPack(pack: PackDetail['pack']): Promise<void> {
    if (this.mutationBlocked()) return;
    const page = this.workspace.data();
    const detail = this.detail.data();
    if (!page || !detail || !this.detailCurrent(detail, page) || detail.pack.id !== pack.id || !page.canPinApprovedPacks ||
        !pack.canPin || !this.pinReviewed() || !this.validPinValues()) return;
    const ok = await this.cmd().run<string>(
      `/api/ui/consolidation/scopes/${this.scopeId()}/external-components`, {
        externalComponentPackId: pack.id, ownershipPercent: this.pinOwnershipPercent().trim(),
        controlMethod: this.pinControlMethod(), expectedGroupRevision: page.currentGroupRevision, reviewed: true,
      }, 'External component submitted for separate component approval.', () => {
        this.pinReviewed.set(false);
        this.workspace.reload(); this.detail.reload(); this.componentPinned.emit();
      });
    if (!ok) this.refreshIfStale();
  }

  private async reviewCommand(
    packId: string, action: 'reconcile' | 'return' | 'approve', body: unknown, success: string, after: () => void,
  ): Promise<void> {
    const ok = await this.cmd().run<boolean>(
      `/api/ui/consolidation/scopes/${this.scopeId()}/external-packs/${packId}/${action}`, body, success, () => {
        after(); this.workspace.reload(); this.detail.reload();
      });
    if (!ok) this.refreshIfStale();
  }

  private refreshIfStale(): void {
    if (this.cmd().code() !== 'generation.stale') return;
    this.clearReviewAssent();
    this.workspace.reload();
    this.detail.reload();
  }

  private clearReviewAssent(): void {
    this.reconcileReviewed.set(false);
    this.returnReviewed.set(false);
    this.approveReviewed.set(false);
    this.pinReviewed.set(false);
    this.reconciliationEvidence.set('');
    this.returnReason.set('');
    this.pinOwnershipPercent.set('');
    this.pinControlMethod.set('');
  }

  private resetForScope(): void {
    this.page.set(1); this.clientPage.set(1); this.engagementPage.set(1); this.linePage.set(1);
    this.selectedClientId.set(''); this.selectedEngagementId.set('');
    this.clientSearch.set(''); this.engagementSearch.set('');
    this.clientSearchInput = ''; this.engagementSearchInput = '';
    this.selectedPackId.set(''); this.priorPackChoice.set(null);
    this.form = {
      clientId: '', engagementId: '', periodStart: '', periodEnd: '', framework: '', reportingCurrency: '',
      periodBasis: '', taxonomyVersion: '', mappingVersion: '', sourceReference: '', priorPackId: '',
      compatibilityBridgeReference: '',
    };
    this.sourceGeneration++;
    this.sourceBusy.set(false); this.sourceError.set(''); this.sourceName.set(''); this.sourceLines.set([]);
    this.rawSourceHash.set(''); this.normalizedSourceDigest.set(''); this.prepareReviewed.set(false);
    this.clearReviewAssent();
  }
}
