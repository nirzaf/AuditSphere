import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { portalWorkspace } from './contracts';
import { PortalDocuments } from './documents';
import { SessionService } from '../../core/session';

@Component({ selector: 'audit-client-portal', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, PortalDocuments, ...SHARED],
  template: `
    <audit-page-header title="Client portal" description="Your assigned requests and validated financial packages. Internal audit workpapers remain restricted." />
    <button matButton (click)="ws.reload()" [disabled]="busy() || ws.loading()">Refresh portal</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="your portal" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (ws.data(); as w) {
      @if (w.pendingOnboarding) { <section class="panel" role="status"><h2>Portal setup pending</h2>
        <p>Your workspace opens after commercial acceptance, Partner clearance, engagement activation and advance payment. Contact your audit team for the remaining action.</p></section> }
      @if (!w.firstSignIn.completed) {
        <section class="panel"><h2>Complete your first sign-in</h2><p>{{ w.firstSignIn.message }}</p>
          @if (w.firstSignIn.canComplete) {
            <form [formGroup]="terms" (ngSubmit)="complete()">
              <label><input type="checkbox" formControlName="acknowledged" /> I will keep my sign-in private, use multi-factor sign-in, and upload only requested files.</label>
              <button matButton="filled" [disabled]="terms.invalid || busy()">Complete first sign-in</button>
            </form>
          }
        </section>
      }
      <section class="panel" aria-labelledby="portal-requests"><h2 id="portal-requests">Requests</h2>
        @for (r of w.requests; track r.id) {
          <article class="portal-request"><h3><a [routerLink]="['/portal/requests', r.id]">{{ r.area }}</a></h3>
            <p>{{ r.objective }}</p><p>{{ r.periodStart }} to {{ r.periodEnd }} · due {{ r.dueDate }}</p>
            <audit-status [value]="r.state" /> @if (r.delegated) { <span>Delegated to you</span> }
          </article>
        } @empty { <p>No requests are assigned to this identity.</p> }
        <div class="actions"><button matButton (click)="previous()" [disabled]="page() === 0 || ws.loading()">Previous requests</button>
          <span>Page {{ page() + 1 }}</span><button matButton (click)="next()" [disabled]="!w.hasMoreRequests || ws.loading()">Next requests</button></div>
      </section>
      <audit-portal-documents />
      <section class="panel"><h2>Financial packages for management review</h2>
        @for (p of w.packages; track p.id) { <p><a [routerLink]="['/portal/accounting/packages', p.id]">{{ p.framework }} · {{ p.periodStart }} to {{ p.periodEnd }} · {{ p.currency }}</a></p> }
        @empty { <p>No validated packages are available for your current scope.</p> }
      </section>
    }
  `, styles: `.portal-request { padding: 1rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }`
})
export class ClientPortalHome {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly page = signal(0);
  readonly ws = this.api.resource(() => `/api/ui/portal?page=${this.page()}`, portalWorkspace, 'Portal unavailable. A current client assignment is required.');
  readonly terms = inject(FormBuilder).nonNullable.group({ acknowledged: [false, Validators.requiredTrue] });
  readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  constructor() { effect(() => { this.session.invalidation(); if (!this.session.current()) { this.terms.reset(); this.message.set(''); } }); }
  previous(): void { this.page.update(p => Math.max(0, p - 1)); }
  next(): void { this.page.update(p => Math.min(10000, p + 1)); }
  async complete(): Promise<void> {
    if (this.busy() || this.terms.invalid) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try {
      const r = await this.api.command('/api/ui/portal/first-sign-in', this.terms.getRawValue());
      if (generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); this.message.set(r.ok ? 'First sign-in complete. Uploads are open for your requests.' : r.message);
      this.terms.reset(); this.ws.reload();
    } finally { this.busy.set(false); }
  }
}
