import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
interface Chart { id: string; version: string; sourceScope: string; status: string; effectiveFrom: string; effectiveTo: string | null; preparedBy: string; publishedBy: string | null; publishedAt: string | null }
interface Charts { clientId: string; items: Chart[]; hasMore: boolean }
interface Account { id: string; stableIdentity: string; accountCode: string; accountName: string; accountType: string; normalBalance: string; isPosting: boolean; parentAccountId: string | null; parentAccountCode: string | null; childCount: number }
interface Publication { chartId: string; version: string; digest: string; accountCount: number; canPublish: boolean }
interface Accounts { items: Account[]; totalCount: number; page: number; pageSize: number }
export interface Alias { id: string; clientAccountId: string; accountCode: string; sourceSystem: string; aliasCode: string; aliasName: string; createdAt: string }
export function decodeCharts(value: unknown): Charts {
  if (!value || typeof value !== 'object') throw new Error('Invalid charts');
  const v = value as Record<string, unknown>;
  if (typeof v['clientId'] !== 'string' || !guidPattern.test(v['clientId']) || typeof v['hasMore'] !== 'boolean' || !Array.isArray(v['items']) || v['items'].length > 100) throw new Error('Invalid charts');
  for (const c of v['items']) {
    if (!c || typeof c.id !== 'string' || !guidPattern.test(c.id) || typeof c.version !== 'string' || !/^[1-9]\d{0,9}$/.test(c.version) ||
      !['sourceScope', 'status'].every(k => typeof c[k] === 'string') || typeof c.effectiveFrom !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(c.effectiveFrom) ||
      !(c.effectiveTo === null || (typeof c.effectiveTo === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(c.effectiveTo))) ||
      typeof c.preparedBy !== 'string' || !guidPattern.test(c.preparedBy) || !(c.publishedBy === null || (typeof c.publishedBy === 'string' && guidPattern.test(c.publishedBy))) ||
      !(c.publishedAt === null || typeof c.publishedAt === 'string')) throw new Error('Invalid chart revision');
  }
  return v as unknown as Charts;
}
export function decodePublication(value: unknown): Publication {
  if (!value || typeof value !== 'object') throw new Error('Invalid publication');
  const v = value as Record<string, unknown>;
  if (typeof v['chartId'] !== 'string' || !guidPattern.test(v['chartId']) || typeof v['version'] !== 'string' || !/^[1-9]\d{0,9}$/.test(v['version']) ||
    typeof v['digest'] !== 'string' || !/^[a-f0-9]{64}$/.test(v['digest']) || !Number.isSafeInteger(v['accountCount']) || Number(v['accountCount']) < 0 || Number(v['accountCount']) > 10000 ||
    typeof v['canPublish'] !== 'boolean') throw new Error('Invalid publication');
  return v as unknown as Publication;
}
export function decodeAccounts(value: unknown): Accounts {
  if (!value || typeof value !== 'object') throw new Error('Invalid accounts');
  const v = value as Record<string, unknown>;
  if (!['totalCount', 'page', 'pageSize'].every(k => Number.isSafeInteger(v[k]) && Number(v[k]) >= 0) || Number(v['page']) < 1 || Number(v['page']) > 10000 ||
    Number(v['pageSize']) < 1 || Number(v['pageSize']) > 500 || !Array.isArray(v['items']) || v['items'].length > Number(v['pageSize'])) throw new Error('Invalid account page');
  for (const a of v['items']) {
    if (!a || typeof a.id !== 'string' || !guidPattern.test(a.id) || !['stableIdentity', 'accountCode', 'accountName', 'accountType', 'normalBalance'].every(k => typeof a[k] === 'string') ||
      typeof a.isPosting !== 'boolean' || !Number.isSafeInteger(a.childCount) || a.childCount < 0 || !(a.parentAccountId === null || (typeof a.parentAccountId === 'string' && guidPattern.test(a.parentAccountId))) ||
      !(a.parentAccountCode === null || typeof a.parentAccountCode === 'string')) throw new Error('Invalid account');
  }
  return v as unknown as Accounts;
}
export function decodeAliases(value: unknown): Alias[] {
  if (!Array.isArray(value) || value.length > 1001) throw new Error('Invalid aliases');
  for (const a of value) {
    if (!a || typeof a !== 'object') throw new Error('Invalid alias');
    const item = a as Record<string, unknown>;
    if (typeof item['id'] !== 'string' || !guidPattern.test(item['id']) ||
      typeof item['clientAccountId'] !== 'string' || !guidPattern.test(item['clientAccountId']) ||
      !['accountCode', 'sourceSystem', 'aliasCode', 'aliasName', 'createdAt'].every(k => typeof item[k] === 'string')) {
      throw new Error('Invalid alias');
    }
  }
  return value as Alias[];
}
@Component({ selector: 'audit-accounting-charts', imports: [FormsModule, MatButtonModule], template: `
  <section><h3>Chart of accounts</h3><p>Client chart revisions and account hierarchy. Published revisions retain their original identities.</p>
    <button matButton [disabled]="busy()" (click)="load()">Refresh chart revisions</button>
    @if (loading()) { <p role="status">Loading chart data…</p> }
    @if (error()) { <p role="alert">{{ error() }}</p> }
    @if (data(); as charts) {
      <form #chartForm="ngForm" (ngSubmit)="chartForm.valid && create()"><h4>Create draft chart revision</h4>
        <label>Chart source scope <input name="chartSource" [(ngModel)]="sourceScope" (ngModelChange)="creationReviewed = false" required maxlength="200" /></label>
        <label>Chart effective from <input name="chartDate" type="date" [(ngModel)]="effectiveFrom" (ngModelChange)="creationReviewed = false" required /></label>
        <label><input name="chartReview" type="checkbox" [(ngModel)]="creationReviewed" /> I reviewed this client and the latest chart version {{ charts.items[0]?.version ?? '0' }}.</label>
        <button matButton type="submit" [disabled]="chartForm.invalid || !creationReviewed || busy() || uncertain()">Create draft chart</button>
      </form>
      @if (!charts.items.length) { <p>No chart revisions configured.</p> }
      @for (c of charts.items; track c.id) { <section><h4>Revision {{ c.version }} · {{ c.status }}</h4><p>{{ c.sourceScope }} · {{ c.effectiveFrom }} — {{ c.effectiveTo ?? 'Open-ended' }}</p>
        <p>Prepared by {{ c.preparedBy }} · Published by {{ c.publishedBy ?? 'Not published' }} · {{ c.publishedAt ?? 'Not published' }}</p>
        <button matButton [disabled]="busy()" (click)="select(c.id)">View accounts · revision {{ c.version }}</button></section> }
      @if (charts.hasMore) { <p>Showing the latest 100 chart revisions.</p> }
    }
    @for (c of data()?.items ?? []; track c.id) { @if (c.id === selected && c.status === 'DRAFT') {
      <form #accountForm="ngForm" (ngSubmit)="accountForm.valid && addAccount(c)"><h4>Add account to revision {{ c.version }}</h4>
        <label>Stable account identity <input name="stable" [(ngModel)]="account.stable" (ngModelChange)="accountReviewed = false" required maxlength="200" /></label>
        <label>Account code <input name="code" [(ngModel)]="account.code" (ngModelChange)="accountReviewed = false" required maxlength="100" /></label>
        <label>Account name <input name="name" [(ngModel)]="account.name" (ngModelChange)="accountReviewed = false" required maxlength="200" /></label>
        <label>Account classification <input name="type" [(ngModel)]="account.type" (ngModelChange)="accountReviewed = false" required maxlength="50" /></label>
        <label for="normal-balance">Normal balance</label><select id="normal-balance" name="balance" [(ngModel)]="account.balance" (ngModelChange)="accountReviewed = false"><option value="DEBIT">Debit</option><option value="CREDIT">Credit</option></select>
        <label>Parent stable identity <input name="parent" [(ngModel)]="account.parent" (ngModelChange)="accountReviewed = false" maxlength="200" /></label>
        <label><input type="checkbox" name="posting" [(ngModel)]="account.posting" (ngModelChange)="accountReviewed = false" /> Posting account</label>
        <label><input type="checkbox" name="review" [(ngModel)]="accountReviewed" /> I reviewed this account and its parent within this draft chart.</label>
        <button matButton type="submit" [disabled]="accountForm.invalid || !accountReviewed || busy() || uncertain()">Add reviewed account</button>
      </form>
      <form #aliasForm="ngForm" (ngSubmit)="aliasForm.valid && addAlias(c)"><h4>Add source account alias to revision {{ c.version }}</h4>
        <label for="alias-account">Target client account</label>
        <select id="alias-account" name="aliasAccount" [(ngModel)]="alias.accountId" (ngModelChange)="aliasReviewed = false" required>
          <option value="">Choose account</option>
          @for (a of accounts()?.items ?? []; track a.id) { <option [value]="a.id">{{ a.accountCode }} · {{ a.accountName }}</option> }
        </select>
        <label>Source system <input name="sourceSystem" [(ngModel)]="alias.sourceSystem" (ngModelChange)="aliasReviewed = false" required maxlength="100" /></label>
        <label>Alias code <input name="aliasCode" [(ngModel)]="alias.code" (ngModelChange)="aliasReviewed = false" required maxlength="100" /></label>
        <label>Alias name <input name="aliasName" [(ngModel)]="alias.name" (ngModelChange)="aliasReviewed = false" maxlength="200" /></label>
        <label><input type="checkbox" name="aliasReview" [(ngModel)]="aliasReviewed" /> I reviewed this source account alias within this draft chart.</label>
        <button matButton type="submit" [disabled]="aliasForm.invalid || aliases() === null || aliasLoading() || !aliasReviewed || busy() || uncertain()">Add reviewed alias</button>
      </form>
    } }
    @if (selected) { <button matButton [disabled]="busy()" (click)="reviewPublication()">Review publication snapshot</button> }
    @if (selected && aliasLoading()) { <p role="status">Loading source account aliases…</p> }
    @if (selected && aliasError()) { <p role="alert">{{ aliasError() }}</p><button matButton type="button" (click)="loadAliases(selected)">Retry aliases</button> }
    @if (publication(); as p) {
      <h4>Independent publication review · revision {{ p.version }}</h4><p>{{ p.accountCount }} accounts · SHA-256 {{ p.digest }}</p>
      <p>Review account pages and hierarchy before publication. The Application command rechecks the complete account set under the chart lock.</p>
      @if (!p.canPublish) { <p>Publication requires a nonempty draft and an authorized reviewer other than the chart preparer.</p> }
      <label><input type="checkbox" [(ngModel)]="publicationReviewed" /> I independently reviewed the entire chart and this exact account snapshot.</label>
      <button matButton [disabled]="!p.canPublish || !publicationReviewed || busy() || uncertain()" (click)="publish()">Publish reviewed chart</button>
    }
    @if (uncertain()) { <p role="alert">An outcome is unconfirmed. Refresh persisted chart revisions and accounts before another change.</p> }
    @if (accounts(); as result) { <div class="table-scroll"><table><caption>Accounts in selected chart revision</caption><thead><tr><th>Code / stable identity</th><th>Name</th><th>Type / normal balance</th><th>Posting</th><th>Parent / children</th></tr></thead>
      <tbody>@for (a of result.items; track a.id) { <tr><td>{{ a.accountCode }}<small>{{ a.stableIdentity }}</small></td><td>{{ a.accountName }}</td><td>{{ a.accountType }} / {{ a.normalBalance }}</td><td>{{ a.isPosting ? 'Yes' : 'No' }}</td><td>{{ a.parentAccountCode ?? 'Root' }} / {{ a.childCount }}</td></tr> }</tbody></table></div>
      @if (!result.items.length) { <p>No accounts in this page.</p> }
      <nav aria-label="Chart account pages"><button matButton [disabled]="result.page <= 1" (click)="select(selected, result.page - 1)">Previous</button><span>Page {{ result.page }} · {{ result.totalCount }} accounts</span>
        <button matButton [disabled]="result.page * result.pageSize >= result.totalCount" (click)="select(selected, result.page + 1)">Next</button></nav>
    }
    @if (aliases(); as aliasList) {
      <div class="table-scroll"><table><caption>Source account aliases in selected chart revision</caption><thead><tr><th>Alias code</th><th>Source system</th><th>Client account</th><th>Alias name</th></tr></thead>
        <tbody>@for (al of aliasList; track al.id) { <tr><td>{{ al.aliasCode }}</td><td>{{ al.sourceSystem }}</td><td>{{ al.accountCode }}</td><td>{{ al.aliasName }}</td></tr> }</tbody></table></div>
      @if (!aliasList.length) { <p>No source account aliases configured for this revision.</p> }
      @if (aliasesTruncated()) { <p>Showing the first 1,000 source aliases for this revision.</p> }
    }
  </section>` })
