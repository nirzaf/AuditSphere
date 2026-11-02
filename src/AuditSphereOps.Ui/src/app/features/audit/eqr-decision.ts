import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { guidPattern } from '../../core/contracts';
import { guid, nat, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';

const decodeReviewBasis = obj({ caseRevision: nat, factsDigest: text, reportDeliverableId: nullable(guid), reportSha256: nullable(text) });
type ReviewBasis = ReturnType<typeof decodeReviewBasis>;
const DECISIONS = ['IN_PROGRESS', 'CHANGES_REQUESTED', 'CONCURRED'] as const;
type Decision = typeof DECISIONS[number];

interface PendingDecision {
  version: 1;
  firmId: string;
  actorUserId: string;
  sessionGeneration: string;
  engagementId: string;
  requestKey: string;
  caseRevision: number;
  factsDigest: string;
  reportDeliverableId: string | null;
  reportSha256: string | null;
  status: Decision;
  findingsDiscussed: boolean;
  notesSalt: string;
  notesHash: string;
}

interface RecoveryReference {
  key: string;
  raw: string;
  pending: PendingDecision;
}

const activeRequests = new Set<string>();
const sha256 = /^[a-f0-9]{64}$/;

function validPending(raw: unknown, owner: { firmId: string; userId: string; engagementId: string }): raw is PendingDecision {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
  const p = raw as Record<string, unknown>;
  return p['version'] === 1 && p['firmId'] === owner.firmId && p['actorUserId'] === owner.userId &&
    typeof p['sessionGeneration'] === 'string' && /^[0-9]{1,19}$/.test(p['sessionGeneration']) && p['engagementId'] === owner.engagementId &&
    typeof p['requestKey'] === 'string' && guidPattern.test(p['requestKey']) &&
    typeof p['caseRevision'] === 'number' && Number.isSafeInteger(p['caseRevision']) && p['caseRevision'] >= 0 &&
    typeof p['factsDigest'] === 'string' && sha256.test(p['factsDigest']) &&
    (p['reportDeliverableId'] === null || typeof p['reportDeliverableId'] === 'string' && guidPattern.test(p['reportDeliverableId'])) &&
    (p['reportSha256'] === null || typeof p['reportSha256'] === 'string' && sha256.test(p['reportSha256'])) &&
    ((p['reportDeliverableId'] === null) === (p['reportSha256'] === null)) &&
    typeof p['status'] === 'string' && DECISIONS.includes(p['status'] as Decision) &&
    typeof p['findingsDiscussed'] === 'boolean' && typeof p['notesSalt'] === 'string' && /^[a-f0-9]{32}$/.test(p['notesSalt']) &&
    typeof p['notesHash'] === 'string' && sha256.test(p['notesHash']);
}

function basisIdentity(basis: ReviewBasis): string {
  return [basis.caseRevision, basis.factsDigest, basis.reportDeliverableId ?? '', basis.reportSha256 ?? ''].join(':');
}

async function notesDigest(salt: string, notes: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${salt}:${notes}`));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

@Component({
  selector: 'audit-eqr-decision',
  imports: [FormsModule, MatButtonModule],
  template: `
    <section class="panel" aria-labelledby="eqr-review-heading">
      <h2 id="eqr-review-heading">Independent EQR review</h2>
      <p>Review authority and case assignment are enforced by the server. This form records only an explicit decision for an already assigned case; it does not assign an EQR or establish applicability.</p>
      <button matButton="outlined" data-eqr-refresh [disabled]="basis.loading() || busy()" (click)="basis.reload()">Refresh EQR review basis</button>
      @if (basis.loading()) { <p role="status">Loading the exact EQR review basis…</p> }
      @if (basis.error()) { <p role="alert">{{ basis.error() }}</p> }
      @if (!basis.loading() && !basis.error() && !basis.data()) { <p role="status">No review basis is available. The server determines whether this account may review the assigned case.</p> }
      @if (error()) { <p role="alert">{{ error() }}</p> }
      @if (message()) { <p role="status">{{ message() }}</p> }
      @if (basis.data(); as b) {
        <dl class="facts" aria-label="Exact EQR review basis">
          <dt>Case revision</dt><dd>{{ b.caseRevision }}</dd>
          <dt>Current review facts SHA-256</dt><dd><code>{{ b.factsDigest }}</code></dd>
          <dt>Current unsigned report</dt><dd>@if (b.reportDeliverableId && b.reportSha256) {
            <a [href]="'/api/deliverables/' + b.reportDeliverableId + '/download'" target="_blank" rel="noopener">Download exact report {{ b.reportDeliverableId }}</a>
            · SHA-256 <code>{{ b.reportSha256 }}</code>
          } @else { No current unsigned report is recorded in this basis. }</dd>
        </dl>
        @if (!validBasis(b)) { <p role="alert">The review basis is incomplete or unsupported. Refresh it before recording a decision.</p> }
        @if (pending(); as p) {
          <p role="alert">A previous EQR decision outcome is unconfirmed. Re-enter the exact original notes to retry the same request. The original request remains bound to its original basis and will not be changed to match a newer basis.</p>
          @if (basisIdentityFor(b) !== pendingBasisIdentity(p)) {
            <p role="alert">The current basis differs from the original request. Only an exact replay of the original request can be retried here.</p>
          }
          <dl aria-label="Original pending EQR request">
            <dt>Original request</dt><dd>{{ p.status }} · case revision {{ p.caseRevision }} · facts <code>{{ p.factsDigest }}</code></dd>
            <dt>Original report</dt><dd>{{ p.reportDeliverableId ?? 'None recorded' }} · {{ p.reportSha256 ?? 'No report hash recorded' }}</dd>
          </dl>
          <label for="eqr-recovery-notes">Re-enter the exact original notes</label>
          <textarea id="eqr-recovery-notes" name="eqrRecoveryNotes" [(ngModel)]="notes" [disabled]="busy()" rows="3" maxlength="8000"></textarea>
          <button matButton="filled" data-eqr-retry [disabled]="busy() || pendingUnavailable() || !notes.trim() && p.status === 'CHANGES_REQUESTED' || !basis.data()" (click)="recordDecision()">
            {{ busy() ? 'Checking original request…' : 'Retry exact EQR request' }}
          </button>
          @if (recoveryReferences().length > 1) {
            <p role="status">Other unconfirmed requests remain saved. Resolve each original request before creating a new decision.</p>
            <button matButton="outlined" data-eqr-choose-another [disabled]="busy()" (click)="chooseAnotherRecovery()">Choose another saved request</button>
          }
        } @else if (recoveryReferences().length > 0) {
          <p role="status">{{ recoveryReferences().length }} saved EQR request(s) need individual recovery. Each original request remains unchanged; new decisions stay blocked until all are resolved.</p>
          <label for="eqr-recovery-choice">Select one original request</label>
          <select id="eqr-recovery-choice" data-eqr-recovery-choice [ngModel]="selectedRecoveryKey()"
            (ngModelChange)="selectedRecoveryKey.set($event)" [disabled]="busy()">
            <option value="">Choose a saved request</option>
            @for (reference of recoveryReferences(); track reference.key) {
              <option [value]="reference.key">{{ reference.pending.status }} · revision {{ reference.pending.caseRevision }} · report {{ reference.pending.reportDeliverableId ?? 'none' }} · request {{ reference.pending.requestKey }}</option>
            }
          </select>
          <button matButton="filled" data-eqr-select-recovery [disabled]="busy() || !selectedRecoveryKey()" (click)="selectRecovery()">Use selected request</button>
        } @else if (!pendingUnavailable()) {
          <label for="eqr-decision-status">Decision</label>
          <select id="eqr-decision-status" name="eqrDecisionStatus" [(ngModel)]="status" (ngModelChange)="invalidateAssent()" [disabled]="busy()">
            <option value="IN_PROGRESS">In progress</option>
            <option value="CHANGES_REQUESTED">Changes requested</option>
            <option value="CONCURRED" [disabled]="!b.reportDeliverableId || !b.reportSha256">Concurred</option>
          </select>
          <label for="eqr-decision-notes">Review notes</label>
          <textarea id="eqr-decision-notes" name="eqrDecisionNotes" [(ngModel)]="notes" (ngModelChange)="invalidateAssent()" [disabled]="busy()" rows="3" maxlength="8000"></textarea>
          <label><input type="checkbox" name="eqr-findings-discussed" [(ngModel)]="findingsDiscussed" (ngModelChange)="invalidateAssent()" [disabled]="busy()" /> Findings were discussed (required for concurrence).</label>
          <label><input #eqrAssent type="checkbox" name="eqr-exact-basis-reviewed" [checked]="reviewedExactBasis()" (change)="reviewedExactBasis.set(eqrAssent.checked); acceptBasis(b)" [disabled]="busy()" /> I reviewed this exact case revision, facts digest and current unsigned report, if present.</label>
          <button matButton="filled" data-eqr-submit [disabled]="!canSubmit(b)" (click)="recordDecision()">
            {{ busy() ? 'Recording…' : 'Record EQR decision' }}
          </button>
        }
        @if (pendingUnavailable()) { <p role="alert">The original EQR recovery reference is unavailable or invalid. No request will be sent until it can be recovered safely.</p> }
      }
    </section>`,
})
export class EqrDecision {
  readonly engagementId = input.required<string>();
  readonly caseStatus = input.required<string>();
  readonly decisionRecorded = output<{ engagementId: string; decisionId: string }>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly basis = this.api.resource(() => {
    const id = this.engagementId()?.toLowerCase();
    return id && guidPattern.test(id) && this.caseStatus() !== 'NOT_ASSIGNED'
      ? `/api/ui/engagements/${id}/completion/eqr-review-basis` : null;
  }, decodeReviewBasis, 'The EQR review basis is unavailable in this scope.',
  'The EQR review basis is unavailable in this scope. The server controls who may review this case.');
  readonly error = signal('');
  readonly message = signal('');
  readonly pending = signal<PendingDecision | null>(null);
  readonly recoveryReferences = signal<RecoveryReference[]>([]);
  readonly selectedRecoveryKey = signal('');
  readonly pendingUnavailable = signal(false);
  readonly reviewedExactBasis = signal(false);
  readonly findingsDiscussed = signal(false);
  readonly busy = signal(false);
  status: Decision = 'IN_PROGRESS';
  notes = '';
  private recoveryRaw: string | null = null;
  private recoveryKey: string | null = null;
  private ownerIdentity = '';
  private loadedOwner: string | null = null;
  private lastBasisIdentity: string | null = null;
  private reviewedIdentity: string | null = null;
  private operation = 0;
  private destroyed = false;

  constructor() {
    effect(() => {
      const id = this.engagementId().toLowerCase();
      const actor = this.session.current();
      const epoch = this.session.invalidation();
      this.caseStatus();
      untracked(() => this.resetContext(id, actor, epoch));
    });
    effect(() => {
      const value = this.basis.data();
      if (!value) return;
      const owner = this.currentOwnerIdentity();
      if (!owner) return;
      const identity = basisIdentity(value);
      if (this.loadedOwner === owner && this.lastBasisIdentity !== null && this.lastBasisIdentity !== identity) {
        this.reviewedExactBasis.set(false);
        this.reviewedIdentity = null;
        this.notes = '';
        if (!this.pending()) { this.status = 'IN_PROGRESS'; this.findingsDiscussed.set(false); }
        this.error.set('The EQR basis changed. Review the new exact basis before making a decision.');
      }
      this.loadedOwner = owner;
      this.lastBasisIdentity = identity;
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; ++this.operation; });
  }

  private currentOwnerIdentity(): string | null {
    const actor = this.session.current(), engagementId = this.engagementId().toLowerCase();
    return actor?.staff && guidPattern.test(engagementId)
      ? `${actor.firmId.toLowerCase()}:${actor.userId.toLowerCase()}:${actor.generation}:${this.session.invalidation()}:${engagementId}` : null;
  }

  private resetContext(engagementId: string, actor: ReturnType<SessionService['current']>, epoch: number): void {
    const nextOwner = actor?.staff && guidPattern.test(engagementId)
      ? `${actor.firmId.toLowerCase()}:${actor.userId.toLowerCase()}:${actor.generation}:${epoch}:${engagementId}` : '';
    if (nextOwner === this.ownerIdentity) return;
    this.ownerIdentity = nextOwner;
    this.loadedOwner = null;
    this.lastBasisIdentity = null;
    this.operation++;
    this.error.set(''); this.message.set(''); this.reviewedExactBasis.set(false);
    this.reviewedIdentity = null;
    this.findingsDiscussed.set(false); this.status = 'IN_PROGRESS'; this.notes = '';
    this.pending.set(null); this.pendingUnavailable.set(false); this.recoveryReferences.set([]); this.selectedRecoveryKey.set('');
    this.recoveryRaw = null; this.recoveryKey = null;
    this.busy.set(false);
    const current = actor?.staff && guidPattern.test(engagementId) ? {
      firmId: actor.firmId.toLowerCase(), userId: actor.userId.toLowerCase(), generation: actor.generation,
      engagementId: engagementId.toLowerCase(),
    } : null;
    if (!current) return;
    try {
      const references = this.findRecoveries(current);
      this.recoveryReferences.set(references);
      if (references.length === 1) this.activateRecovery(references[0]);
    } catch {
      this.pendingUnavailable.set(true);
      this.error.set('The pending EQR request could not be verified. No new request will be sent.');
    }
  }

  private storageKey(owner: { firmId: string; userId: string; engagementId: string }): string {
    // The reference outlives session replacement; authorization and UI state remain session-bound.
    return `auditsphere-eqr-pending-v2:${owner.firmId}:${owner.userId}:${owner.engagementId}`;
  }

  // Keep legacy references in place: moving or rewriting one could lose the only replay identity.
  private findRecoveries(owner: { firmId: string; userId: string; engagementId: string }): RecoveryReference[] {
    const stable = this.storageKey(owner);
    const legacyPrefix = `auditsphere-eqr-pending-v1:${owner.firmId}:${owner.userId}:`;
    const legacySuffix = `:${owner.engagementId}`;
    const keys: string[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key === null) throw new Error('Storage enumeration failed');
      if (key === stable || key.startsWith(legacyPrefix) && key.endsWith(legacySuffix)) keys.push(key);
    }
    // Also verify direct access, rather than relying on enumeration alone.
    const stableRaw = sessionStorage.getItem(stable);
    if ((stableRaw !== null) !== keys.includes(stable)) throw new Error('Storage enumeration inconsistent');
    return keys.map(key => {
      const raw = sessionStorage.getItem(key);
      if (raw === null) throw new Error('Pending request unavailable');
      const pending: unknown = JSON.parse(raw);
      if (!validPending(pending, owner) || key !== stable &&
          key !== `${legacyPrefix}${pending.sessionGeneration}${legacySuffix}`) throw new Error('Invalid pending request');
      return { key, raw, pending };
    });
  }

  private activateRecovery(reference: RecoveryReference): void {
    this.pending.set(reference.pending);
    this.status = reference.pending.status;
    this.findingsDiscussed.set(reference.pending.findingsDiscussed);
    this.notes = '';
    this.recoveryRaw = reference.raw;
    this.recoveryKey = reference.key;
  }

  selectRecovery(): void {
    const owner = this.captureOwner(), key = this.selectedRecoveryKey();
    if (!owner || !key || this.busy()) return;
    try {
      const observed = this.recoveryReferences().find(reference => reference.key === key);
      const current = this.findRecoveries(owner);
      const reference = current.find(candidate => candidate.key === key);
      if (!observed || !reference || observed.raw !== reference.raw) {
        this.pendingUnavailable.set(true);
        this.error.set('The selected EQR recovery reference changed or is unavailable. No request was sent.');
        return;
      }
      this.recoveryReferences.set(current);
      this.activateRecovery(reference);
      this.selectedRecoveryKey.set(key);
      this.error.set('');
    } catch {
      this.pendingUnavailable.set(true);
      this.error.set('The selected EQR recovery reference could not be verified. No request was sent.');
    }
  }

  chooseAnotherRecovery(): void {
    if (this.busy() || this.recoveryReferences().length < 2) return;
    this.pending.set(null);
    this.recoveryRaw = null;
    this.recoveryKey = null;
    this.notes = '';
    this.selectedRecoveryKey.set('');
  }

  validBasis(basis: ReviewBasis): boolean {
    return Number.isSafeInteger(basis.caseRevision) && basis.caseRevision >= 0 && sha256.test(basis.factsDigest) &&
      ((basis.reportDeliverableId === null) === (basis.reportSha256 === null)) &&
      (basis.reportDeliverableId === null || guidPattern.test(basis.reportDeliverableId) && sha256.test(basis.reportSha256 ?? ''));
  }

  pendingBasisIdentity(pending: PendingDecision): string {
    return [pending.caseRevision, pending.factsDigest, pending.reportDeliverableId ?? '', pending.reportSha256 ?? ''].join(':');
  }

  basisIdentityFor(basis: ReviewBasis): string { return basisIdentity(basis); }

  invalidateAssent(): void {
    this.reviewedExactBasis.set(false);
    this.reviewedIdentity = null;
  }

  acceptBasis(basis: ReviewBasis): void {
    if (!this.reviewedExactBasis()) { this.reviewedIdentity = null; return; }
    if (!this.validBasis(basis) || this.basis.data() !== basis || this.loadedOwner !== this.currentOwnerIdentity()) {
      this.reviewedExactBasis.set(false);
      this.reviewedIdentity = null;
    } else this.reviewedIdentity = basisIdentity(basis);
  }

  canSubmit(basis: ReviewBasis): boolean {
    return !this.busy() && !this.pending() && this.recoveryReferences().length === 0 && !this.pendingUnavailable() && !this.basis.loading() && !this.basis.error() &&
      this.validBasis(basis) && this.basis.data() === basis && this.loadedOwner === this.currentOwnerIdentity() &&
      this.reviewedExactBasis() && this.reviewedIdentity === basisIdentity(basis) &&
      (this.status !== 'CONCURRED' || this.findingsDiscussed() && !!basis.reportDeliverableId && !!basis.reportSha256) &&
      (this.status !== 'CHANGES_REQUESTED' || !!this.notes.trim()) && this.notes.trim().length <= 8000;
  }

  async recordDecision(): Promise<void> {
    const basis = this.basis.data(), owner = this.captureOwner();
    const recovering = this.pending();
    if (!basis || !owner || this.busy() || this.pendingUnavailable() || !this.validBasis(basis) ||
        this.loadedOwner !== this.currentOwnerIdentity() || this.basis.loading() || this.basis.error()) return;
    const draftNotes = this.notes, notes = draftNotes.trim();
    const draftStatus = this.status, draftFindings = this.findingsDiscussed();
    if (recovering) {
      if (this.recoveryKey === null || this.recoveryRaw === null) return;
      if (notes.length > 8000 || (recovering.status === 'CHANGES_REQUESTED' && !notes)) {
        this.error.set('Enter the exact original EQR notes before retrying.'); return;
      }
    } else if (!this.canSubmit(basis)) return;

    const operation = ++this.operation;
    this.busy.set(true); this.error.set(''); this.message.set('');
    const storageKey = recovering ? this.recoveryKey! : this.storageKey(owner);
    const claimKey = this.storageKey(owner);
    if (activeRequests.has(claimKey)) {
      this.error.set('The original EQR request is still in flight. Wait before retrying.');
      this.busy.set(false);
      return;
    }
    let pending = recovering;
    let raw = this.recoveryRaw;
    let dispatchStarted = false;
    activeRequests.add(claimKey);
    try {
      if (pending) {
        const digest = await notesDigest(pending.notesSalt, notes);
        if (!this.isCurrent(operation, owner)) return;
        if (this.basis.data() !== basis || this.basis.loading() || this.basis.error() || this.loadedOwner !== this.currentOwnerIdentity()) {
          this.error.set('The review basis changed while checking the original request. Refresh before retrying.');
          return;
        }
        if (digest !== pending.notesHash || this.notes !== draftNotes) {
          this.error.set('The notes differ from the original EQR request. Re-enter the exact original notes.');
          this.busy.set(false);
          return;
        }
        const reference = this.findRecoveries(owner).find(candidate => candidate.key === storageKey);
        if (raw === null || !reference || reference.raw !== raw) {
          this.pendingUnavailable.set(true);
          this.error.set('The original EQR recovery reference changed or is unavailable. No request was sent.');
          this.busy.set(false);
          return;
        }
      } else {
        const salt = Array.from(crypto.getRandomValues(new Uint8Array(16)), byte => byte.toString(16).padStart(2, '0')).join('');
        pending = { version: 1, firmId: owner.firmId, actorUserId: owner.userId, sessionGeneration: owner.generation,
          engagementId: owner.engagementId, requestKey: crypto.randomUUID(), caseRevision: basis.caseRevision,
          factsDigest: basis.factsDigest, reportDeliverableId: basis.reportDeliverableId, reportSha256: basis.reportSha256,
          status: draftStatus, findingsDiscussed: draftFindings, notesSalt: salt, notesHash: await notesDigest(salt, notes) };
        if (!this.isCurrent(operation, owner) || this.basis.data() !== basis || this.basis.loading() || this.basis.error() ||
            this.loadedOwner !== this.currentOwnerIdentity() || !this.reviewedExactBasis() || this.reviewedIdentity !== basisIdentity(basis) ||
            this.status !== draftStatus || this.findingsDiscussed() !== draftFindings || this.notes !== draftNotes) {
          if (this.isCurrent(operation, owner)) this.busy.set(false);
          return;
        }
        raw = JSON.stringify(pending);
        const existingReferences = this.findRecoveries(owner);
        if (existingReferences.length > 0) {
          this.recoveryReferences.set(existingReferences);
          this.selectedRecoveryKey.set('');
          this.error.set('Saved EQR requests must be resolved individually before creating another request.');
          this.busy.set(false);
          return;
        }
        this.pending.set(pending); this.recoveryRaw = raw; this.recoveryKey = storageKey;
        sessionStorage.setItem(storageKey, raw);
        if (sessionStorage.getItem(storageKey) !== raw) {
          this.pendingUnavailable.set(true);
          throw new Error('Recovery reference could not be confirmed');
        }
      }
      if (!pending || !raw || !this.isCurrent(operation, owner) || this.recoveryKey !== storageKey) {
        if (this.isCurrent(operation, owner)) this.busy.set(false);
        return;
      }
      dispatchStarted = true;
      const outcome = await this.api.command<unknown>(`/api/ui/engagements/${owner.engagementId}/completion/eqr-decision`, {
        status: pending.status, notes, findingsDiscussed: pending.findingsDiscussed, requestKey: pending.requestKey,
        expectedCaseRevision: pending.caseRevision, expectedFactsDigest: pending.factsDigest,
        expectedReportDeliverableId: pending.reportDeliverableId, expectedReportSha256: pending.reportSha256,
      });
      if (!this.isCurrent(operation, owner)) {
        // A definitive refusal of the first transmission can clear only its exact saved reference,
        // even after navigation. An unknown outcome or replay refusal never proves non-commit.
        if (!recovering && !outcome.ok && !outcome.unknown && outcome.status !== 408 && outcome.code !== 'idempotency.conflict') {
          if (raw !== null && this.removeExactRecoveryReference(owner, storageKey, raw)) {
            const currentOwner = this.captureOwner();
            if (currentOwner?.firmId === owner.firmId && currentOwner.userId === owner.userId &&
                currentOwner.engagementId === owner.engagementId) {
              try {
                this.recoveryReferences.set(this.findRecoveries(currentOwner));
                if (this.recoveryKey === storageKey && this.recoveryRaw === raw) {
                  this.pending.set(null);
                  this.recoveryKey = null;
                  this.recoveryRaw = null;
                  this.selectedRecoveryKey.set('');
                }
                this.error.set('The earlier EQR request was refused. Review the current basis before making another decision.');
              } catch {
                this.pendingUnavailable.set(true);
                this.error.set('The earlier EQR request was refused, but remaining recovery references could not be verified. No new request will be sent.');
              }
            }
          }
        }
        return;
      }
      if (outcome.ok) {
        const decisionId = outcome.value;
        if (typeof decisionId !== 'string' || !guidPattern.test(decisionId)) {
          this.error.set('The server response could not confirm the EQR decision identity. The original request remains unconfirmed.');
          return;
        }
        if (!this.clearExactRecovery(owner, storageKey, raw)) return;
        this.pending.set(null); this.recoveryRaw = null; this.recoveryKey = null;
        this.notes = ''; this.reviewedExactBasis.set(false); this.reviewedIdentity = null;
        this.findingsDiscussed.set(false); this.status = 'IN_PROGRESS';
        this.message.set(`The server confirmed EQR decision ${decisionId}. Refreshing the current review basis; current concurrence is not assumed.`);
        this.basis.reload();
        this.decisionRecorded.emit({ engagementId: owner.engagementId, decisionId });
        return;
      }
      if (outcome.unknown || outcome.status === 408) {
        this.error.set('The EQR outcome is unconfirmed. Re-enter the exact original notes and retry the same request when ready.');
        return;
      }
      // A refusal of an exact replay does not prove the earlier transmission did not commit.
      if (!recovering && outcome.code !== 'idempotency.conflict') {
        if (!this.clearExactRecovery(owner, storageKey, raw)) return;
        this.pending.set(null); this.recoveryRaw = null; this.recoveryKey = null;
        this.reviewedExactBasis.set(false); this.reviewedIdentity = null;
      }
      this.error.set(outcome.message);
    } catch {
      if (this.isCurrent(operation, owner)) {
        if (!dispatchStarted) this.pendingUnavailable.set(true);
        this.error.set(dispatchStarted
          ? 'The EQR outcome is unconfirmed. Re-enter the exact original notes and retry the same request when ready.'
          : 'The exact EQR recovery reference could not be saved or checked. No request was sent.');
      }
    } finally {
      activeRequests.delete(claimKey);
      if (this.isCurrent(operation, owner)) this.busy.set(false);
    }
  }

  private captureOwner(): { firmId: string; userId: string; generation: string; epoch: number; engagementId: string } | null {
    const actor = this.session.current(), engagementId = this.engagementId().toLowerCase();
    return actor?.staff && guidPattern.test(engagementId)
      ? { firmId: actor.firmId.toLowerCase(), userId: actor.userId.toLowerCase(), generation: actor.generation,
        epoch: this.session.invalidation(), engagementId } : null;
  }

  private isCurrent(operation: number, owner: { firmId: string; userId: string; generation: string; epoch: number; engagementId: string }): boolean {
    const current = this.captureOwner();
    return !this.destroyed && operation === this.operation && current?.firmId === owner.firmId && current.userId === owner.userId &&
      current.generation === owner.generation && current.epoch === owner.epoch && current.engagementId === owner.engagementId;
  }

  private clearExactRecovery(owner: { firmId: string; userId: string; engagementId: string }, key: string, raw: string): boolean {
    try {
      if (!this.removeExactRecoveryReference(owner, key, raw)) {
        this.error.set('The recovery reference changed. It was preserved for reconciliation.');
        this.pendingUnavailable.set(true);
        return false;
      }
      this.recoveryReferences.set(this.findRecoveries(owner));
      this.selectedRecoveryKey.set('');
      return true;
    } catch {
      this.error.set('The EQR request was confirmed or refused, but its recovery reference could not be updated safely.');
      this.pendingUnavailable.set(true);
      return false;
    }
  }

  private removeExactRecoveryReference(owner: { firmId: string; userId: string; engagementId: string }, key: string, raw: string): boolean {
    const reference = this.findRecoveries(owner).find(candidate => candidate.key === key);
    if (!reference || reference.raw !== raw) return false;
    sessionStorage.removeItem(key);
    return sessionStorage.getItem(key) === null;
  }
}
