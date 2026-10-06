import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
import { decodeCounterparties } from './counterparties-contract';

interface Amendment { id: string; counterpartyId: string; revision: string; displayName: string; address: string; taxIdentifier: string; contactDetails: string; paymentTerms: string; reason: string; proposedByUserId: string; createdAt: string; decision: 'APPROVE' | 'REJECT' | null; reviewReason: string | null; reviewedByUserId: string | null }
interface History { current: ReturnType<typeof decodeCounterparties>['counterparties'][number]; bookkeepingActive: boolean; page: number; pageSize: number; total: number; effectiveAmendment: Amendment | null; amendments: Amendment[] }
export function decodeCounterpartyHistory(value: unknown, client: string, party: string, page = 0): History {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid party history');
  const v = value as Record<string, unknown>;
  const current = decodeCounterparties({ clientId: client, role: null, page: 0, pageSize: 25, total: 1, bookkeepingActive: true, counterparties: [v['current']] }, client, null, 0).counterparties[0];
  if (current.id !== party || typeof v['bookkeepingActive'] !== 'boolean' || v['page'] !== page || v['pageSize'] !== 25 || !Number.isSafeInteger(v['total']) || Number(v['total']) < 0 || !Array.isArray(v['amendments']) || v['amendments'].length > 25 || v['amendments'].length > Number(v['total'])) throw new Error('Wrong party history');
  const seen = new Set<string>();
  const records = [...v['amendments']];
  if (v['effectiveAmendment'] !== null) { if (!v['effectiveAmendment'] || typeof v['effectiveAmendment'] !== 'object') throw new Error('Invalid effective amendment'); const e = v['effectiveAmendment'] as Amendment; const listed = records.find(x => x.id === e.id); if (listed && !['id', 'counterpartyId', 'revision', 'displayName', 'address', 'taxIdentifier', 'contactDetails', 'paymentTerms', 'reason', 'proposedByUserId', 'createdAt', 'decision', 'reviewReason', 'reviewedByUserId'].every(k => listed[k] === (e as unknown as Record<string, unknown>)[k])) throw new Error('Conflicting effective history'); if (!listed) records.push(e); }
  for (const item of records) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid amendment');
    const a = item as Record<string, unknown>;
    if (typeof a['id'] !== 'string' || !guidPattern.test(a['id']) || seen.has(a['id']) || a['counterpartyId'] !== party ||
        typeof a['revision'] !== 'string' || !/^[1-9][0-9]{0,18}$/.test(a['revision']) || BigInt(a['revision']) < 2n ||
        typeof a['proposedByUserId'] !== 'string' || !guidPattern.test(a['proposedByUserId']) ||
        !['displayName', 'address', 'taxIdentifier', 'contactDetails', 'paymentTerms', 'reason', 'createdAt'].every(k => typeof a[k] === 'string') ||
        !String(a['displayName']).trim() || !String(a['reason']).trim() ||
        ![null, 'APPROVE', 'REJECT'].includes(a['decision'] as null | string) ||
        (a['decision'] === null ? a['reviewReason'] !== null || a['reviewedByUserId'] !== null : typeof a['reviewReason'] !== 'string' || !a['reviewReason'].trim() || typeof a['reviewedByUserId'] !== 'string' || !guidPattern.test(a['reviewedByUserId']) || a['reviewedByUserId'] === a['proposedByUserId'])) throw new Error('Invalid amendment evidence');
    seen.add(a['id']);
  }
  if (current.effectiveAmendmentId) {
    const effective = v['effectiveAmendment'] as Amendment | null;
    if (!effective || effective.id !== current.effectiveAmendmentId || effective.decision !== 'APPROVE' || effective.revision !== current.revision || effective.address !== current.address || effective.displayName !== current.displayName || effective.taxIdentifier !== current.taxIdentifier || effective.contactDetails !== current.contactDetails || effective.paymentTerms !== current.paymentTerms) throw new Error('Effective party revision is not supported by its review');
  } else if (current.revision !== '1' || v['effectiveAmendment'] !== null) throw new Error('Missing effective party amendment');
  return value as History;
}
@Component({
  selector: 'audit-counterparty-history', imports: [FormsModule, MatButtonModule],
  template: `<section aria-label="Counterparty revision workspace">
    <h4>Counterparty revisions</h4><button matButton type="button" [disabled]="busy()" (click)="refresh()">Refresh party history</button>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (unknown()) { <p role="status">Outcome is unconfirmed. Inspect refreshed party history before starting another command.</p><button matButton type="button" [disabled]="busy() || !history()" (click)="reset()">I inspected history; start another command</button> }
    @if (history(); as h) {
      @if (!h.bookkeepingActive) { <p>Bookkeeping is inactive. Party revision history remains readable; amendment commands are blocked.</p> }
      <p>Effective party revision {{ h.current.revision }} · {{ h.current.displayName }}</p><p>Effective address: {{ h.current.address || 'Not supplied' }}</p>
      <details><summary>Propose a contact detail amendment</summary><p>The legal identity, financial role, country, currency default and external identity remain fixed. Reclassification and duplicate merges require a separate reviewed workflow.</p>
      <form #amendForm="ngForm" (ngSubmit)="amendForm.valid && propose()"><fieldset [disabled]="busy() || unknown() || !h.bookkeepingActive"><legend>Proposed party details</legend>
        <label>Amended display name <input name="displayName" [(ngModel)]="draft.displayName" (ngModelChange)="reviewed.set(false)" required maxlength="300" /></label>
        <label>Amended address <textarea name="address" [(ngModel)]="draft.address" (ngModelChange)="reviewed.set(false)" maxlength="2000"></textarea></label>
        <label>Amended tax identifier (optional) <input name="tax" [(ngModel)]="draft.taxIdentifier" (ngModelChange)="reviewed.set(false)" maxlength="200" /></label>
        <label>Amended contact details <textarea name="contact" [(ngModel)]="draft.contactDetails" (ngModelChange)="reviewed.set(false)" maxlength="1000"></textarea></label>
        <label>Amended payment terms <input name="terms" [(ngModel)]="draft.paymentTerms" (ngModelChange)="reviewed.set(false)" maxlength="500" /></label>
        <label>Amendment reason <textarea name="reason" [(ngModel)]="draft.reason" (ngModelChange)="reviewed.set(false)" required maxlength="2000"></textarea></label>
        <label><input type="checkbox" name="reviewed" [ngModel]="reviewed()" (ngModelChange)="reviewed.set($event)" /> I checked the current party revision and proposed details.</label>
        <button matButton type="submit" [disabled]="amendForm.invalid || !reviewed() || busy() || unknown()">Submit party amendment</button>
      </fieldset></form></details>
      <h5>Preserved proposals and decisions</h5><p>{{ h.total }} proposals · Page {{ h.page + 1 }}</p><button matButton type="button" [disabled]="busy() || h.page === 0" (click)="refresh(h.page - 1)">Previous party proposals</button><button matButton type="button" [disabled]="busy() || (h.page + 1) * h.pageSize >= h.total" (click)="refresh(h.page + 1)">Next party proposals</button>
      @for (a of h.amendments; track a.id) { <details><summary>Proposed revision {{ a.revision }} · {{ a.decision || 'PENDING' }}</summary>
        <p>Proposed by {{ a.proposedByUserId }} · {{ a.createdAt }}</p><p>{{ a.displayName }} · {{ a.address }}</p><p>Tax identifier: {{ a.taxIdentifier || 'Not supplied' }} · Contact: {{ a.contactDetails }} · Terms: {{ a.paymentTerms }}</p><p>Proposal reason: {{ a.reason }}</p>
        @if (a.decision) { <p>Review: {{ a.decision }} · {{ a.reviewReason }} · {{ a.reviewedByUserId }}</p> }
        @else { <button matButton type="button" [disabled]="busy() || unknown() || a.proposedByUserId === userId()" (click)="choose(a)">Review this party amendment</button> }
      </details> }
      @if (target(); as a) { <form #reviewForm="ngForm"><fieldset [disabled]="busy() || unknown() || !h.bookkeepingActive"><legend>Review proposed revision {{ a.revision }}</legend>
        <p>Exact proposal: {{ a.displayName }} · {{ a.address }} · {{ a.reason }}</p>
        <label>Party amendment review reason <textarea name="reviewReason" [(ngModel)]="reviewReason" (ngModelChange)="reviewed.set(false)" required maxlength="2000"></textarea></label>
        <label><input type="checkbox" name="reviewed" [ngModel]="reviewed()" (ngModelChange)="reviewed.set($event)" /> I independently reviewed this exact party proposal.</label>
        <button matButton type="button" [disabled]="reviewForm.invalid || !reviewed() || busy() || unknown() || a.revision !== nextRevision()" (click)="decide('APPROVE')">Approve party amendment</button>
        <button matButton type="button" [disabled]="reviewForm.invalid || !reviewed() || busy() || unknown()" (click)="decide('REJECT')">Reject party amendment</button>
      </fieldset></form> }
    }
  </section>`
})
export class CounterpartyHistoryWorkspace {
  readonly clientId = input.required<string>(); readonly partyId = input.required<string>(); readonly changed = output<void>();
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly history = signal<History | null>(null); readonly target = signal<Amendment | null>(null); readonly busy = signal(false); readonly error = signal(''); readonly unknown = signal(false); readonly reviewed = signal(false);
  readonly userId = () => this.session.current()?.userId ?? ''; readonly nextRevision = () => (BigInt(this.history()?.current.revision ?? '0') + 1n).toString();
  draft = { displayName: '', address: '', taxIdentifier: '', contactDetails: '', paymentTerms: '', reason: '' }; reviewReason = '';
  private request = 0; private operation?: Subscription;
  private readonly invalidate = effect(() => { this.clientId(); this.partyId(); this.session.invalidation(); untracked(() => { this.operation?.unsubscribe(); ++this.request; this.history.set(null); this.busy.set(false); this.unknown.set(false); this.error.set(''); this.reset(); if (this.session.current()) this.refresh(); }); });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  reset(): void { const p = this.history()?.current; this.draft = { displayName: p?.displayName ?? '', address: p?.address ?? '', taxIdentifier: p?.taxIdentifier ?? '', contactDetails: p?.contactDetails ?? '', paymentTerms: p?.paymentTerms ?? '', reason: '' }; this.reviewReason = ''; this.reviewed.set(false); this.target.set(null); this.unknown.set(false); }
  choose(a: Amendment): void { this.target.set(a); this.reviewed.set(false); this.reviewReason = ''; }
  refresh(page = 0): void {
    if (this.busy() || !this.session.current()) return;
    const client = this.clientId(), party = this.partyId(), generation = this.session.invalidation(), request = ++this.request;
    this.busy.set(true); this.history.set(null); this.target.set(null); this.reviewed.set(false); this.error.set('');
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/counterparties/${party}`, { params: { page: String(page), pageSize: '25' } }).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.request || generation !== this.session.invalidation() || client !== this.clientId() || party !== this.partyId()) return; this.busy.set(false); try { this.history.set(decodeCounterpartyHistory(value, client, party, page)); const pending = this.unknown(); this.reset(); this.unknown.set(pending); } catch { this.error.set('Party history could not be validated.'); } },
      error: failure => { if (request !== this.request || generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Party history unavailable. Refresh to retry.'); if (failure.status === 401) this.session.clear(); }
    });
  }
  propose(): void { const h = this.history(); if (!h || !h.bookkeepingActive || this.busy() || this.unknown() || !this.reviewed() || this.target()) return; this.command(`/amendments`, { ...this.draft, expectedRevision: h.current.revision, reviewed: true }, true); }
  decide(decision: 'APPROVE' | 'REJECT'): void { const a = this.target(); if (!a || !this.history()?.bookkeepingActive || this.busy() || this.unknown() || !this.reviewed() || a.proposedByUserId === this.userId() || !this.reviewReason.trim() || (decision === 'APPROVE' && a.revision !== this.nextRevision())) return; this.command(`/amendments/${a.id}/review`, { expectedRevision: a.revision, decision, reason: this.reviewReason, reviewed: true }, false); }
  private command(path: string, body: unknown, proposal: boolean): void {
    const client = this.clientId(), party = this.partyId(), generation = this.session.invalidation(), request = ++this.request;
    this.busy.set(true); this.reviewed.set(false); this.error.set('');
    this.operation = this.http.post<unknown>(`/api/ui/accounting/clients/${client}/counterparties/${party}${path}`, body).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.request || generation !== this.session.invalidation() || client !== this.clientId() || party !== this.partyId()) return; this.busy.set(false); const v = value as Record<string, unknown> | null; if (!v || (proposal ? typeof v['id'] !== 'string' || !guidPattern.test(v['id']) : v['decided'] !== true)) { this.unknown.set(true); this.history.set(null); this.target.set(null); return; } this.reset(); this.refresh(); this.changed.emit(); },
      error: failure => { if (request !== this.request || generation !== this.session.invalidation()) return; this.busy.set(false); if (failure.status === 400 || failure.status === 403) this.error.set('Amendment refused. Refresh the exact revision, service status and assigned authority.'); else { this.unknown.set(true); this.history.set(null); this.target.set(null); this.error.set('Amendment outcome could not be confirmed. Refresh history before another command.'); } if (failure.status === 401) this.session.clear(); }
    });
  }
}
