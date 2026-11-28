import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { Subscription, timeout } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { guidPattern } from '../../core/contracts';
interface Chart { id: string; version: string; sourceScope: string; status: string; effectiveFrom: string; effectiveTo: string | null; preparedBy: string; publishedBy: string | null; publishedAt: string | null }
interface Charts { clientId: string; items: Chart[]; hasMore: boolean; canSaveDimensionSchema: boolean; canPublishDimensionSchema: boolean }
interface Account { id: string; stableIdentity: string; accountCode: string; accountName: string; accountType: string; normalBalance: string; isPosting: boolean; parentAccountId: string | null; parentAccountCode: string | null; childCount: number }
interface Publication { chartId: string; version: string; digest: string; accountCount: number; canPublish: boolean }
interface Accounts { items: Account[]; totalCount: number; page: number; pageSize: number }
export interface Alias { id: string; clientAccountId: string; accountCode: string; sourceSystem: string; aliasCode: string; aliasName: string; createdAt: string }
type DimensionValue = { code: string; name: string; effectiveFrom: string; effectiveTo: string | null; active: boolean; valueId?: string | null };
type DimensionDefinition = { dimensionType: string; code: string; name: string; required: boolean; values: DimensionValue[]; definitionId?: string | null };
type DimensionDraftBody = { baseRevisionRef: string | null; definitions: DimensionDefinition[]; draftRevisionRef?: string; impactAcknowledged?: boolean; expectedCanonicalDigest?: string };
type DimensionDraftReceipt = { draftRevisionRef: string; schemaId: string; revision: number; canonicalDigestVersion: number; canonicalDigest: string; definitions: DimensionDefinition[] };
type DimensionPublication = { schemaRevisionRef: string; schemaId: string; revision: number; canonicalDigestVersion: number; canonicalDigest: string; status: string };
type ResolvedDimensionRevision = { firmId: string; clientId: string; reference: { schemaId: string; revisionId: string; revision: number; canonicalDigestVersion: number; canonicalDigest: string } };

