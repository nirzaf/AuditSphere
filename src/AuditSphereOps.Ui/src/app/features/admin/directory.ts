import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api } from '../../core/api';
import { Decoder, bool, decode, guid, instant, nullable, obj, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { SHARED } from '../../core/ui';
import { accessWorkspace } from './access-contracts';
import { directoryCandidate, directoryPage, tenantWorkspace } from './tenant-contracts';
import { RoleAssignmentDialog } from './roles';

type Candidate = ReturnType<typeof directoryCandidate>;
type Query = { prefix: string; domain: string; browse: boolean };
type Attempt = { query: Query; pageToken: string | null };
const identityShape = obj({ tenantId: guid, objectId: guid, guest: bool });
type Identity = ReturnType<typeof identityShape>;
const checkpoint = { entity: 'microsoft-directory-binding', baseRevision: '0'.repeat(64) };
function bindingIdentity(value: unknown): Identity | null {
  try { return value && typeof value === 'object' && Object.keys(value).length === 3 ? identityShape(value, 'identity') : null; } catch { return null; }
}
const bindingShape = obj({ tenantId: guid, objectId: guid, bound: bool, userId: nullable(guid), userKind: nullable(text), localDisabled: nullable(bool),
  verificationStartedAt: nullable(instant), directoryEnabledState: nullable(text), directoryUserType: nullable(text) });
export const decodeDirectoryBinding: Decoder<ReturnType<typeof bindingShape>> = (value, path) => {
  const status = bindingShape(value, path);
  if (status.bound !== !!status.userId || status.bound && (status.userKind === null || status.localDisabled === null) ||
    !status.bound && (status.userKind !== null || status.localDisabled !== null)) throw new Error('Inconsistent local binding state');
  return status;
};
export function decodeDirectoryResults(value: unknown, tenant: string, query: Query, pageToken: string | null) {
  const result = decode(directoryPage, value), ids = new Set<string>(), next = result.nextPageToken;
  if (next !== null && (!next.trim() || next.length > 20000 || /[\u0000-\u001f\u007f]/.test(next) || next === pageToken)) throw new Error('Invalid directory continuation');
  for (const user of result.users) {
    const id = user.objectId.toLowerCase();
    if (user.tenantId.toLowerCase() !== tenant.toLowerCase() || ids.has(id) || id === '00000000-0000-0000-0000-000000000000' ||
      !user.displayName.trim() || !user.userPrincipalName.trim() || query.browse && !user.accountEnabled ||
      query.domain && !user.userPrincipalName.toLowerCase().endsWith('@' + query.domain)) throw new Error('Invalid directory identity page');
    ids.add(id);
  }
  return result;
}
const sameIdentity = (a: Identity, b: { tenantId: string; objectId: string }) => a.tenantId === b.tenantId && a.objectId === b.objectId;

@Component({ selector: 'audit-microsoft-directory', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <section class="panel"><h2>Microsoft directory</h2><p>Search up to 25 identities per page. Binding verifies immutable tenant and object IDs and grants no local access.</p>
    <button matButton="outlined" (click)="refresh()">Refresh directory configuration</button>
    <audit-state [loading]="settings.loading()" [error]="settings.error()" label="directory configuration" />
    @if (settings.data(); as w) {
      @if (parentAccess() === null) { <p role="status">Current firm access is loading or unavailable. Directory actions are blocked.</p> }
      @else if (!w.directoryConfigured || !w.configuredTenantId) { <p role="status">BLOCKED_EXTERNAL: configure the separately consented directory reader before searching Microsoft identities.</p> }
      @else {
        <form [formGroup]="form" (ngSubmit)="search(false)"><label for="directory-prefix">Display name or user principal name prefix</label><input id="directory-prefix" formControlName="prefix" maxlength="80" />
          <label for="directory-domain">Domain filter (optional)</label><input id="directory-domain" formControlName="domain" maxlength="253" />
          <div class="actions"><button matButton="filled" [disabled]="busy() || form.controls.prefix.value.trim().length < 2">Search Microsoft directory</button>
            <button type="button" matButton (click)="search(true)" [disabled]="busy()">Browse active identities</button></div></form>
        @if (applied(); as q) { <p role="status">Showing {{ q.browse ? 'active identities' : 'prefix ' + q.prefix }} · UPN domain {{ q.domain || 'All domains' }} · tenant {{ w.configuredTenantId }}</p> }
        @if (page(); as p) { <div class="table-scroll"><table><caption>Microsoft identities in the configured tenant</caption><thead><tr><th>Name</th><th>Microsoft identity</th><th>Account type</th><th>Status</th><th>Action</th></tr></thead><tbody>
          @for(u of p.users; track u.objectId) { <tr><td>{{ u.displayName }}</td><td>{{ u.userPrincipalName }}<br />{{ u.objectId }}</td><td>{{ u.userType }}</td><td>{{ u.accountEnabled ? 'Enabled' : 'Disabled' }}</td><td>
            <button matButton (click)="select(u)" [disabled]="busy() || uncertain() || !eligible(u)">Review {{ u.displayName }}</button></td></tr> }
          @empty { <tr><td colspan="5">No matching identities on this page.</td></tr> }
        </tbody></table></div>@if(p.nextPageToken) { <button matButton (click)="next()" [disabled]="busy() || !!retryAttempt()">Next 25 identities</button> } }
        @if (retryAttempt()) { <p role="alert">This directory page was not loaded. Retry keeps the same applied filters and cursor.</p><button matButton (click)="retryPage()" [disabled]="busy()">Retry directory page</button> }
        @if(selected(); as s) { <section class="panel"><h3>Review existing {{ s.userType === 'Guest' ? 'guest' : 'member' }}</h3><p>{{ s.displayName }} · {{ s.userPrincipalName }}</p><p>Tenant {{ s.tenantId }} · object {{ s.objectId }}</p>
          <p>{{ s.userType === 'Guest' ? 'ClientUser requires CLIENT or ENGAGEMENT scope.' : 'Microsoft membership does not grant AuditSphere roles.' }}</p>
          <label><input type="checkbox" [formControl]="reviewed" [attr.disabled]="busy() || uncertain() ? '' : null" /> I reviewed this exact Microsoft identity and account type.</label>
          <button matButton="filled" (click)="bind()" [disabled]="busy() || uncertain() || !reviewed.value">Verify and bind selected identity</button></section> }
        @if(boundId(); as id) { <p role="status">Identity verified and bound locally. No new AuditSphere role has been granted.</p><button matButton="filled" (click)="assign(id)" [disabled]="busy() || uncertain()">Assign reviewed role and scope</button> }
        @if (uncertain()) {
          <section class="panel" aria-label="Directory binding outcome review"><h3>Binding outcome is not confirmed</h3>
            <p>No binding will be retried automatically. Current local state is not an original-request receipt.</p>
            @if (pendingBinding(); as p) { <p>Tenant {{ p.tenantId }} · object {{ p.objectId }} · {{ p.guest ? 'Guest' : 'Member' }}</p>
              <button matButton (click)="inspectBinding()" [disabled]="busy() || p.tenantId !== w.configuredTenantId">Inspect current local binding</button> }
            @else { <p>Tab checkpoint storage is unavailable. Restore it and refresh before sending another binding.</p> }
            @if (inspection(); as s) {
              <p>Current local binding: {{ s.bound ? 'Present' : 'Not found at read time' }} · local user {{ s.userId ?? 'None' }} · {{ s.userKind ?? 'No local kind' }}</p>
              <p>Local disabled: {{ s.localDisabled === null ? 'Not applicable' : s.localDisabled ? 'Yes' : 'No' }} · directory observation {{ s.directoryEnabledState ?? 'None' }} / {{ s.directoryUserType ?? 'None' }} · verification started {{ s.verificationStartedAt ?? 'Not observed' }}</p>
              <p>This read does not prove the original command succeeded or can never commit. The binding remains blocked; no receipt/retry contract exists for that original request.</p>
            }
          </section>
        }
      }
    }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </section>
`, styles: ['p, td { overflow-wrap: anywhere; }'] })
export class MicrosoftDirectory {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly dialog = inject(MatDialog); private readonly fb = inject(FormBuilder);
  private readonly drafts = inject(TabDrafts);
  readonly parentAccess = input<ReturnType<typeof accessWorkspace> | null | undefined>(undefined);
  readonly changed = output<void>();
  readonly settings = this.api.resource(() => '/api/ui/administration/microsoft365', tenantWorkspace);
  readonly form = this.fb.nonNullable.group({ prefix: ['', Validators.maxLength(80)], domain: ['', Validators.maxLength(253)] }); readonly reviewed = this.fb.nonNullable.control(false);
  readonly page = signal<ReturnType<typeof directoryPage> | null>(null); readonly selected = signal<Candidate | null>(null); readonly boundId = signal<string | null>(null);
  readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  readonly applied = signal<Query | null>(null); readonly retryAttempt = signal<Attempt | null>(null);
  readonly pendingBinding = signal<Identity | null>(null); private readonly storageUnavailable = signal(false);
  readonly uncertain = computed(() => !!this.pendingBinding() || this.storageUnavailable());
  readonly inspection = signal<ReturnType<typeof decodeDirectoryBinding> | null>(null);
  private boundIdentity: Identity | null = null;
  private generation = 0; private operation = 0; private destroyed = false; private actorKey = '';
  private roleDialog: ReturnType<MatDialog['open']> | null = null;
  constructor() {
    const changes = this.form.valueChanges.subscribe(() => this.invalidate(true));
    const navigation = inject(Router).events.subscribe(event => { if (event instanceof NavigationStart) this.invalidate(true); });
    effect(() => {
      const settings = this.settings.data(), actor = this.session.current(), epoch = this.session.invalidation(), parent = this.parentAccess();
      untracked(() => {
        const key = `${actor?.firmId}/${actor?.userId}/${actor?.generation}/${actor?.staff}/${epoch}`;
        if (key !== this.actorKey) { this.actorKey = key; this.pendingBinding.set(null); this.storageUnavailable.set(false); this.boundId.set(null); this.boundIdentity = null; this.form.reset({}, { emitEvent: false }); }
        this.invalidate(false);
        if (settings && (!settings.directoryConfigured || settings.configuredTenantId !== this.boundIdentity?.tenantId) && this.boundIdentity) { this.boundId.set(null); this.boundIdentity = null; }
        if (parent && this.boundIdentity && !this.localUser(parent, this.boundId()!, this.boundIdentity)) { this.boundId.set(null); this.boundIdentity = null; }
        if (this.ready()) {
          const saved = this.drafts.read(checkpoint, bindingIdentity);
          if (saved.state === 'ready' && saved.draft.submissionPending) this.pendingBinding.set(saved.draft.value);
          this.storageUnavailable.set(saved.state === 'unavailable');
        }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.invalidate(true); changes.unsubscribe(); navigation.unsubscribe(); });
  }
  private invalidate(clearBound: boolean): void {
    ++this.generation; ++this.operation; this.busy.set(false); this.page.set(null); this.selected.set(null); this.reviewed.setValue(false);
    this.applied.set(null); this.retryAttempt.set(null); this.inspection.set(null); this.message.set(''); this.failed.set(false);
    if (clearBound) { this.boundId.set(null); this.boundIdentity = null; }
    this.roleDialog?.close(false); this.roleDialog = null;
  }
  ready(): boolean { return !this.destroyed && !!this.session.current()?.staff && this.parentAccess() !== null &&
    !!this.settings.data()?.directoryConfigured && !!this.settings.data()?.configuredTenantId; }
  private ticket() {
    const generation = this.generation, settings = this.settings.data(), actor = this.session.current(), epoch = this.session.invalidation(), parent = this.parentAccess();
    return () => this.ready() && generation === this.generation && settings === this.settings.data() && actor === this.session.current() && epoch === this.session.invalidation() && parent === this.parentAccess();
  }
  eligible(user: Candidate): boolean { return this.ready() && user.tenantId === this.settings.data()?.configuredTenantId && user.accountEnabled &&
    (user.userType === 'Member' || user.userType === 'Guest') && !!this.page()?.users.includes(user); }
  select(user: Candidate): void { if (this.busy() || this.uncertain() || !this.eligible(user)) return; this.selected.set(user); this.boundId.set(null); this.boundIdentity = null; this.reviewed.setValue(false); }
  refresh(): void { if (this.destroyed) return; this.invalidate(true); this.settings.reload(); }
  async search(browse: boolean): Promise<void> {
    if (!this.ready() || this.busy()) return;
    const raw = this.form.getRawValue(), prefix = raw.prefix.trim(), domain = raw.domain.trim().replace(/^@+/, '').toLowerCase();
    if (this.form.invalid || !browse && (prefix.length < 2 || /[\u0000-\u001f\u007f]/.test(prefix)) ||
      domain && (domain.length > 253 || !domain.includes('.') || domain.startsWith('.') || domain.endsWith('.') || /[^a-z0-9.-]/.test(domain))) {
      this.failed.set(true); this.message.set('Enter at least two prefix characters for search and a valid optional UPN domain.'); return;
    }
    const query = { prefix, domain, browse }; this.applied.set(query); await this.load({ query, pageToken: null });
  }
  async next(): Promise<void> { const query = this.applied(), token = this.page()?.nextPageToken; if (query && token && !this.retryAttempt()) await this.load({ query, pageToken: token }); }
  async retryPage(): Promise<void> { const attempt = this.retryAttempt(); if (attempt) await this.load(attempt); }
  private async load(attempt: Attempt): Promise<void> {
    if (!this.ready() || this.busy() || this.applied() !== attempt.query) return;
    const valid = this.ticket(), operation = ++this.operation, tenant = this.settings.data()!.configuredTenantId!, previous = this.page();
    this.busy.set(true); this.selected.set(null); this.reviewed.setValue(false); this.boundId.set(null); this.boundIdentity = null; this.retryAttempt.set(null); this.message.set(''); this.failed.set(false);
    if (!attempt.pageToken) this.page.set(null);
    try {
      const result = await this.api.command('/api/ui/administration/directory/search', { ...attempt.query, pageToken: attempt.pageToken });
      if (!valid()) return;
      if (!result.ok) {
        const transient = result.unknown || result.status === 429;
        this.page.set(transient ? previous : null); this.retryAttempt.set(transient ? attempt : null); this.failed.set(true); this.message.set(result.message); return;
      }
      this.page.set(decodeDirectoryResults(result.value, tenant, attempt.query, attempt.pageToken));
    } catch { if (valid()) { this.page.set(null); this.retryAttempt.set(null); this.failed.set(true); this.message.set('The directory response could not be verified. Start a fresh search.'); } }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  private localUser(workspace: ReturnType<typeof accessWorkspace>, id: string, identity: Identity) {
    return [...workspace.users, ...workspace.disabledOrRevoked].find(user => user.userId === id && user.tenantId === identity.tenantId && user.objectId === identity.objectId &&
      user.auditSphereStatus !== 'DISABLED' && user.accountType.startsWith(identity.guest ? 'Client (' : 'Staff ('));
  }
  private bindingUrl(identity: Identity) { return `/api/ui/administration/directory/binding?tenantId=${identity.tenantId}&objectId=${identity.objectId}`; }
  async bind(): Promise<void> {
    const user = this.selected();
    if (!user || !this.ready() || !this.eligible(user) || !this.reviewed.value || this.busy() || this.uncertain()) return;
    const identity = { tenantId: user.tenantId, objectId: user.objectId, guest: user.userType === 'Guest' };
    if (!this.drafts.save(checkpoint, identity, bindingIdentity, true)) { this.failed.set(true); this.message.set('The binding checkpoint could not be saved. No binding was sent.'); return; }
    const valid = this.ticket(), operation = ++this.operation; let changed = false;
    this.pendingBinding.set(identity); this.busy.set(true); this.reviewed.setValue(false); this.message.set('');
    try {
      const result = await this.api.command('/api/ui/administration/directory/bind', { objectId: user.objectId, guest: identity.guest, reviewed: true });
      if (!valid()) return;
      if (!result.ok) {
        this.failed.set(true); this.message.set(result.message);
        if (!result.unknown && this.drafts.clear(checkpoint.entity)) this.pendingBinding.set(null);
        if (result.status === 401 || result.status === 403 || result.status === 404) { this.page.set(null); this.selected.set(null); this.reviewed.setValue(false); }
        return;
      }
      const id = decode(guid, result.value), status = await this.api.get(this.bindingUrl(identity), decodeDirectoryBinding);
      if (!valid()) return;
      if (!sameIdentity(identity, status) || !status.bound || status.userId !== id || status.localDisabled !== false || status.userKind !== (identity.guest ? 'Client' : 'Staff') ||
        status.directoryEnabledState !== 'ENABLED' || status.directoryUserType !== (identity.guest ? 'Guest' : 'Member')) throw new Error('Wrong current binding');
      if (!this.drafts.clear(checkpoint.entity)) throw new Error('Checkpoint unavailable');
      this.pendingBinding.set(null); this.boundIdentity = identity; this.boundId.set(id); this.selected.set(null); this.failed.set(false); changed = true;
    } catch { if (valid()) { this.failed.set(true); this.message.set('The binding outcome or current identity could not be verified. Inspect its exact local state; no binding will be retried.'); } }
    finally { if (operation === this.operation) { this.busy.set(false); if (changed && valid()) this.changed.emit(); } }
  }
  async inspectBinding(): Promise<void> {
    const identity = this.pendingBinding();
    if (!identity || !this.ready() || identity.tenantId !== this.settings.data()?.configuredTenantId || this.busy()) return;
    const valid = this.ticket(), operation = ++this.operation; this.busy.set(true); this.inspection.set(null);
    try {
      const status = await this.api.get(this.bindingUrl(identity), decodeDirectoryBinding);
      if (!valid()) return;
      if (!sameIdentity(identity, status) || status.bound && status.userKind !== (identity.guest ? 'Client' : 'Staff')) throw new Error('Wrong local identity');
      this.inspection.set(status); this.failed.set(false); this.message.set('Current local state read. The original binding outcome remains unconfirmed.');
    } catch { if (valid()) { this.failed.set(true); this.message.set('Current binding state could not be verified. The retained identity remains blocked.'); this.page.set(null); this.selected.set(null); } }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
  async assign(id: string): Promise<void> {
    const identity = this.boundIdentity;
    if (!identity || id !== this.boundId() || !this.ready() || this.busy() || this.uncertain()) return;
    const valid = this.ticket(), operation = ++this.operation; this.busy.set(true);
    try {
      const workspace = await this.api.get('/api/ui/administration/access', accessWorkspace);
      if (!valid()) return;
      const user = this.localUser(workspace, id, identity); if (!user) throw new Error('Current identity unavailable');
      const ref = this.dialog.open(RoleAssignmentDialog, { data: { user, workspace }, width: '48rem', maxWidth: '96vw', autoFocus: 'first-heading' });
      this.roleDialog = ref;
      ref.afterClosed().subscribe(changed => { if (valid() && changed) this.changed.emit(); if (this.roleDialog === ref) this.roleDialog = null; });
    } catch { if (valid()) { this.boundId.set(null); this.boundIdentity = null; this.failed.set(true); this.message.set('Current local access could not be verified. Refresh before selecting this identity again.'); } }
    finally { if (operation === this.operation) this.busy.set(false); }
  }
}
