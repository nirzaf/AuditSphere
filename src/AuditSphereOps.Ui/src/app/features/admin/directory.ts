import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api } from '../../core/api';
import { decode, guid } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { accessWorkspace } from './access-contracts';
import { directoryCandidate, directoryPage, tenantWorkspace } from './tenant-contracts';
import { RoleAssignmentDialog } from './roles';

@Component({ selector: 'audit-microsoft-directory', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <section class="panel"><h2>Microsoft directory</h2><p>Search up to 25 identities per page. Selecting an existing member or guest verifies immutable tenant and object IDs. Binding grants no local access.</p>
    <audit-state [loading]="settings.loading()" [error]="settings.error()" label="directory configuration" />
    @if (settings.data(); as w) {
      @if(!w.directoryConfigured) { <p role="status">BLOCKED_EXTERNAL: configure and verify the separately consented directory reader before searching Microsoft identities.</p> }
      @else {
        <form [formGroup]="form" (ngSubmit)="search(false)"><label for="directory-prefix">Display name or user principal name prefix</label><input id="directory-prefix" formControlName="prefix" maxlength="80" />
          <label for="directory-domain">Domain filter (optional)</label><input id="directory-domain" formControlName="domain" maxlength="253" />
          <div class="actions"><button matButton="filled" [disabled]="busy() || form.controls.prefix.value.trim().length < 2">Search Microsoft directory</button>
            <button type="button" matButton (click)="search(true)" [disabled]="busy()">Browse active identities</button></div></form>
        @if(page(); as p) { <div class="table-scroll"><table><caption>Microsoft identities in the configured tenant</caption><thead><tr><th>Name</th><th>Microsoft identity</th><th>Account type</th><th>Status</th><th>Action</th></tr></thead><tbody>
          @for(u of p.users; track u.objectId) { <tr><td>{{ u.displayName }}</td><td>{{ u.userPrincipalName }}<br />{{ u.objectId }}</td><td>{{ u.userType }}</td><td>{{ u.accountEnabled ? 'Enabled' : 'Disabled' }}</td><td>
            <button matButton (click)="select(u)" [disabled]="busy() || uncertain() || !u.accountEnabled || (u.userType !== 'Member' && u.userType !== 'Guest')">Review {{ u.displayName }}</button></td></tr> }
          @empty { <tr><td colspan="5">No matching identities on this page.</td></tr> }
        </tbody></table></div>@if(p.nextPageToken) { <button matButton (click)="next()" [disabled]="busy()">Next 25 identities</button> } }
        @if(selected(); as s) { <section class="panel"><h3>Review existing {{ s.userType === 'Guest' ? 'guest' : 'member' }}</h3><p>{{ s.displayName }} · {{ s.userPrincipalName }}</p><p>Tenant {{ s.tenantId }} · object {{ s.objectId }}</p>
          <p>{{ s.userType === 'Guest' ? 'ClientUser requires CLIENT or ENGAGEMENT scope.' : 'Microsoft membership does not grant AuditSphere roles.' }}</p>
          <label><input type="checkbox" [formControl]="reviewed" /> I reviewed this exact Microsoft identity and account type.</label>
          <button matButton="filled" (click)="bind()" [disabled]="busy() || uncertain() || !reviewed.value">Verify and bind selected identity</button></section> }
        @if(boundId(); as id) { <p role="status">Identity verified and bound locally. No new AuditSphere role has been granted.</p><button matButton="filled" (click)="assign(id)" [disabled]="busy()">Assign reviewed role and scope</button> }
      }
    }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </section>
` })
export class MicrosoftDirectory {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly dialog = inject(MatDialog); private readonly fb = inject(FormBuilder);
  readonly settings = this.api.resource(() => '/api/ui/administration/microsoft365', tenantWorkspace);
  readonly form = this.fb.nonNullable.group({ prefix: ['', Validators.maxLength(80)], domain: ['', Validators.maxLength(253)] }); readonly reviewed = this.fb.nonNullable.control(false);
  readonly page = signal<ReturnType<typeof directoryPage> | null>(null); readonly selected = signal<ReturnType<typeof directoryCandidate> | null>(null); readonly boundId = signal<string | null>(null);
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  private applied: { prefix: string; domain: string; browse: boolean } | null = null;
  constructor() { effect(() => { if (!this.settings.data()) { this.page.set(null); this.selected.set(null); this.boundId.set(null); this.message.set(''); this.form.reset(); this.applied = null; } }); }
  select(user: ReturnType<typeof directoryCandidate>): void { this.selected.set(user); this.boundId.set(null); this.reviewed.setValue(false); }
  search(browse: boolean): Promise<void> { if(this.busy() || this.form.invalid) return Promise.resolve(); this.applied = { ...this.form.getRawValue(), browse }; return this.load(null); }
  next(): Promise<void> { return this.load(this.page()?.nextPageToken ?? null); }
  private async load(pageToken: string | null): Promise<void> {
    if(!this.applied || this.busy() || !this.settings.data()?.directoryConfigured) return; this.busy.set(true); const generation = this.session.invalidation();
    this.page.set(null); this.selected.set(null); this.boundId.set(null); this.message.set('');
    try { const r = await this.api.command('/api/ui/administration/directory/search', { ...this.applied, pageToken }); if(generation !== this.session.invalidation()) return;
      this.failed.set(!r.ok); if(r.ok) this.page.set(decode(directoryPage, r.value)); else this.message.set(r.message);
    } catch { this.failed.set(true); this.message.set('The directory response could not be verified.'); } finally { this.busy.set(false); }
  }
  async bind(): Promise<void> {
    const u = this.selected(); if(!u || !this.reviewed.value || this.busy() || this.uncertain()) return; this.busy.set(true); const generation = this.session.invalidation();
    try { const r = await this.api.command('/api/ui/administration/directory/bind', { objectId: u.objectId, guest: u.userType === 'Guest', reviewed: true });
      if(generation !== this.session.invalidation()) return; this.failed.set(!r.ok); if(r.ok) { this.boundId.set(decode(guid, r.value)); this.selected.set(null); } else { this.message.set(r.message); if(r.unknown) this.uncertain.set(true); }
    } catch { this.failed.set(true); this.message.set('The binding outcome could not be verified. Refresh local users before repeating.'); this.uncertain.set(true); } finally { this.busy.set(false); }
  }
  async assign(id: string): Promise<void> {
    if(this.busy()) return; this.busy.set(true); const generation = this.session.invalidation();
    try { const workspace = await this.api.get('/api/ui/administration/access', accessWorkspace); if(generation !== this.session.invalidation()) return;
      const user = [...workspace.users, ...workspace.disabledOrRevoked].find(u => u.userId === id); if(!user) { this.message.set('Refresh Users & Access to review this identity.'); return; }
      this.dialog.open(RoleAssignmentDialog, { data: { user, workspace }, width: '48rem', maxWidth: '96vw', autoFocus: 'first-heading' });
    } catch { this.failed.set(true); this.message.set('Current local access could not be loaded.'); } finally { this.busy.set(false); }
  }
}
