import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeOffboardingReadiness, OffboardingReadiness, offboardingStateLabel } from './client-contracts';

/** On-demand, read-only closeout assessment; it never changes the client, work, money or access. */
@Component({
  selector: 'audit-client-offboarding-readiness',
  imports: [RouterLink, FormsModule, MatButtonModule],
  template: `
    <section aria-labelledby="client-offboarding-heading">
      <h2 id="client-offboarding-heading">Offboarding readiness</h2>
      <p class="muted">A read-only check of open work, money, access and retention. Each part is shown only if you hold that area's access; otherwise it is unknown, not clear. It changes nothing.</p>
      <button matButton="outlined" data-offboarding-check [disabled]="loading()" (click)="load()">{{ loading() ? 'Checking…' : result() ? 'Refresh readiness' : 'Check offboarding readiness' }}</button>
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
            <label for="offboarding-disposition-notes">Review notes:</label>
            <textarea id="offboarding-disposition-notes" name="offboardingReviewNotes" [(ngModel)]="dispositionNotes" rows="3" maxlength="2000" placeholder="Enter notes about this blocked review"></textarea>
            <button matButton="filled" data-offboarding-closeout [disabled]="closeoutLoading() || !dispositionNotes.trim()" (click)="recordCloseout(r)">
              {{ closeoutLoading() ? 'Recording…' : 'Record blocked review' }}
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
  dispositionNotes = '';
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private closeoutRequest?: Subscription;
  private revision = 0;
  private closeoutIntent: { clientId: string; readinessDigest: string; archiveRevision: string; notes: string; requestKey: string } | null = null;

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
    this.result.set(null); this.error.set(''); this.loading.set(false);
    this.closeoutLoading.set(false); this.closeoutSuccess.set(false);
    this.dispositionNotes = '';
    this.closeoutIntent = null;
  }

  load(): void {
    const id = this.clientId()?.toLowerCase();
    if (!id || !guidPattern.test(id) || this.loading() || !this.session.current()?.staff) return;
    const revision = ++this.revision;
    this.loading.set(true); this.error.set('');
    this.request?.unsubscribe();
    this.request = this.http.get<unknown>(`/api/ui/clients/${id}/offboarding-readiness`).pipe(timeout(30000)).subscribe({
      next: (value) => {
        if (revision !== this.revision) return;
        this.loading.set(false);
        try {
          const decoded = decodeOffboardingReadiness(value);
          if (decoded.clientId.toLowerCase() !== id) throw new Error('Unexpected client');
          this.result.set(decoded);
        } catch { this.result.set(null); this.error.set('The server returned an unsupported readiness response. Refresh to try again.'); }
      },
      error: () => {
        if (revision !== this.revision) return;
        this.loading.set(false); this.result.set(null);
        this.error.set('Offboarding readiness is unavailable for your current access. Nothing was changed.');
      },
    });
  }

  recordCloseout(r: OffboardingReadiness): void {
    const id = this.clientId()?.toLowerCase();
    const notes = this.dispositionNotes.trim();
    if (!id || !guidPattern.test(id) || r.clientId.toLowerCase() !== id || this.closeoutLoading() || !notes) return;
    let intent = this.closeoutIntent;
    if (!intent || intent.clientId !== id || intent.readinessDigest !== r.readinessDigest ||
        intent.archiveRevision !== r.archiveRevision || intent.notes !== notes) {
      intent = { clientId: id, readinessDigest: r.readinessDigest, archiveRevision: r.archiveRevision, notes, requestKey: crypto.randomUUID() };
      this.closeoutIntent = intent;
    }
    const requestKey = intent.requestKey;
    const revision = this.revision;
    this.closeoutLoading.set(true);
    this.error.set('');
    this.closeoutRequest?.unsubscribe();
    this.closeoutRequest = this.http.post<unknown>(`/api/ui/clients/${id}/offboarding-closeout`, {
      dispositionNotes: notes,
      acknowledgedActionItems: [],
      expectedRevision: r.archiveRevision,
      archiveClientOnCloseout: false,
      expectedReadinessDigest: r.readinessDigest,
      requestKey
    }).pipe(timeout(30000)).subscribe({
      next: (res) => {
        if (revision !== this.revision) return;
        this.closeoutLoading.set(false);
        const receipt = res as {
          recordId?: unknown; clientId?: unknown; status?: unknown; readinessDigest?: unknown;
          requestKey?: unknown; archiveRevision?: unknown; isArchived?: unknown;
          readinessOverallState?: unknown; dispositionNotes?: unknown; acknowledgedActionItems?: unknown;
          decidedByUserId?: unknown;
        } | null;
        const expectedActor = this.session.current()?.userId.toLowerCase();
        if (receipt && typeof receipt.recordId === 'string' && guidPattern.test(receipt.recordId.toLowerCase()) &&
            typeof receipt.clientId === 'string' && receipt.clientId.toLowerCase() === id &&
            receipt.status === 'BLOCKED_DECISION' && receipt.readinessDigest === r.readinessDigest &&
            receipt.requestKey === requestKey && receipt.archiveRevision === r.archiveRevision &&
            receipt.isArchived === r.isArchived && receipt.readinessOverallState === r.overallState &&
            receipt.dispositionNotes === notes && Array.isArray(receipt.acknowledgedActionItems) && receipt.acknowledgedActionItems.length === 0 &&
            typeof receipt.decidedByUserId === 'string' && receipt.decidedByUserId.toLowerCase() === expectedActor) {
          this.closeoutSuccess.set(true);
        } else {
          this.error.set('The server returned an invalid blocked-review receipt. Refresh to verify the saved record before retrying.');
        }
      },
      error: (err) => {
        if (revision !== this.revision) return;
        this.closeoutLoading.set(false);
        this.error.set(err?.error?.message || 'Failed to record offboarding closeout.');
      }
    });
  }
}
