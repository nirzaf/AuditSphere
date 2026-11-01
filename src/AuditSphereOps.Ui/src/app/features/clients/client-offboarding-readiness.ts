import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { finalize, Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeOffboardingReadiness, OffboardingReadiness, offboardingStateLabel } from './client-contracts';
import { offboardingNotesHash, pendingOffboarding, type PendingOffboarding } from './offboarding-pending';

const activeOffboardingRequests = new Set<string>();

/** On-demand, read-only closeout assessment; it never changes the client, work, money or access. */
@Component({
  selector: 'audit-client-offboarding-readiness',
  imports: [RouterLink, FormsModule, MatButtonModule],
  template: `
    <section aria-labelledby="client-offboarding-heading">
      <h2 id="client-offboarding-heading">Offboarding readiness</h2>
      <p class="muted">A read-only check of open work, money, access and retention. Each part is shown only if you hold that area's access; otherwise it is unknown, not clear. It changes nothing.</p>
      <button matButton="outlined" data-offboarding-check [disabled]="loading() || closeoutLoading()" (click)="load()">{{ loading() ? 'Checking…' : result() ? 'Refresh readiness' : 'Check offboarding readiness' }}</button>
      @if (error()) { <p role="alert">{{ error() }}</p> }
      @if (result(); as r) {
        <p role="status">Overall: <strong>{{ label(r.overallState) }}</strong>. Assessed {{ r.assessedAt }}.</p>
        @for (s of r.sections; track s.key) {
          <section [attr.aria-label]="s.title">
            <h3>{{ s.title }} — {{ label(s.state) }}</h3>
            <ul>
              @for (i of s.items; track i.key) {
                <li>{{ i.label }}: <strong>{{ i.count === null ? 'Unknown' : i.count }}</strong> ({{ label(i.state) }})@if (i.detail) { — {{ i.detail }} }</li>
              }
            </ul>
            @if (s.ownerRoute) { <a [routerLink]="s.ownerRoute">Open the owning workspace</a> }
          </section>
        }
        <ul>@for (n of r.coverageNotes; track n) { <li class="muted">{{ n }}</li> }</ul>

        <section aria-labelledby="offboarding-review-heading">
          <h3 id="offboarding-review-heading">Record a blocked review</h3>
          @if (closeoutSuccess()) {
            <p role="status">A BLOCKED_DECISION review record was saved. It did not approve offboarding or change the client's archive, work, money, access or retention state.</p>
          } @else {
            <p class="muted">This immutable snapshot records what was visible at review time. It does not approve or complete offboarding. UNKNOWN and action-required items remain unresolved; client archiving is a separate action.</p>
            @if (pending()) { <p role="alert">A previous review outcome is unconfirmed. Re-enter its exact original notes to retry the same request.</p> }
            <label for="offboarding-disposition-notes">Review notes:</label>
            <textarea id="offboarding-disposition-notes" name="offboardingReviewNotes" [(ngModel)]="dispositionNotes" rows="3" maxlength="2000" placeholder="Enter notes about this blocked review"></textarea>
            <button matButton="filled" data-offboarding-closeout [disabled]="closeoutLoading() || !dispositionNotes.trim()" (click)="recordCloseout(r)">
              {{ closeoutLoading() ? 'Recording…' : pending() ? 'Retry original review' : 'Record blocked review' }}
            </button>
          }
        </section>
      }
    </section>`,
})
export class ClientOffboardingReadiness {
  readonly clientId = input.required<string>();
  readonly result = signal<OffboardingReadiness | null>(null);
  readonly loading = signal(false);
  readonly closeoutLoading = signal(false);
  readonly closeoutSuccess = signal(false);
  readonly error = signal('');
  readonly label = offboardingStateLabel;
  readonly pending = signal<PendingOffboarding | null>(null);
  private pendingUnavailable = false;
  dispositionNotes = '';
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private closeoutRequest?: Subscription;
  private revision = 0;
  private readinessContext: string | null = null;
  private currentContext(): string {
    const session = this.session.current();
    return JSON.stringify([this.clientId()?.toLowerCase(), session?.firmId, session?.userId,
      session?.generation, session?.staff, this.session.invalidation()]);
  }
  private storageKey(id: string): string | null {
    const session = this.session.current();
    return session?.staff ? 'auditsphere-offboarding-pending-v1:' + session.firmId.toLowerCase() + ':' + session.userId.toLowerCase() + ':' + id : null;
  }

