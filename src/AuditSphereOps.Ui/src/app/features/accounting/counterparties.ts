import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';

interface Party { id: string; clientId: string; legalName: string; displayName: string; role: string; address: string; country: string; taxIdentifier: string; contactDetails: string; paymentTerms: string; defaultCurrency: string; externalSystem: string; externalReference: string; createdByUserId: string; createdAt: string }
interface PartyList { clientId: string; role: string | null; page: number; pageSize: number; total: number; bookkeepingActive: boolean; counterparties: Party[] }
export function decodeCounterparties(value: unknown, client: string, role: string | null, page: number): PartyList {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid party list');
  const v = value as Record<string, unknown>;
  if (v['clientId'] !== client || v['role'] !== role || v['page'] !== page || v['pageSize'] !== 25 || typeof v['bookkeepingActive'] !== 'boolean' ||
      !Number.isSafeInteger(v['total']) || Number(v['total']) < 0 || !Array.isArray(v['counterparties']) || v['counterparties'].length > 25 || v['counterparties'].length > Number(v['total'])) throw new Error('Invalid scoped party list');
  const seen = new Set<string>();
  for (const item of v['counterparties']) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid party');
    const p = item as Record<string, unknown>;
    if (p['clientId'] !== client || typeof p['id'] !== 'string' || !guidPattern.test(p['id']) || seen.has(p['id']) ||
        typeof p['createdByUserId'] !== 'string' || !guidPattern.test(p['createdByUserId']) ||
        !['CUSTOMER', 'SUPPLIER', 'BOTH'].includes(String(p['role'])) || (role && p['role'] !== role && !(role !== 'BOTH' && p['role'] === 'BOTH')) ||
        !['legalName', 'displayName', 'address', 'country', 'taxIdentifier', 'contactDetails', 'paymentTerms', 'defaultCurrency', 'externalSystem', 'externalReference', 'createdAt'].every(k => typeof p[k] === 'string') ||
        !String(p['legalName']).trim() || !String(p['displayName']).trim() || !/^[A-Z]{2}$/.test(String(p['country'])) || !/^(?:[A-Z]{3})?$/.test(String(p['defaultCurrency']))) throw new Error('Invalid party profile');
    seen.add(p['id']);
  }
  return value as PartyList;
}
const emptyParty = () => ({ legalName: '', displayName: '', role: 'CUSTOMER', address: '', country: '', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: '', externalSystem: '', externalReference: '' });
@Component({
  selector: 'audit-client-counterparties', imports: [FormsModule, MatButtonModule],
  template: `<section aria-labelledby="client-counterparties-heading">
    <h3 id="client-counterparties-heading">Client customers and suppliers</h3>
    <p>These parties belong to the selected client's books. A combined role does not automatically net customer and supplier balances.</p>
    <label for="client-party-filter">Counterparty role filter</label><select id="client-party-filter" [ngModel]="filter" (ngModelChange)="filter = $event; list.set(null)"><option value="">All roles</option><option value="CUSTOMER">Customers</option><option value="SUPPLIER">Suppliers</option><option value="BOTH">Both roles</option></select>
    <button matButton type="button" [disabled]="busy()" (click)="refresh(0)">Refresh counterparties</button>
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (list(); as l) {
      <p>{{ l.total }} matching counterparties · Page {{ l.page + 1 }}</p>
      @if (!l.bookkeepingActive) { <p>Bookkeeping is inactive. Saved profiles remain readable; new profiles are blocked.</p> }
      <div class="table-scroll"><table><caption>Saved client counterparties</caption><thead><tr><th>Name</th><th>Role</th><th>Country</th><th>Reference</th><th>Details</th></tr></thead>
      <tbody>@for (p of l.counterparties; track p.id) { <tr><td>{{ p.displayName }} · {{ p.legalName }}</td><td>{{ p.role }}</td><td>{{ p.country }}</td><td>{{ p.externalSystem }} · {{ p.externalReference }}</td><td><details><summary>View saved party details</summary><p>{{ p.address }}</p><p>Contact: {{ p.contactDetails }}</p><p>Terms: {{ p.paymentTerms }} · Currency default: {{ p.defaultCurrency || 'Not set' }}</p><p>Tax identifier: {{ p.taxIdentifier || 'Not supplied' }}</p></details></td></tr> }</tbody></table></div>
      <button matButton type="button" [disabled]="busy() || l.page === 0" (click)="refresh(l.page - 1)">Previous counterparties</button>
      <button matButton type="button" [disabled]="busy() || (l.page + 1) * l.pageSize >= l.total" (click)="refresh(l.page + 1)">Next counterparties</button>
    }
    <details><summary>Add a customer or supplier</summary>
      <p>Saved profiles are retained as entered. Amendments and reviewed duplicate resolution are not yet available.</p>
      @if (unknown()) { <p role="status">Creation outcome is unknown. Inspect saved counterparties before starting a separate draft.</p> }
      <form #partyForm="ngForm" (ngSubmit)="partyForm.valid && create()"><fieldset [disabled]="busy() || unknown()"><legend>New client counterparty</legend>
        <label>Party legal name <input name="legalName" [(ngModel)]="draft.legalName" (ngModelChange)="reviewed.set(false)" required maxlength="300" /></label>
        <label>Party display name <input name="displayName" [(ngModel)]="draft.displayName" (ngModelChange)="reviewed.set(false)" required maxlength="300" /></label>
        <label for="new-client-party-role">Party role</label><select id="new-client-party-role" name="partyRole" [(ngModel)]="draft.role" (ngModelChange)="reviewed.set(false)"><option value="CUSTOMER">Customer</option><option value="SUPPLIER">Supplier</option><option value="BOTH">Customer and supplier</option></select>
        <label>Party country <input name="country" [(ngModel)]="draft.country" (ngModelChange)="reviewed.set(false)" required pattern="[A-Za-z]{2}" maxlength="2" /></label>
        <label>Party address (optional) <textarea name="address" [(ngModel)]="draft.address" (ngModelChange)="reviewed.set(false)" maxlength="2000"></textarea></label>
        <label>Party contact details (optional) <textarea name="contact" [(ngModel)]="draft.contactDetails" (ngModelChange)="reviewed.set(false)" maxlength="1000"></textarea></label>
        <label>Payment terms (optional) <input name="terms" [(ngModel)]="draft.paymentTerms" (ngModelChange)="reviewed.set(false)" maxlength="500" /></label>
        <label>Currency default (optional) <input name="currency" [(ngModel)]="draft.defaultCurrency" (ngModelChange)="reviewed.set(false)" pattern="[A-Za-z]{3}" maxlength="3" /></label>
        <label>Tax identifier (optional) <input name="tax" [(ngModel)]="draft.taxIdentifier" (ngModelChange)="reviewed.set(false)" maxlength="200" /></label>
        <label>External party system (optional) <input name="externalSystem" [(ngModel)]="draft.externalSystem" (ngModelChange)="reviewed.set(false)" maxlength="100" /></label>
        <label>External party reference (optional) <input name="externalReference" [(ngModel)]="draft.externalReference" (ngModelChange)="reviewed.set(false)" maxlength="200" /></label>
        <label><input type="checkbox" name="reviewed" [ngModel]="reviewed()" (ngModelChange)="reviewed.set($event)" /> I reviewed this client's party identity, role and optional details.</label>
        <button matButton type="submit" [disabled]="partyForm.invalid || !reviewed() || busy() || unknown() || list()?.bookkeepingActive === false">Save client counterparty</button>
      </fieldset></form>
      <button matButton type="button" [disabled]="busy()" (click)="resetDraft()">Start a separate party draft</button>
    </details>
  </section>`,
})
export class ClientCounterparties {
  readonly clientId = input.required<string>(); private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  readonly list = signal<PartyList | null>(null); readonly error = signal(''); readonly busy = signal(false); readonly unknown = signal(false); readonly reviewed = signal(false);
  draft = emptyParty(); filter = ''; private request = 0; private operation?: Subscription;
  private readonly invalidate = effect(() => { this.clientId(); this.session.invalidation(); untracked(() => { this.operation?.unsubscribe(); ++this.request; this.list.set(null); this.error.set(''); this.busy.set(false); this.filter = ''; this.resetDraft(); }); });
  constructor() { inject(DestroyRef).onDestroy(() => this.operation?.unsubscribe()); }
  resetDraft(): void { this.draft = emptyParty(); this.reviewed.set(false); this.unknown.set(false); }
  refresh(page = 0): void {
    if (this.busy()) return;
    const client = this.clientId(); const role = this.filter || null; const generation = this.session.invalidation(); const request = ++this.request;
    const params: Record<string, string> = { page: String(page), pageSize: '25' }; if (role) params['role'] = role;
    this.busy.set(true); this.error.set(''); this.list.set(null);
    this.operation = this.http.get<unknown>(`/api/ui/accounting/clients/${client}/counterparties`, { params }).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.request || generation !== this.session.invalidation() || client !== this.clientId()) return; this.busy.set(false); if ((this.filter || null) !== role) return;
        try { this.list.set(decodeCounterparties(value, client, role, page)); } catch { this.error.set('Counterparty response could not be validated for this client and filter.'); } },
      error: failure => { if (request !== this.request || generation !== this.session.invalidation()) return; this.busy.set(false); this.error.set('Saved counterparties are unavailable. Refresh or retry.'); if (failure.status === 401) this.session.clear(); }
    });
  }
  create(): void {
    if (this.busy() || this.unknown() || !this.reviewed() || this.list()?.bookkeepingActive === false) return;
    const client = this.clientId(); const generation = this.session.invalidation(); const request = ++this.request;
    const party = { ...this.draft }; this.busy.set(true); this.error.set(''); this.reviewed.set(false);
    this.operation = this.http.post<{ id: string }>(`/api/ui/accounting/clients/${client}/counterparties`, { party, reviewed: true }).pipe(timeout(15000)).subscribe({
      next: value => { if (request !== this.request || generation !== this.session.invalidation() || client !== this.clientId()) return; this.busy.set(false);
        if (!value?.id || !guidPattern.test(value.id)) { this.unknown.set(true); return; } this.resetDraft(); this.filter = ''; this.refresh(0); },
      error: failure => { if (request !== this.request || generation !== this.session.invalidation()) return; this.busy.set(false);
        if (failure.status === 400 || failure.status === 403) this.error.set('Creation was refused. Check the details, active service and matching saved parties; reviewed duplicate resolution is not yet available.');
        else { this.unknown.set(true); this.error.set('The creation outcome could not be confirmed. Inspect saved counterparties.'); } if (failure.status === 401) this.session.clear(); }
    });
  }
}
