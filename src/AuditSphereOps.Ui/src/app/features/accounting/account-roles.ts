import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
const roles = ['AR','AP','TAX_RECOVERABLE','TAX_PAYABLE','REVENUE','PURCHASE_EXPENSE','PURCHASE_ASSET','RETAINED_EARNINGS','ROUNDING','FX'];
interface Configuration { id: string; chartVersionId: string; accountId: string; role: string; effectiveFrom: string; effectiveTo: string | null; reason: string; proposedByUserId: string; decision: string | null; decisionReason: string | null; reviewedByUserId: string | null }
interface Account { id: string; chartVersionId: string; accountCode: string; accountName: string; accountType: string }
interface Workspace { clientId: string; bookkeepingActive: boolean; canReview: boolean; page: number; hasMore: boolean; hasMoreAccounts: boolean; configurations: Configuration[]; accounts: Account[] }
export function decodeAccountRoles(value: unknown, client: string, page: number): Workspace {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid role workspace');
  const w = value as Workspace;
  if (w.clientId !== client || w.page !== page || typeof w.bookkeepingActive !== 'boolean' || typeof w.canReview !== 'boolean' || typeof w.hasMore !== 'boolean' || typeof w.hasMoreAccounts !== 'boolean' || !Array.isArray(w.configurations) || w.configurations.length > 25 || !Array.isArray(w.accounts) || w.accounts.length > 50) throw new Error('Invalid scoped account-role workspace');
  const date = /^\d{4}-\d{2}-\d{2}$/;
  for (const c of w.configurations) if (![c.id,c.chartVersionId,c.accountId,c.proposedByUserId].every(x => typeof x === 'string' && guidPattern.test(x)) || !roles.includes(c.role) || !date.test(c.effectiveFrom) || c.effectiveTo !== null && !date.test(c.effectiveTo) || typeof c.reason !== 'string' || !c.reason.trim() || ![null,'APPROVE','REJECT'].includes(c.decision) || (c.decision === null ? c.reviewedByUserId !== null || c.decisionReason !== null : !guidPattern.test(c.reviewedByUserId ?? '') || c.reviewedByUserId === c.proposedByUserId || !c.decisionReason?.trim())) throw new Error('Invalid retained role proposal');
  for (const a of w.accounts) if (![a.id,a.chartVersionId].every(x => typeof x === 'string' && guidPattern.test(x)) || ![a.accountCode,a.accountName,a.accountType].every(x => typeof x === 'string' && x.trim())) throw new Error('Invalid role account');
  if (new Set(w.configurations.map(c => c.id)).size !== w.configurations.length || new Set(w.accounts.map(a => a.id)).size !== w.accounts.length) throw new Error('Duplicate role identities');
  return w;
}
@Component({ selector: 'audit-account-roles', imports: [FormsModule, MatButtonModule], template: `<details><summary>Client posting account roles</summary>
  <p>Independent approval activates an account role for its stated dates. Tax and ancillary roles are optional. AR/AP control activity requires a supported counterparty open-item workflow.</p>
  @if (error()) { <p role="alert">{{ error() }}</p> }
  @if (unknown()) { <p role="status">The role command outcome is unconfirmed. Refresh history before preparing another command.</p> }
  <label>Find approved posting accounts <input [(ngModel)]="search" maxlength="100" /></label><button matButton [disabled]="busy()" (click)="load(0)">Refresh client account roles</button>
  @if (workspace(); as w) {
    @if (!w.bookkeepingActive) { <p>Bookkeeping service inactive. Retained role history remains available.</p> }
    @if (w.hasMoreAccounts) { <p>More approved accounts match. Refine the account search.</p> }
    <table><caption>Retained client account-role history</caption><thead><tr><th>Role</th><th>Account identity</th><th>Effective dates</th><th>Reason</th><th>Review</th></tr></thead><tbody>@for (c of w.configurations; track c.id) { <tr><td>{{ c.role }}</td><td>{{ c.accountId }}</td><td>{{ c.effectiveFrom }} – {{ c.effectiveTo || 'No end date' }}</td><td>{{ c.reason }}</td><td>{{ c.decision || 'Pending independent review' }} {{ c.decisionReason }} @if (w.canReview && !c.decision && c.proposedByUserId !== userId() && w.bookkeepingActive) { <button matButton [disabled]="busy() || unknown()" (click)="choose(c)">Review role {{ c.role }}</button> }</td></tr> }</tbody></table>
    <button matButton [disabled]="busy() || w.page === 0" (click)="load(w.page - 1)">Previous role history</button><button matButton [disabled]="busy() || !w.hasMore" (click)="load(w.page + 1)">Next role history</button>
    <form #f="ngForm" (ngSubmit)="f.valid && propose()"><fieldset [disabled]="busy() || unknown() || !w.bookkeepingActive"><legend>Prepare an account-role proposal</legend>
      <label>Client role account <select aria-label="Client role account" name="account" [(ngModel)]="accountId" (ngModelChange)="assent.set(false)" required><option value="">Choose approved account</option>@for (a of w.accounts; track a.id) { <option [value]="a.id">{{ a.accountCode }} · {{ a.accountName }} · {{ a.accountType }}</option> }</select></label>
      <label>Posting account role <select aria-label="Posting account role" name="role" [(ngModel)]="role" (ngModelChange)="assent.set(false)" required><option value="">Choose explicit role</option>@for (r of roles; track r) { <option [value]="r">{{ r }}</option> }</select></label>
      <label>Role effective from <input type="date" name="from" [(ngModel)]="from" (ngModelChange)="assent.set(false)" required /></label><label>Role effective to <input type="date" name="to" [(ngModel)]="to" (ngModelChange)="assent.set(false)" /></label>
      <label>Account role proposal reason <input name="reason" [(ngModel)]="reason" (ngModelChange)="assent.set(false)" required maxlength="2000" /></label><label><input type="checkbox" name="assent" [ngModel]="assent()" (ngModelChange)="assent.set($event)" /> I checked the client account, role and dates.</label>
      <button matButton type="submit" [disabled]="f.invalid || !assent()">Propose client account role</button>
    </fieldset></form>
    @if (selected(); as c) { <section aria-label="Independent account role review"><p>Review {{ c.role }} · {{ c.accountId }} · {{ c.effectiveFrom }} – {{ c.effectiveTo || 'No end date' }}</p><label>Account role review reason <input [(ngModel)]="reviewReason" (ngModelChange)="reviewAssent.set(false)" maxlength="2000" /></label><label><input type="checkbox" [ngModel]="reviewAssent()" (ngModelChange)="reviewAssent.set($event)" /> I independently checked this exact role proposal.</label><button matButton [disabled]="busy() || unknown() || !reviewAssent() || !reviewReason.trim()" (click)="review('APPROVE')">Approve client account role</button><button matButton [disabled]="busy() || unknown() || !reviewAssent() || !reviewReason.trim()" (click)="review('REJECT')">Reject client account role</button></section> }
  }
</details>` })
export class ClientAccountRoles {
  readonly clientId = input.required<string>(); readonly roles = roles; private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly workspace = signal<Workspace | null>(null); readonly busy = signal(false); readonly error = signal(''); readonly unknown = signal(false); readonly assent = signal(false); readonly reviewAssent = signal(false); readonly selected = signal<Configuration | null>(null);
  search = ''; accountId = ''; role = ''; from = ''; to = ''; reason = ''; reviewReason = ''; private request = 0; private operation?: Subscription;
  readonly userId = () => this.session.current()?.userId ?? '';
  private readonly invalidation = effect(() => { this.clientId(); this.session.invalidation(); untracked(() => { ++this.request; this.operation?.unsubscribe(); this.workspace.set(null); this.selected.set(null); this.busy.set(false); this.unknown.set(false); this.error.set(''); this.clear(); this.search = ''; }); });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  private clear(): void { this.accountId = this.role = this.from = this.to = this.reason = this.reviewReason = ''; this.assent.set(false); this.reviewAssent.set(false); }
  load(page: number): void { if (this.busy()) return; const client = this.clientId(), generation = this.session.invalidation(), request = ++this.request; this.busy.set(true); this.workspace.set(null); this.selected.set(null); this.assent.set(false); this.reviewAssent.set(false);
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/account-roles`, { params: { page: String(page), accountSearch: this.search.trim() } }).pipe(timeout(15000)).subscribe({ next: value => { if (!this.current(client,generation,request)) return; this.busy.set(false); try { this.workspace.set(decodeAccountRoles(value,client,page)); if (this.unknown()) this.clear(); this.unknown.set(false); this.error.set(''); } catch { this.error.set('Account role history could not be validated.'); } }, error: e => this.failed(e,client,generation,request,false) });
  }
  choose(c: Configuration): void { if (this.busy() || this.unknown()) return; this.selected.set(c); this.reviewReason = ''; this.reviewAssent.set(false); }
  propose(): void { const w = this.workspace(), a = w?.accounts.find(x => x.id === this.accountId); if (!w?.bookkeepingActive || !a || !this.assent() || !roles.includes(this.role) || !this.from || !this.reason.trim()) return;
    this.command('/account-roles', { reviewed: true, proposal: { chartVersionId: a.chartVersionId, accountId: a.id, role: this.role, effectiveFrom: this.from, effectiveTo: this.to || null, reason: this.reason.trim() } });
  }
  review(decision: string): void { const c = this.selected(); if (!c || c.decision || c.proposedByUserId === this.userId() || !this.reviewAssent() || !this.reviewReason.trim() || !this.workspace()?.bookkeepingActive) return; this.command(`/account-roles/${c.id}/review`, { reviewed: true, decision, reason: this.reviewReason.trim() }); }
  private command(path: string, body: unknown): void { if (this.busy() || this.unknown()) return; const client = this.clientId(), generation = this.session.invalidation(), request = ++this.request; this.busy.set(true); this.assent.set(false); this.reviewAssent.set(false); this.error.set(''); this.operation = this.http.post(`/api/ui/accounting/clients/${client}${path}`,body).pipe(timeout(15000)).subscribe({ next: () => { if (!this.current(client,generation,request)) return; this.busy.set(false); this.clear(); this.load(0); }, error: e => this.failed(e,client,generation,request,true) }); }
  private current(client: string, generation: number, request: number): boolean { return client === this.clientId() && generation === this.session.invalidation() && request === this.request; }
  private failed(e: { status: number }, client: string, generation: number, request: number, command: boolean): void { if (!this.current(client,generation,request)) return; this.busy.set(false); this.assent.set(false); this.reviewAssent.set(false); if (command && e.status !== 400 && e.status !== 403) this.unknown.set(true); this.error.set(command ? 'Role command refused or unconfirmed. Refresh current history before continuing.' : 'Role history unavailable.'); if (e.status === 401) this.session.clear(); }
}
