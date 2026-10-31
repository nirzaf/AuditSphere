import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeOffboardingReadiness, OffboardingReadiness, offboardingStateLabel } from './client-contracts';

/** On-demand, read-only closeout assessment; it never changes the client, work, money or access. */
@Component({
  selector: 'audit-client-offboarding-readiness',
  imports: [RouterLink, MatButtonModule],
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
      }
    </section>`,
})
export class ClientOffboardingReadiness {
  readonly clientId = input.required<string>();
  readonly result = signal<OffboardingReadiness | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly label = offboardingStateLabel;
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private request?: Subscription;
  private revision = 0;

  constructor() {
    // A route or session change discards retained evidence and any in-flight response.
    effect(() => {
      this.clientId(); this.session.invalidation(); this.session.current();
      untracked(() => this.reset());
    });
    inject(DestroyRef).onDestroy(() => { this.revision++; this.request?.unsubscribe(); });
  }

  private reset(): void {
    this.revision++; this.request?.unsubscribe();
    this.result.set(null); this.error.set(''); this.loading.set(false);
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
}