const dimensionTypes = ['BRANCH', 'COST_CENTRE', 'DEPARTMENT', 'PROJECT', 'INTERCOMPANY_COUNTERPARTY'];
function record(value: unknown): value is Record<string, unknown> { return !!value && typeof value === 'object' && !Array.isArray(value); }
function optionalGuid(value: unknown): value is string | null | undefined { return value == null || typeof value === 'string' && guidPattern.test(value); }
function isoDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  if (year < 1 || month < 1 || month > 12 || day < 1) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day <= days[month - 1];
}
function dimensionDefinitions(value: unknown): DimensionDefinition[] {
  if (!Array.isArray(value) || value.length < 1 || value.length > dimensionTypes.length) throw new Error('Enter one to five dimension definitions.');
  for (const d of value) {
    if (!record(d) || typeof d['dimensionType'] !== 'string' || !dimensionTypes.includes(d['dimensionType']) ||
        typeof d['code'] !== 'string' || d['code'].length > 100 || typeof d['name'] !== 'string' || d['name'].length > 300 ||
        typeof d['required'] !== 'boolean' || !Array.isArray(d['values']) || !optionalGuid(d['definitionId'])) throw new Error('A dimension definition is invalid.');
    for (const v of d['values']) {
      if (!record(v) || typeof v['code'] !== 'string' || v['code'].length > 100 || typeof v['name'] !== 'string' || v['name'].length > 300 ||
          !isoDate(v['effectiveFrom']) || !(v['effectiveTo'] == null || isoDate(v['effectiveTo'])) ||
          typeof v['active'] !== 'boolean' || !optionalGuid(v['valueId'])) throw new Error('A dimension value is invalid.');
    }
  }
  return value as DimensionDefinition[];
}
export function decodeDimensionDraftReceipt(value: unknown): DimensionDraftReceipt {
  if (!record(value) || typeof value['draftRevisionRef'] !== 'string' || !guidPattern.test(value['draftRevisionRef']) ||
      typeof value['schemaId'] !== 'string' || !guidPattern.test(value['schemaId']) || !Number.isSafeInteger(value['revision']) || Number(value['revision']) < 1 ||
      value['canonicalDigestVersion'] !== 1 || typeof value['canonicalDigest'] !== 'string' || !/^[a-f0-9]{64}$/.test(value['canonicalDigest'])) throw new Error('Invalid dimension draft receipt.');
  return { ...(value as unknown as DimensionDraftReceipt), definitions: dimensionDefinitions(value['definitions']) };
}
export function decodeDimensionPublication(value: unknown): DimensionPublication {
  if (!record(value) || typeof value['schemaRevisionRef'] !== 'string' || !guidPattern.test(value['schemaRevisionRef']) ||
      typeof value['schemaId'] !== 'string' || !guidPattern.test(value['schemaId']) || !Number.isSafeInteger(value['revision']) || Number(value['revision']) < 1 ||
      value['canonicalDigestVersion'] !== 1 || typeof value['canonicalDigest'] !== 'string' || !/^[a-f0-9]{64}$/.test(value['canonicalDigest']) || typeof value['status'] !== 'string')
    throw new Error('Invalid dimension publication receipt.');
  return value as unknown as DimensionPublication;
}
export function decodeResolvedDimensionRevision(value: unknown): ResolvedDimensionRevision {
  if (!record(value) || typeof value['firmId'] !== 'string' || !guidPattern.test(value['firmId']) ||
      typeof value['clientId'] !== 'string' || !guidPattern.test(value['clientId']) || !record(value['reference'])) throw new Error('Invalid resolved dimension revision.');
  const r = value['reference'];
  if (typeof r['schemaId'] !== 'string' || !guidPattern.test(r['schemaId']) || typeof r['revisionId'] !== 'string' || !guidPattern.test(r['revisionId']) ||
      !Number.isSafeInteger(r['revision']) || Number(r['revision']) < 1 || r['canonicalDigestVersion'] !== 1 ||
      typeof r['canonicalDigest'] !== 'string' || !/^[a-f0-9]{64}$/.test(r['canonicalDigest'])) throw new Error('Invalid resolved dimension revision reference.');
  return value as unknown as ResolvedDimensionRevision;
}
export function decodeCharts(value: unknown): Charts {
  if (!value || typeof value !== 'object') throw new Error('Invalid charts');
  const v = value as Record<string, unknown>;
  if (typeof v['clientId'] !== 'string' || !guidPattern.test(v['clientId']) || typeof v['hasMore'] !== 'boolean' ||
      typeof v['canSaveDimensionSchema'] !== 'boolean' || typeof v['canPublishDimensionSchema'] !== 'boolean' ||
      !Array.isArray(v['items']) || v['items'].length > 100) throw new Error('Invalid charts');
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
    @if (data(); as schemaAccess) {
      <section aria-labelledby="dimension-schema-heading">
        <h3 id="dimension-schema-heading">Client dimension schema revisions</h3>
        <p>Save an exact client-owned draft and have a different authorized reviewer publish it. Publication does not activate a reporting context.</p>
        @if (!schemaAccess.canSaveDimensionSchema && !schemaAccess.canPublishDimensionSchema) {
          <p role="status">Dimension schema maintenance is unavailable for this assignment.</p>
        }
        @if (dimensionError()) { <p role="alert">{{ dimensionError() }}</p> }
        @if (dimensionMessage()) { <p [attr.role]="dimensionUnknown() ? 'alert' : 'status'">{{ dimensionMessage() }}</p> }
        @if (schemaAccess.canSaveDimensionSchema) {
          <label>Exact published base revision ID (leave blank for the first revision)
            <input name="dimensionBaseRevision" [(ngModel)]="dimensionBaseRevisionRef" (ngModelChange)="dimensionInputChanged()" maxlength="36" />
          </label>
          <label>Dimension definitions JSON
            <textarea name="dimensionDefinitions" [(ngModel)]="dimensionDefinitionsJson" (ngModelChange)="dimensionInputChanged()" rows="12" maxlength="200000"></textarea>
          </label>
          @if (dimensionDraft(); as draft) {
            <p>Saved draft revision {{ draft.revision }} · schema {{ draft.schemaId }} · SHA-256 <code>{{ draft.canonicalDigest }}</code></p>
          }
          <button matButton type="button" (click)="saveDimensionDraft()"
            [disabled]="dimensionBusy() || dimensionUnknown() || !schemaAccess.canSaveDimensionSchema || dimensionDefinitionsJson.length > 200000">
            {{ dimensionDraft() ? 'Save changes to this exact draft' : 'Save dimension schema draft' }}
          </button>
          @if (dimensionDraft(); as draft) {
            @if (!schemaAccess.canPublishDimensionSchema) { <p role="status">An independently authorized reviewer must publish this draft.</p> }
            <label><input type="checkbox" name="dimensionReviewed" [(ngModel)]="dimensionReviewed" /> I reviewed the exact saved definitions and base revision shown above.</label>
            <label><input type="checkbox" name="dimensionImpactAcknowledged" [(ngModel)]="dimensionImpactAcknowledged" /> I reviewed the schema impact acknowledgement.</label>
            <button matButton type="button" (click)="publishDimensionDraft()"
              [disabled]="dimensionBusy() || dimensionUnknown() || !schemaAccess.canPublishDimensionSchema || !dimensionReviewed || !dimensionImpactAcknowledged || !dimensionDraftMatchesInput()">
              Publish reviewed dimension schema
            </button>
          }
        }
        @if (dimensionPublication(); as publication) {
          <p>Published schema {{ publication.schemaId }} · revision {{ publication.revision }} · <code>{{ publication.canonicalDigest }}</code></p>
          <button matButton type="button" (click)="readDimensionRevision()" [disabled]="dimensionBusy()">Resolve this exact published revision</button>
          <button matButton type="button" (click)="usePublishedDimensionBase()" [disabled]="dimensionBusy() || dimensionUnknown() || !schemaAccess.canSaveDimensionSchema">Prepare a successor from this exact revision</button>
        }
        @if (dimensionResolved(); as resolved) {
          <p role="status">Exact published revision verified for this client: {{ resolved.reference.revisionId }} · revision {{ resolved.reference.revision }} · <code>{{ resolved.reference.canonicalDigest }}</code></p>
        }
        @if (dimensionUnknown()) { <p role="alert">The command outcome is unknown. Do not repeat it; the existing schema API has no retained command lookup for this request.</p> }
      </section>
    }
  </section>` })
export class AccountingCharts {
  readonly clientId = input.required<string>(); readonly data = signal<Charts | null>(null); readonly accounts = signal<Accounts | null>(null);
  readonly aliases = signal<Alias[] | null>(null);
  readonly aliasLoading = signal(false); readonly aliasError = signal('');
  readonly aliasesTruncated = signal(false);
  readonly loading = signal(false); readonly error = signal(''); selected = '';
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService);
  private readonly api = inject(Api);
  private request?: Subscription; private detail?: Subscription; private write?: Subscription; private publicationRead?: Subscription;
  private aliasRead?: Subscription;
  private dimensionWrite?: Subscription; private dimensionRead?: Subscription;
  readonly publication = signal<Publication | null>(null); publicationReviewed = false;
  readonly busy = signal(false); readonly uncertain = signal(false);
  sourceScope = ''; effectiveFrom = ''; creationReviewed = false; accountReviewed = false;
  account = { stable: '', code: '', name: '', type: '', balance: 'DEBIT', parent: '', posting: true };
  aliasReviewed = false;
  alias = { accountId: '', sourceSystem: '', code: '', name: '' };
  readonly dimensionDraft = signal<DimensionDraftReceipt | null>(null);
  readonly dimensionPublication = signal<DimensionPublication | null>(null);
  readonly dimensionResolved = signal<ResolvedDimensionRevision | null>(null);
  readonly dimensionBusy = signal(false);
  readonly dimensionUnknown = signal(false);
  readonly dimensionMessage = signal('');
  readonly dimensionError = signal('');
  dimensionDefinitionsJson = '[]';
  dimensionBaseRevisionRef = '';
  dimensionReviewed = false;
  dimensionImpactAcknowledged = false;
  private dimensionDraftBody: DimensionDraftBody | null = null;
  private dimensionSavedInput = '';
  private dimensionSavedBase = '';
  constructor() {
    effect(() => { this.clientId(); this.session.invalidation(); const staff = this.session.current()?.staff;
      untracked(() => { this.request?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.publicationRead?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; this.busy.set(false); this.creationReviewed = false; this.accountReviewed = false; this.aliasReviewed = false; this.aliasLoading.set(false); this.aliasError.set(''); this.aliasesTruncated.set(false); this.sourceScope = ''; this.effectiveFrom = ''; this.account = { stable: '', code: '', name: '', type: '', balance: 'DEBIT', parent: '', posting: true }; this.alias = { accountId: '', sourceSystem: '', code: '', name: '' }; this.data.set(null); this.accounts.set(null); this.aliases.set(null); this.selected = ''; this.loading.set(false); if (staff) this.load(); }); });
    inject(DestroyRef).onDestroy(() => { this.request?.unsubscribe(); this.detail?.unsubscribe(); this.write?.unsubscribe(); this.publicationRead?.unsubscribe(); this.aliasRead?.unsubscribe(); this.publication.set(null); this.publicationReviewed = false; });
    effect(() => {
      this.clientId(); this.session.invalidation(); this.session.current();
      untracked(() => {
        this.dimensionWrite?.unsubscribe(); this.dimensionRead?.unsubscribe();
        this.dimensionDraft.set(null); this.dimensionPublication.set(null); this.dimensionResolved.set(null);
        this.dimensionBusy.set(false); this.dimensionUnknown.set(false); this.dimensionMessage.set(''); this.dimensionError.set('');
        this.dimensionDefinitionsJson = '[]'; this.dimensionBaseRevisionRef = ''; this.dimensionReviewed = false;
        this.dimensionImpactAcknowledged = false; this.dimensionDraftBody = null; this.dimensionSavedInput = ''; this.dimensionSavedBase = '';
      });
    });
    inject(DestroyRef).onDestroy(() => { this.dimensionWrite?.unsubscribe(); this.dimensionRead?.unsubscribe(); });
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

  dimensionInputChanged(): void {
    this.dimensionReviewed = false;
    this.dimensionImpactAcknowledged = false;
    this.dimensionError.set('');
  }

  dimensionDraftMatchesInput(): boolean {
    return !!this.dimensionDraftBody && this.dimensionDefinitionsJson === this.dimensionSavedInput &&
      this.dimensionBaseRevisionRef.trim().toLowerCase() === this.dimensionSavedBase;
  }

  async saveDimensionDraft(): Promise<void> {
    const workspace = this.data();
    if (!workspace?.canSaveDimensionSchema || this.dimensionBusy() || this.dimensionUnknown()) return;
    let definitions: DimensionDefinition[];
    try {
      if (this.dimensionDefinitionsJson.length > 200000) throw new Error('Dimension schema input exceeds the supported size.');
      definitions = dimensionDefinitions(JSON.parse(this.dimensionDefinitionsJson));
    } catch (error) {
      this.dimensionError.set(error instanceof Error ? error.message : 'Enter a valid dimension definitions JSON array.');
      return;
    }
    const base = this.dimensionBaseRevisionRef.trim();
    if (base && !guidPattern.test(base)) { this.dimensionError.set('The published base revision must be an exact GUID.'); return; }
    const current = this.dimensionDraft();
    const body: DimensionDraftBody = {
      baseRevisionRef: base || null,
      definitions,
      ...(current ? { draftRevisionRef: current.draftRevisionRef, expectedCanonicalDigest: current.canonicalDigest } : {}),
    };
    this.dimensionBusy.set(true); this.dimensionMessage.set(''); this.dimensionError.set('');
    const client = this.clientId(), generation = this.session.invalidation();
    try {
      const result = await this.api.command<unknown>(`/api/ui/clients/${client}/dimension-schemas/drafts`, body);
      if (generation !== this.session.invalidation() || client !== this.clientId()) return;
      if (!result.ok) {
        this.dimensionUnknown.set(result.unknown);
        this.dimensionMessage.set(result.message);
        return;
      }
      const receipt = decodeDimensionDraftReceipt(result.value);
      const normalizedBody: DimensionDraftBody = { baseRevisionRef: body.baseRevisionRef, definitions: receipt.definitions,
        draftRevisionRef: receipt.draftRevisionRef, expectedCanonicalDigest: receipt.canonicalDigest };
      this.dimensionDraft.set(receipt);
      this.dimensionDraftBody = normalizedBody;
      this.dimensionBaseRevisionRef = body.baseRevisionRef ?? '';
      this.dimensionDefinitionsJson = JSON.stringify(receipt.definitions, null, 2);
      this.dimensionSavedInput = this.dimensionDefinitionsJson;
      this.dimensionSavedBase = this.dimensionBaseRevisionRef.trim().toLowerCase();
      this.dimensionReviewed = false;
      this.dimensionImpactAcknowledged = false;
      this.dimensionMessage.set('Exact dimension schema draft saved. A different authorized reviewer must publish it.');
    } catch {
      this.dimensionUnknown.set(true);
      this.dimensionMessage.set('The draft response could not be verified. Do not submit the command again.');
    } finally {
      if (generation === this.session.invalidation() && client === this.clientId()) this.dimensionBusy.set(false);
    }
  }

  async publishDimensionDraft(): Promise<void> {
    const workspace = this.data(), receipt = this.dimensionDraft(), body = this.dimensionDraftBody;
    if (!workspace?.canPublishDimensionSchema || !receipt || !body || !this.dimensionReviewed || !this.dimensionImpactAcknowledged ||
        !this.dimensionDraftMatchesInput() || this.dimensionBusy() || this.dimensionUnknown()) return;
    this.dimensionBusy.set(true); this.dimensionMessage.set(''); this.dimensionError.set('');
    const client = this.clientId(), generation = this.session.invalidation();
    try {
      const result = await this.api.command<unknown>(`/api/ui/clients/${client}/dimension-schemas/${receipt.schemaId}/publish`,
        { ...body, impactAcknowledged: true });
      if (generation !== this.session.invalidation() || client !== this.clientId()) return;
      if (!result.ok) {
        this.dimensionUnknown.set(result.unknown);
        this.dimensionMessage.set(result.message);
        this.dimensionReviewed = false;
        return;
      }
      const published = decodeDimensionPublication(result.value);
      if (published.schemaId !== receipt.schemaId || published.revision !== receipt.revision) throw new Error('Published revision mismatch.');
      this.dimensionPublication.set(published);
      this.dimensionDraft.set(null);
      this.dimensionDraftBody = null;
      this.dimensionReviewed = false;
      this.dimensionImpactAcknowledged = false;
      this.dimensionMessage.set('Dimension schema revision published by an independent reviewer.');
      await this.readDimensionRevision(generation, client);
    } catch {
      if (generation === this.session.invalidation() && client === this.clientId()) {
        this.dimensionUnknown.set(true);
        this.dimensionMessage.set('The publication response could not be verified. Do not submit the command again.');
      }
    } finally {
      if (generation === this.session.invalidation() && client === this.clientId()) this.dimensionBusy.set(false);
    }
  }

  async readDimensionRevision(generation = this.session.invalidation(), client = this.clientId()): Promise<void> {
    const publication = this.dimensionPublication();
    if (!publication) return;
    this.dimensionRead?.unsubscribe(); this.dimensionResolved.set(null); this.dimensionError.set('');
    try {
      const reference = await this.api.get<ResolvedDimensionRevision>(
        `/api/ui/clients/${client}/dimension-schemas/${publication.schemaId}/revisions/${publication.revision}` +
        `?revisionId=${publication.schemaRevisionRef}&canonicalDigestVersion=${publication.canonicalDigestVersion}&canonicalDigest=${publication.canonicalDigest}`,
        decodeResolvedDimensionRevision);
      if (generation !== this.session.invalidation() || client !== this.clientId()) return;
      if (reference.clientId !== client || reference.reference.schemaId !== publication.schemaId ||
          reference.reference.revisionId !== publication.schemaRevisionRef || reference.reference.revision !== publication.revision ||
          reference.reference.canonicalDigest !== publication.canonicalDigest) throw new Error('Exact revision identity mismatch.');
      this.dimensionResolved.set(reference);
    } catch (error) {
      if (generation === this.session.invalidation() && client === this.clientId())
        this.dimensionError.set(error instanceof Error ? error.message : 'The exact published revision could not be resolved.');
    }
  }

  usePublishedDimensionBase(): void {
    const publication = this.dimensionPublication();
    if (!publication || this.dimensionBusy() || this.dimensionUnknown() || !this.data()?.canSaveDimensionSchema) return;
    this.dimensionBaseRevisionRef = publication.schemaRevisionRef;
    this.dimensionDefinitionsJson = '[]';
    this.dimensionDraft.set(null); this.dimensionDraftBody = null; this.dimensionSavedInput = ''; this.dimensionSavedBase = '';
    this.dimensionReviewed = false; this.dimensionImpactAcknowledged = false;
    this.dimensionMessage.set('The exact published revision is selected as the successor base. Enter and review the new definitions.');
    this.dimensionInputChanged();
  }

}
