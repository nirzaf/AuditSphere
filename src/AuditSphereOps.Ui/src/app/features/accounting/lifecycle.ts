import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
interface Readiness { periodId: string; periodCode: string; periodStatus: string; canClose: boolean; canDecideClose: boolean; canReopen: boolean; packageCount: number; blockers: { code: string; detail: string }[] }
export function decodeReadiness(value: unknown): Readiness {
  if (!value || typeof value !== 'object') throw new Error('Invalid readiness');
  const v = value as Record<string, unknown>;
  if (typeof v['periodId'] !== 'string' || !guidPattern.test(v['periodId']) ||
    !['periodCode', 'periodStatus'].every(k => typeof v[k] === 'string') ||
    !['canClose', 'canDecideClose', 'canReopen'].every(k => typeof v[k] === 'boolean') ||
    !Number.isSafeInteger(v['packageCount']) || Number(v['packageCount']) < 0 ||
    !Array.isArray(v['blockers']) || v['blockers'].length > 100 ||
    !v['blockers'].every(b => b && typeof b.code === 'string' && typeof b.detail === 'string')) throw new Error('Invalid readiness');
  return v as unknown as Readiness;
}
@Component({ selector: 'audit-period-lifecycle', imports: [FormsModule, MatButtonModule], template: `
  <section><h4>Period decision · {{ code() }}</h4>
    <button matButton [disabled]="busy()" (click)="load()">Refresh close readiness</button>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (data(); as r) {
      <p>Status: {{ r.periodStatus }} · Reviewed revision {{ revision() }} · {{ r.packageCount }} financial packages</p>
      @for (b of r.blockers; track b.code) { <p>{{ b.detail }}</p> }
      @if (r.canClose) { <p>Current close checks have no blockers. This does not replace the authorized close decision.</p> }
      <label>Decision reason <textarea [(ngModel)]="reason" maxlength="2000"></textarea></label>
      <label><input type="checkbox" [(ngModel)]="reviewed" /> I reviewed this period, revision and decision.</label>
      @if (r.periodStatus === 'CLOSED') {
        <p>Reopening creates a new period revision and records immutable amendment evidence.</p>
        <button matButton [disabled]="busy() || !reviewed || !reason.trim() || !r.canReopen" (click)="decide('reopen')">Reopen period</button>
      } @else {
        <button matButton [disabled]="busy() || !reviewed || !reason.trim() || !r.canClose || !r.canDecideClose" (click)="decide('close')">Close period</button>
      }
    }
  </section>` })
export class PeriodLifecycle {
  readonly clientId = input.required<string>(); readonly periodId = input.required<string>();
  readonly revision = input.required<string>(); readonly code = input.required<string>();
  readonly changed = output<void>(); readonly data = signal<Readiness | null>(null);
  readonly error = signal(''); readonly busy = signal(false); reason = ''; reviewed = false;
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  private read?: Subscription; private write?: Subscription;
  constructor() {
    effect(() => { this.clientId(); this.periodId(); this.revision(); this.session.invalidation(); const staff = this.session.current()?.staff;
      untracked(() => { this.read?.unsubscribe(); this.write?.unsubscribe(); this.data.set(null); this.reason = ''; this.reviewed = false; this.busy.set(false); if (staff) this.load(); }); });
    inject(DestroyRef).onDestroy(() => { this.read?.unsubscribe(); this.write?.unsubscribe(); });
  }
  private path(): string { return '/api/ui/accounting/clients/' + this.clientId() + '/periods/' + this.periodId(); }
  load(): void {
    if (this.busy()) return;
    this.read?.unsubscribe(); this.data.set(null); this.error.set(''); this.reviewed = false;
    const generation = this.session.invalidation();
    this.read = this.http.get<unknown>(this.path() + '/readiness').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation()) return; try { const r = decodeReadiness(value); if (r.periodId !== this.periodId()) throw new Error('Mismatch'); this.data.set(r); } catch { this.error.set('Unsupported readiness response.'); } },
      error: () => { if (generation === this.session.invalidation()) this.error.set('Period readiness unavailable. Refresh your assignment.'); },
    });
  }
  decide(action: 'close' | 'reopen'): void {
    const r = this.data();
    if (!r || this.busy() || !this.reviewed || !this.reason.trim() || (action === 'close' ? !r.canClose || !r.canDecideClose : r.periodStatus !== 'CLOSED' || !r.canReopen)) return;
    this.read?.unsubscribe(); this.busy.set(true); const generation = this.session.invalidation();
    this.write = this.http.post(this.path() + '/' + action, { revision: this.revision(), reason: this.reason, reviewed: true }).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.data.set(null); this.changed.emit(); },
      error: () => { if (generation !== this.session.invalidation()) return; this.busy.set(false); this.data.set(null); this.reviewed = false; this.error.set('Decision not confirmed. Refresh the client and persisted period revision before another decision.'); },
    });
  }
}
