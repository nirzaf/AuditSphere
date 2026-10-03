import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormField, form, maxLength } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, DraftScope, pendingRequestReference, PendingRequestReference } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { decode } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { setupPreview, setupReceipt, setupReceiptLookup, SetupPreview, SetupReceipt, storedSetupFields } from './tenant-setup-contracts';

@Component({ selector: 'audit-tenant-setup-editor', imports: [FormField, MatButtonModule, ...SHARED], template: `
<section class="panel" aria-labelledby="setup-edit-title"><h2 id="setup-edit-title">Edit local setup configuration</h2>
<p>This changes the setup label and configuration flags only. Microsoft consent, mail transport and records acceptance require separate verification.</p>
@if (!editable()) { <p>Protected setup requires a separately reviewed replacement draft.</p> }
@else {
<fieldset [disabled]="busy() || !!pending() || !!receipt()">
<label>Tenant label<input [formField]="fields.name" /></label>
<label>Mail setup<select [formField]="fields.mail"><option value="NOT_CONFIGURED">Not configured</option><option value="CONFIGURED">Configured</option></select></label>
<label>Records setup<select [formField]="fields.records"><option value="NOT_CONFIGURED">Not configured</option><option value="CONFIGURED">Configured</option></select></label>
<button matButton (click)="review()" [disabled]="fields().invalid()">Review exact changes</button>
<button matButton (click)="saveDraft()">Save tab draft</button><button matButton (click)="restoreDraft()">Recover tab draft</button>
</fieldset>
}
@if(preview(); as p) {
<h3>Review setup revision {{ p.expectedRevision }}</h3>
<table><caption>Current and proposed local metadata</caption><thead><tr><th>Field</th><th>Current</th><th>Proposed</th></tr></thead><tbody>
<tr><th>Label</th><td>{{ p.before.tenantDisplayName ?? 'None' }}</td><td>{{ p.fields.tenantDisplayName ?? 'None' }}</td></tr>
<tr><th>Mail</th><td>{{ p.before.mailState }}</td><td>{{ p.fields.mailState }}</td></tr>
<tr><th>Records</th><td>{{ p.before.recordsState }}</td><td>{{ p.fields.recordsState }}</td></tr></tbody></table>
<label><input type="checkbox" [formField]="assent.reviewed" /> I reviewed these exact local changes.</label>
<button matButton="filled" (click)="save()" [disabled]="!assentModel().reviewed || busy() || !!pending()">Save reviewed setup</button>
}
@if(pending()) { <p role="status">Submission requires reconciliation. No automatic resend occurs.</p><button matButton (click)="lookup()" [disabled]="busy()">Check saved receipt</button> }
@if(receipt(); as r) { <p role="status">Saved local metadata at revision {{ r.appliedRevision }}. Receipt {{ r.id }}.</p><button matButton (click)="acknowledge()">Acknowledge receipt and refresh</button> }
<audit-command-message [message]="message()" [failed]="failed()" />
</section>` })
export class TenantSetupEditor {
  readonly draftId = input.required<string>(); readonly revision = input.required<string>();
  readonly initial = input.required<{tenantDisplayName: string | null; mailState: string; recordsState: string}>();
  readonly editable = input(false); readonly changed = output<void>();
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  private readonly drafts = inject(TabDrafts); private readonly dialog = inject(MatDialog);
  private alive = true; private version = 0;
  readonly model = signal({ name: '', mail: 'NOT_CONFIGURED', records: 'NOT_CONFIGURED' });
  readonly fields = form(this.model, p => maxLength(p.name, 300));
  readonly assentModel = signal({ reviewed: false }); readonly assent = form(this.assentModel);
  readonly preview = signal<SetupPreview | null>(null); readonly receipt = signal<SetupReceipt | null>(null);
  readonly pending = signal<PendingRequestReference | null>(null); readonly scope = signal<DraftScope | null>(null);
  readonly busy = signal(false); readonly message = signal(''); readonly failed = signal(false);
  private readonly baseline = signal('');
  readonly dirty = computed(() => JSON.stringify(this.model()) !== this.baseline());
  constructor() {
    effect(() => {
      const id = this.draftId(), rev = this.revision(), initial = this.initial(); this.session.invalidation();
      const version = ++this.version;
      untracked(() => {
        this.model.set({name: initial.tenantDisplayName ?? '', mail: initial.mailState, records: initial.recordsState});
        this.baseline.set(JSON.stringify(this.model())); this.preview.set(null); this.receipt.set(null); this.pending.set(null);
        this.scope.set(null); this.assentModel.set({reviewed: false}); this.busy.set(false); this.message.set('');
      });
      void crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify([id, rev, initial]))).then(bytes => {
        if (!this.alive || version !== this.version) return;
        const baseRevision = Array.from(new Uint8Array(bytes), x => x.toString(16).padStart(2, '0')).join('');
        const scope = {entity: `tenant-setup:${id}`, baseRevision}; this.scope.set(scope);
        const recovered = this.drafts.readPendingRequest(scope);
        if (recovered.state === 'ready') this.pending.set(recovered.draft.value);
      });
    });
    effect(() => { this.model(); untracked(() => { this.preview.set(null); this.assentModel.set({reviewed: false}); }); });
    const unload = (event: BeforeUnloadEvent) => { if (this.dirty() || this.pending()) { event.preventDefault(); event.returnValue = ''; } };
    window.addEventListener('beforeunload', unload);
    inject(DestroyRef).onDestroy(() => { this.alive = false; ++this.version; window.removeEventListener('beforeunload', unload); });
  }
  private values() { const m = this.model(); return {tenantDisplayName: m.name.trim() || null, mailState: m.mail, recordsState: m.records}; }
  private valid(version: number, epoch: number) { return this.alive && version === this.version && epoch === this.session.invalidation(); }
  async review() {
    if (!this.editable() || this.busy() || this.pending() || this.receipt() || this.fields().invalid()) return;
    const v = this.version, e = this.session.invalidation(), snapshot = JSON.stringify(this.model()), requestId = crypto.randomUUID(); this.busy.set(true);
    try {
      const result = await this.api.command('/api/ui/administration/microsoft365/setup/preview', {
        requestId, draftId: this.draftId(), expectedRevision: this.revision(), fields: this.values()});
      if (!this.valid(v,e) || snapshot !== JSON.stringify(this.model())) return;
      if (!result.ok) { this.message.set(result.message); this.failed.set(true); return; }
      const p = decode(setupPreview, result.value);
      if (p.requestId !== requestId || p.draftId !== this.draftId() || p.expectedRevision !== this.revision() || JSON.stringify(p.fields) !== JSON.stringify(this.values())) throw new Error();
      this.preview.set(p); this.assentModel.set({reviewed:false}); this.message.set('Review the exact before and after values.'); this.failed.set(false);
    } catch { if(this.valid(v,e)) { this.failed.set(true); this.message.set('The exact review could not be verified. Refresh before reviewing again.'); } }
    finally { if(this.valid(v,e)) this.busy.set(false); }
  }
  async save() {
    const p = this.preview(), scope = this.scope();
    if (!p || !scope || !this.editable() || !this.assentModel().reviewed || this.busy() || this.pending() || this.receipt()) return;
    const reference = {requestId:p.requestId, requestHash:p.requestHash};
    if (!this.drafts.save(scope, reference, pendingRequestReference, true)) { this.failed.set(true); this.message.set('Recovery storage is unavailable. No setup command was sent.'); return; }
    this.pending.set(reference); this.busy.set(true); const v=this.version,e=this.session.invalidation();
    try {
      const result = await this.api.command('/api/ui/administration/microsoft365/setup/commands', {...p, reviewed:true});
      if(!this.valid(v,e)) return;
      if(result.ok) { const r=decode(setupReceipt,result.value); if(r.previousFingerprint !== p.reviewBasis || BigInt(r.appliedRevision) !== BigInt(p.expectedRevision)+1n) throw new Error(); this.accept(r,reference); }
      else { this.failed.set(true); this.message.set(result.message); }
    } catch { if(this.valid(v,e)) this.message.set('The outcome is unconfirmed. Check the saved receipt before another action.'); }
    finally { if(this.valid(v,e)) this.busy.set(false); }
  }
  private accept(r: SetupReceipt, reference: PendingRequestReference) {
    if(r.requestId!==reference.requestId || r.requestHash!==reference.requestHash || r.draftId!==this.draftId()) throw new Error();
    this.receipt.set(r); this.preview.set(null); this.assentModel.set({reviewed:false}); this.failed.set(false); this.message.set('Immutable local setup receipt verified.');
  }
  async lookup() {
    const ref=this.pending(); if(!ref || this.busy()) return;
    const v=this.version,e=this.session.invalidation(); this.busy.set(true);
    try { const result=await this.api.get(`/api/ui/administration/microsoft365/setup/receipts/${ref.requestId}?requestHash=${ref.requestHash}`,setupReceiptLookup);
      if(!this.valid(v,e)) return;
      if(result.receipt) this.accept(result.receipt,ref);
      else if(this.scope() && this.drafts.clear(this.scope()!.entity)) { this.pending.set(null); this.preview.set(null); this.assentModel.set({reviewed:false}); this.message.set('No saved receipt exists. Refresh current setup and obtain a fresh review.'); this.changed.emit(); }
    } catch { if(this.valid(v,e)) { this.failed.set(true); this.message.set('Receipt lookup could not establish the outcome. Keep the pending reference and retry lookup.'); } }
    finally { if(this.valid(v,e)) this.busy.set(false); }
  }
  acknowledge() { const scope=this.scope(); if(!scope || !this.receipt() || !this.drafts.clear(scope.entity)) return;
    this.pending.set(null); this.receipt.set(null); this.baseline.set(JSON.stringify(this.model())); this.changed.emit(); }
  saveDraft(): boolean { const scope=this.scope(); if(!scope || this.pending() || this.receipt() || this.busy()) return false;
    const saved=this.drafts.save(scope,this.values(),storedSetupFields); this.message.set(saved?'Unsubmitted tab draft saved. Review assent is not retained.':'Tab draft could not be saved.'); this.failed.set(!saved); return saved; }
  restoreDraft() { const scope=this.scope(); if(!scope || this.pending() || this.busy() || this.receipt()) return;
    const r=this.drafts.read(scope,storedSetupFields); if(r.state==='ready' && !r.draft.submissionPending) { const f=r.draft.value; this.model.set({name:f.tenantDisplayName??'',mail:f.mailState,records:f.recordsState}); this.preview.set(null); this.assentModel.set({reviewed:false}); }
    else this.message.set('No compatible unsubmitted tab draft is available.'); }
  async confirmNavigation(): Promise<boolean> {
    if(this.busy()) return false; if(!this.dirty() || this.pending()) return true;
    const choice=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if(choice==='save') return this.saveDraft();
    if(choice==='discard') { const scope=this.scope(); return !scope || this.drafts.clear(scope.entity); }
    return false;
  }
}