export class AccountingCharts {
  readonly clientId = input.required<string>(); readonly data = signal<Charts | null>(null); readonly accounts = signal<Accounts | null>(null);
  readonly aliases = signal<Alias[] | null>(null);
  readonly aliasLoading = signal(false); readonly aliasError = signal('');
  readonly aliasesTruncated = signal(false);
  readonly loading = signal(false); readonly error = signal(''); selected = '';
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  private request?: Subscription; private detail?: Subscription; private write?: Subscription; private publicationRead?: Subscription;
  private aliasRead?: Subscription;
  readonly publication = signal<Publication | null>(null); publicationReviewed = false;
  readonly busy = signal(false); readonly uncertain = signal(false);
  sourceScope = ''; effectiveFrom = ''; creationReviewed = false; accountReviewed = false;
  account = { stable: '', code: '', name: '', type: '', balance: 'DEBIT', parent: '', posting: true };
  aliasReviewed = false;
  alias = { accountId: '', sourceSystem: '', code: '', name: '' };
  constructor() {
    effect(() => { this.clientId(); this.session.invalidation(); const staff = this.session.current()?.staff;
      untracked(() => { this.request?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.publicationRead?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; this.busy.set(false); this.creationReviewed = false; this.accountReviewed = false; this.aliasReviewed = false; this.aliasLoading.set(false); this.aliasError.set(''); this.aliasesTruncated.set(false); this.sourceScope = ''; this.effectiveFrom = ''; this.account = { stable: '', code: '', name: '', type: '', balance: 'DEBIT', parent: '', posting: true }; this.alias = { accountId: '', sourceSystem: '', code: '', name: '' }; this.data.set(null); this.accounts.set(null); this.aliases.set(null); this.selected = ''; this.loading.set(false); if (staff) this.load(); }); });
    inject(DestroyRef).onDestroy(() => { this.request?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.publicationRead?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; });
  }
  load(): void {
    if (this.busy()) return;
    this.request?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.publicationRead?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; this.busy.set(false); this.creationReviewed = false; this.accountReviewed = false; this.aliasReviewed = false; this.aliasLoading.set(false); this.aliasError.set(''); this.aliasesTruncated.set(false); this.sourceScope = ''; this.effectiveFrom = ''; this.account = { stable: '', code: '', name: '', type: '', balance: 'DEBIT', parent: '', posting: true }; this.alias = { accountId: '', sourceSystem: '', code: '', name: '' }; this.data.set(null); this.accounts.set(null); this.aliases.set(null); this.error.set(''); this.loading.set(true);
    const generation = this.session.invalidation(), client = this.clientId();
    this.request = this.http.get<unknown>('/api/ui/accounting/clients/' + client + '/charts').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || client !== this.clientId()) return;
        try { const c = decodeCharts(value); if (c.clientId !== client) throw new Error('Mismatch'); this.data.set(c); this.uncertain.set(false); this.selected = ''; } catch { this.error.set('Unsupported chart response.'); } this.loading.set(false); },
      error: () => { if (generation !== this.session.invalidation() || client !== this.clientId()) return; this.loading.set(false); this.error.set('Chart revisions unavailable. Refresh your current assignment.'); },
    });
  }
  select(id: string, page = 1): void {
    if (this.busy()) return; this.accountReviewed = false; this.aliasReviewed = false; this.publicationRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false;
    if (!this.data()?.items.some(c => c.id === id)) return;
    this.detail?.unsubscribe(); this.aliasRead?.unsubscribe(); this.selected = id; this.accounts.set(null); this.aliases.set(null); this.aliasesTruncated.set(false); this.aliasError.set(''); this.aliasLoading.set(false); this.error.set(''); this.loading.set(true);
    const generation = this.session.invalidation(), client = this.clientId();
    this.detail = this.http.get<unknown>('/api/ui/accounting/clients/' + client + '/charts/' + id + '/accounts', { params: { page, pageSize: 50 } }).pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || client !== this.clientId() || id !== this.selected) return;
        try { const result = decodeAccounts(value); if (result.page !== page) throw new Error('Mismatch'); this.accounts.set(result); } catch { this.error.set('Unsupported account response.'); } this.loading.set(false); },
      error: () => { if (generation !== this.session.invalidation() || client !== this.clientId() || id !== this.selected) return; this.loading.set(false); this.error.set('Accounts unavailable. Refresh chart revisions.'); },
    });
    this.loadAliases(id, generation);
  }
  loadAliases(id: string, generation = this.session.invalidation()): void {
    if (!this.data()?.items.some(c => c.id === id) || id !== this.selected) return;
    this.aliasRead?.unsubscribe(); this.aliases.set(null); this.aliasesTruncated.set(false); this.aliasError.set(''); this.aliasLoading.set(true);
    const client = this.clientId();
    this.aliasRead = this.http.get<unknown>('/api/ui/accounting/clients/' + client + '/charts/' + id + '/aliases').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || client !== this.clientId() || id !== this.selected) return;
        try { const aliases = decodeAliases(value); this.aliasesTruncated.set(aliases.length > 1000); this.aliases.set(aliases.slice(0, 1000)); }
        catch { this.aliasError.set('Source account aliases returned an unsupported response. Retry before continuing.'); }
        this.aliasLoading.set(false); },
      error: failure => { if (generation !== this.session.invalidation() || client !== this.clientId() || id !== this.selected) return;
        this.aliasLoading.set(false); this.aliasError.set('Source account aliases are unavailable. Retry before continuing.');
        if (failure.status === 401) this.session.clear(); },
    });
  }
  create(): void {
    const charts = this.data();
    if (!charts || !this.creationReviewed || this.busy() || this.uncertain()) return;
    this.mutate('/charts', { latestVersion: charts.items[0]?.version ?? '0', sourceScope: this.sourceScope, effectiveFrom: this.effectiveFrom, reviewed: true });
  }
  addAccount(chart: Chart): void {
    if (!this.accountReviewed || this.busy() || this.uncertain() || chart.status !== 'DRAFT' || chart.id !== this.selected) return;
    this.mutate('/charts/' + chart.id + '/accounts', { version: chart.version, reviewed: true, accounts: [{ stableIdentity: this.account.stable,
      accountCode: this.account.code, accountName: this.account.name, accountType: this.account.type, normalBalance: this.account.balance,
      isPosting: this.account.posting, parentStableIdentity: this.account.parent.trim() || null }] });
  }
  addAlias(chart: Chart): void {
    if (this.aliases() === null || this.aliasLoading() || !this.aliasReviewed || this.busy() || this.uncertain() || chart.status !== 'DRAFT' || chart.id !== this.selected || !this.alias.accountId) return;
    this.mutate('/charts/' + chart.id + '/aliases', { version: chart.version, reviewed: true, aliases: [{
      clientAccountId: this.alias.accountId, sourceSystem: this.alias.sourceSystem.trim(),
      aliasCode: this.alias.code.trim(), aliasName: this.alias.name.trim()
    }] });
  }
  private mutate(path: string, body: unknown): void {
    const generation = this.session.invalidation(), client = this.clientId();
    this.request?.unsubscribe(); this.detail?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publicationRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; this.busy.set(true); this.error.set('');
    this.write = this.http.post('/api/ui/accounting/clients/' + client + path, body).pipe(timeout(15000)).subscribe({
      next: () => { if (generation !== this.session.invalidation() || client !== this.clientId()) return; this.busy.set(false); this.load(); },
      error: () => { if (generation !== this.session.invalidation() || client !== this.clientId()) return; this.busy.set(false); this.uncertain.set(true);
        this.creationReviewed = false; this.accountReviewed = false; this.aliasReviewed = false; this.error.set('Chart change not confirmed. Refresh persisted revisions and accounts.'); },
    });
  }

  reviewPublication(): void {
    const chart = this.data()?.items.find(c => c.id === this.selected);
    if (!chart || this.busy()) return;
    this.publicationRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false;
    const generation = this.session.invalidation(), client = this.clientId();
    this.publicationRead = this.http.get<unknown>('/api/ui/accounting/clients/' + client + '/charts/' + chart.id + '/publication').pipe(timeout(15000)).subscribe({
      next: value => { if (generation !== this.session.invalidation() || client !== this.clientId() || chart.id !== this.selected) return;
        try { const p = decodePublication(value); if (p.chartId !== chart.id || p.version !== chart.version) throw new Error('Mismatch'); this.publication.set(p); }
        catch { this.error.set('Publication snapshot unavailable. Refresh chart revisions.'); } },
      error: () => { if (generation === this.session.invalidation() && client === this.clientId()) this.error.set('Publication snapshot unavailable. Refresh chart revisions.'); },
    });
  }
  publish(): void {
    const p = this.publication();
    if (!p || !p.canPublish || !this.publicationReviewed || p.chartId !== this.selected || this.busy() || this.uncertain()) return;
    this.mutate('/charts/' + p.chartId + '/publish', { version: p.version, digest: p.digest, reviewed: true });
  }

}