  private readPending(id: string): void {
    this.pending.set(null); this.pendingUnavailable = false;
    try {
      const key = this.storageKey(id);
      if (!key) throw new Error('Session unavailable');
      const raw = sessionStorage.getItem(key);
      if (raw !== null) {
        const value = pendingOffboarding(JSON.parse(raw), id);
        if (!value) throw new Error('Invalid recovery reference');
        this.pending.set(value);
      }
    } catch {
      this.pendingUnavailable = true;
      this.error.set('The pending-review reference is unavailable. Recording is blocked until it can be recovered.');
    }
  }

  constructor() {
    // A route or session change discards retained evidence and any in-flight response.
    effect(() => {
      this.clientId(); this.session.invalidation(); this.session.current();
      untracked(() => this.reset());
    });
    inject(DestroyRef).onDestroy(() => {
      this.revision++;
      this.request?.unsubscribe();
      this.closeoutRequest?.unsubscribe();
    });
  }

  private reset(): void {
    this.revision++;
    this.request?.unsubscribe();
    this.closeoutRequest?.unsubscribe();
    this.readinessContext = null;
    this.result.set(null); this.error.set(''); this.loading.set(false);
    this.closeoutLoading.set(false); this.closeoutSuccess.set(false);
    this.dispositionNotes = '';
    this.pending.set(null); this.pendingUnavailable = false;
  }

