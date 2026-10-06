import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { clientRequestStatusLabel, portalWorkspace } from './contracts';
import { PortalJournals } from './journals';
import { PortalDocuments } from './documents';
import { PortalFinance } from './finance';
import { SessionService } from '../../core/session';

@Component({ selector: 'audit-client-portal', imports: [ReactiveFormsModule, RouterLink, MatButtonModule, PortalDocuments, PortalJournals, PortalFinance, ...SHARED],
  template: `
    <audit-page-header title="Client portal" description="Your assigned requests and validated financial packages. Internal audit workpapers remain restricted." />
    <button matButton (click)="ws.reload()" [disabled]="busy() || ws.loading()">Refresh portal</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="your portal" />
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if (ws.data(); as w) {
      @if (w.pendingOnboarding && !w.engagementCount) {
        <section class="panel" role="status"><h2>Portal setup pending</h2>
          <p>Your workspace opens after commercial acceptance, Partner clearance, engagement activation and advance payment. Contact your audit team for the remaining action.</p></section>
      } @else {
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
              <audit-status [value]="statusLabel(r.state)" /> @if (r.delegated) { <span>Delegated to you</span> }
            </article>
          } @empty { <p>No requests are assigned to this identity.</p> }
          <nav class="actions" aria-label="Request pages">
            <button matButton (click)="previousRequestPage()" [disabled]="requestPage() === 0 || ws.loading()">Previous requests</button>
            <span aria-live="polite">Page {{ requestPage() + 1 }} · {{ w.requests.length }} shown</span>
            <button matButton (click)="nextRequestPage()" [disabled]="!w.hasMoreRequests || ws.loading()">Next requests</button>
            <label for="portal-request-page-size">Requests per page
              <select id="portal-request-page-size" [value]="requestPageSize()" (change)="setRequestPageSize($event)">
                @for (size of pageSizes; track size) { <option [value]="size">{{ size }}</option> }
              </select>
            </label>
          </nav>
        </section>
        <audit-portal-documents />
        <audit-portal-journals />
        <audit-portal-finance />
        <section class="panel"><h2>Financial packages for management review</h2>
          @for (p of packageRows(w.packages); track p.id) { <p><a [routerLink]="['/portal/accounting/packages', p.id]">{{ p.framework }} · {{ p.periodStart }} to {{ p.periodEnd }} · {{ p.currency }}</a></p> }
          @empty { <p>No validated packages are available for your current scope.</p> }
          @if (w.packages.length > 10) {
            <nav class="actions" aria-label="Financial package pages">
              <button matButton (click)="previousPackagePage()" [disabled]="packagePage() === 0">Previous packages</button>
              <span aria-live="polite">Page {{ packagePage() + 1 }} of {{ packagePageCount(w.packages.length) }} · {{ packageRangeStart(w.packages.length) }}–{{ packageRangeEnd(w.packages.length) }} of {{ w.packages.length }}</span>
              <button matButton (click)="nextPackagePage(w.packages.length)" [disabled]="packagePage() + 1 >= packagePageCount(w.packages.length)">Next packages</button>
              <label for="portal-package-page-size">Packages per page
                <select id="portal-package-page-size" [value]="packagePageSize()" (change)="setPackagePageSize($event)">
                  @for (size of pageSizes; track size) { <option [value]="size">{{ size }}</option> }
                </select>
              </label>
            </nav>
          }
        </section>
      }
    }
  `, styles: `.portal-request { padding: 1rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }`
})
export class ClientPortalHome {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly pageSizes = [10, 25, 50] as const;
  readonly requestPage = signal(0);
  readonly requestPageSize = signal(10);
  readonly packagePage = signal(0);
  readonly packagePageSize = signal(10);
  readonly ws = this.api.resource(() => `/api/ui/portal?page=${this.requestPage()}&pageSize=${this.requestPageSize()}`, portalWorkspace, 'Portal unavailable. A current client assignment is required.');
  readonly terms = inject(FormBuilder).nonNullable.group({ acknowledged: [false, Validators.requiredTrue] });
  readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  statusLabel(state: string): string { return clientRequestStatusLabel(state); }
  constructor() {
    effect(() => { this.session.invalidation(); if (!this.session.current()) { this.terms.reset(); this.message.set(''); } });
    const interval = setInterval(() => {
      if (!this.busy() && !this.ws.loading() && this.session.current()) {
        this.ws.reload();
      }
    }, 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(interval));
  }
  previousRequestPage(): void { this.requestPage.update(p => Math.max(0, p - 1)); }
  nextRequestPage(): void { this.requestPage.update(p => Math.min(10000, p + 1)); }
  setRequestPageSize(event: Event): void {
    const size = Number((event.target as HTMLSelectElement).value);
    if ((this.pageSizes as readonly number[]).includes(size)) { this.requestPageSize.set(size); this.requestPage.set(0); }
  }
  packageRows<T>(rows: T[]): T[] { const start = this.packagePage() * this.packagePageSize(); return rows.slice(start, start + this.packagePageSize()); }
  packagePageCount(count: number): number { return Math.max(1, Math.ceil(count / this.packagePageSize())); }
  packageRangeStart(count: number): number { return count === 0 ? 0 : this.packagePage() * this.packagePageSize() + 1; }
  packageRangeEnd(count: number): number { return Math.min(count, (this.packagePage() + 1) * this.packagePageSize()); }
  previousPackagePage(): void { this.packagePage.update(p => Math.max(0, p - 1)); }
  nextPackagePage(count: number): void { this.packagePage.update(p => Math.min(this.packagePageCount(count) - 1, p + 1)); }
  setPackagePageSize(event: Event): void {
    const size = Number((event.target as HTMLSelectElement).value);
    if ((this.pageSizes as readonly number[]).includes(size)) { this.packagePageSize.set(size); this.packagePage.set(0); }
  }
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