  load(): void {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || this.loading() || this.closeoutLoading() || !this.session.current()?.staff) return;
    const revision = ++this.revision, context = this.currentContext();
    this.loading.set(true); this.error.set('');
    this.request?.unsubscribe();
    this.request = this.http.get<unknown>(`/api/ui/clients/${id}/offboarding-readiness`).pipe(timeout(30000)).subscribe({
      next: (value) => {
        if (revision !== this.revision || context !== this.currentContext()) return;
        this.loading.set(false);
        try {
          const decoded = decodeOffboardingReadiness(value);
          if (decoded.clientId.toLowerCase() !== id) throw new Error('Unexpected client');
          this.readinessContext = context;
          this.result.set(decoded);
          this.readPending(id);
        } catch { this.result.set(null); this.error.set('The server returned an unsupported readiness response. Refresh to try again.'); }
      },
      error: () => {
        if (revision !== this.revision || context !== this.currentContext()) return;
        this.loading.set(false); this.result.set(null);
        this.error.set('Offboarding readiness is unavailable for your current access. Nothing was changed.');
      },
    });
  }

  async recordCloseout(r: OffboardingReadiness): Promise<void> {
    const id = this.clientId()?.toLowerCase();
    const notes = this.dispositionNotes.trim(), context = this.currentContext();
    if (!id || !guidPattern.test(id) || r !== this.result() || r.clientId.toLowerCase() !== id ||
        this.readinessContext !== context || this.closeoutLoading() || this.pendingUnavailable || !notes || notes.length > 2000) return;
    const key = this.storageKey(id);
    if (!key) return;
    const revision = this.revision;
    const recovering = this.pending() !== null;
    this.closeoutLoading.set(true); this.error.set('');
    let intent = this.pending();
    let claimed = false;
    let willDispatch = false;
    try {
      if (!intent) {
        const salt = Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, '0')).join('');
        intent = { clientId: id, readinessDigest: r.readinessDigest, archiveRevision: r.archiveRevision,
          requestKey: crypto.randomUUID(), notesHash: await offboardingNotesHash(salt, notes), salt,
          isArchived: r.isArchived, overallState: r.overallState };
      } else if (await offboardingNotesHash(intent.salt, notes) !== intent.notesHash) {
        if (revision === this.revision && context === this.currentContext()) this.error.set('The notes differ from the original review. Re-enter the original notes.');
        return;
      }
      if (revision !== this.revision || context !== this.currentContext() || key !== this.storageKey(id) || r !== this.result()) return;
      if (activeOffboardingRequests.has(key)) {
        this.error.set('The original review request is still in flight. Wait before retrying.');
        return;
      }
      activeOffboardingRequests.add(key); claimed = true;
      const raw = JSON.stringify(intent);
      const existing = sessionStorage.getItem(key);
      if ((!recovering && existing !== null) || (recovering && existing !== raw)) {
        this.pendingUnavailable = true;
        this.error.set('The recovery reference changed. Reload this client and resolve the original request.');
        return;
      }
      sessionStorage.setItem(key, raw);
      if (sessionStorage.getItem(key) !== raw) throw new Error('Recovery reference unavailable');
      this.pending.set(intent);
      willDispatch = true;
    } catch {
      if (revision === this.revision && context === this.currentContext()) this.error.set('The recovery reference could not be saved. No review was sent.');
      return;
    } finally {
      if (claimed && !willDispatch) activeOffboardingRequests.delete(key);
      if (revision === this.revision && context === this.currentContext()) this.closeoutLoading.set(false);
    }
    if (!intent || revision !== this.revision || context !== this.currentContext()) { if (claimed) activeOffboardingRequests.delete(key); return; }
    const original = intent;
    this.closeoutLoading.set(true);
    this.closeoutRequest?.unsubscribe();
    this.closeoutRequest = this.http.post<unknown>('/api/ui/clients/' + id + '/offboarding-closeout', {
      dispositionNotes: notes, acknowledgedActionItems: [], expectedRevision: original.archiveRevision,
      archiveClientOnCloseout: false, expectedReadinessDigest: original.readinessDigest, requestKey: original.requestKey
    }).pipe(timeout(30000), finalize(() => activeOffboardingRequests.delete(key))).subscribe({
      next: (res) => {
        if (revision !== this.revision || context !== this.currentContext() || key !== this.storageKey(id)) return;
        this.closeoutLoading.set(false);
        const receipt = res as {
          recordId?: unknown; clientId?: unknown; status?: unknown; readinessDigest?: unknown;
          requestKey?: unknown; archiveRevision?: unknown; isArchived?: unknown;
          readinessOverallState?: unknown; dispositionNotes?: unknown; acknowledgedActionItems?: unknown; decidedByUserId?: unknown;
        } | null;
        const expectedActor = this.session.current()?.userId.toLowerCase();
        if (receipt && typeof receipt.recordId === 'string' && guidPattern.test(receipt.recordId.toLowerCase()) &&
            typeof receipt.clientId === 'string' && receipt.clientId.toLowerCase() === id &&
            receipt.status === 'BLOCKED_DECISION' && receipt.readinessDigest === original.readinessDigest &&
            receipt.requestKey === original.requestKey && receipt.archiveRevision === original.archiveRevision &&
            receipt.isArchived === original.isArchived && receipt.readinessOverallState === original.overallState &&
            receipt.dispositionNotes === notes && Array.isArray(receipt.acknowledgedActionItems) && receipt.acknowledgedActionItems.length === 0 &&
            typeof receipt.decidedByUserId === 'string' && receipt.decidedByUserId.toLowerCase() === expectedActor) {
          try {
            if (sessionStorage.getItem(key) !== JSON.stringify(original)) throw new Error('Recovery reference changed');
            sessionStorage.removeItem(key);
          } catch { this.error.set('The receipt was confirmed but its recovery reference could not be cleared.'); return; }
          this.pending.set(null); this.closeoutSuccess.set(true); this.dispositionNotes = '';
        } else {
          this.error.set('The server returned an invalid blocked-review receipt. The original outcome remains unconfirmed.');
        }
      },
      error: (err: unknown) => {
        const rejected = !recovering && err instanceof HttpErrorResponse && [400, 409].includes(err.status) &&
          ['crm.invalid', 'gate.blocked', 'generation.stale'].includes(err.error?.code);
        let cleared = false;
        if (rejected) {
          // A received definitive first-attempt rejection resolves only its exact stored
          // reference, even if the view changed. Cancellation or a retry rejection does
          // not prove the original outcome. Never project an old response into a new view.
          try {
            if (sessionStorage.getItem(key) !== JSON.stringify(original)) throw new Error('Recovery reference changed');
            sessionStorage.removeItem(key); cleared = true;
          } catch { /* Retain a changed or unavailable reference. */ }
        }
        if (revision !== this.revision || context !== this.currentContext() || key !== this.storageKey(id)) return;
        this.closeoutLoading.set(false);
        if (rejected) {
          if (cleared) this.pending.set(null);
          else this.pendingUnavailable = true;
          this.error.set(cleared ? 'The review was not accepted. Refresh and review the current readiness.'
            : 'The review was not accepted, but its recovery reference could not be cleared. Reload this client before continuing.');
        } else {
          this.error.set('The review outcome is unconfirmed. Retry the exact original request before recording another review.');
        }
      }
    });
  }
}
