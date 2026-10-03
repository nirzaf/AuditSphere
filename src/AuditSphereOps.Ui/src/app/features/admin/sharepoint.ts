import { Component, DestroyRef, effect, inject, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts, DraftScope } from '../../core/tab-drafts';
import { UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { currentVerification, editableResource, selectedResources } from './sharepoint-contracts';

@Component({ selector: 'audit-sharepoint-administration', imports: [ReactiveFormsModule, MatButtonModule, ...SHARED], template: `
  <section class="panel" aria-labelledby="selected-sharepoint-heading"><h2 id="selected-sharepoint-heading">Selected SharePoint workspace</h2>
    <p>Sites.Selected remains the document boundary. Consent, site permission, library access and workspace activation are separate checks.</p>
    <button matButton (click)="refresh()" [disabled]="busy()">Refresh resource and template state</button>
    <audit-state [loading]="ws.loading()" [error]="ws.error()" label="Selected SharePoint workspace" />
    @if(ws.data(); as w) {
      @if(w.workspace.draft; as d) {
        <p>Tenant {{ d.tenantId ?? 'Not prepared' }} · draft revision {{ d.revision }} · <audit-status [value]="d.state" /></p>
        <p>Application consent: {{ d.consentState ?? 'NOT_VERIFIED' }}</p>
        @if(w.workspace.verification; as v) { <p>Selected-site boundary: <audit-status [value]="currentVerification(v.state, v.observedAt) ? 'VERIFIED' : v.state === 'VERIFIED' ? 'STALE' : v.state" /> · {{ v.observedAt }} · {{ v.diagnosticCode }}</p> }
        @else { <p>Selected-site boundary: NOT_VERIFIED</p> }
        <form [formGroup]="resourceForm" (ngSubmit)="saveResource()">
          <label>Selected site URL<input formControlName="siteUrl" type="url" maxlength="2000" /></label>
          <label>Site ID<input formControlName="siteId" maxlength="2000" /></label>
          <label>Library / drive ID<input formControlName="driveId" maxlength="2000" /></label>
          <label>Root folder ID<input formControlName="rootFolderId" maxlength="2000" /></label>
          <label>Storage access profile<select formControlName="accessProfile"><option value="APP_MEDIATED">App mediated</option><option value="DIRECT_STAFF_COLLABORATION">Direct staff collaboration</option></select></label>
          <p>Direct collaboration requires its separate human ACL review. Saving these IDs grants no staff or client access.</p>
          @if(editableResource(d.state)) {
            <label><input type="checkbox" formControlName="reviewed" /> I reviewed the exact site, library, root and access profile.</label>
            <button matButton type="button" (click)="saveResourceDraft()" [disabled]="!resourceScope() || busy() || uncertain()">Save resource tab draft</button>
            <button matButton type="button" (click)="recoverResourceDraft()" [disabled]="!resourceScope() || busy() || uncertain()">Recover resource tab draft</button>
            <button matButton="filled" [disabled]="resourceForm.invalid || busy() || uncertain()">Save resource draft</button>
          } @else { <p>Active or protected configurations cannot be edited in place. A replacement draft requires separate review.</p> }
        </form>
        @if(!w.probeConfigured) { <p role="status">BLOCKED_EXTERNAL: configure the approved Sites.Selected credential and an unrelated synthetic site on the server. A Microsoft SharePoint administrator must grant the exact selected site.</p> }
        <label><input type="checkbox" [formControl]="verifyReviewed" /> I reviewed the saved resource revision and want the exact resource and unrelated-site denial checks.</label>
        <button matButton="outlined" (click)="verify()" [disabled]="!canVerify() || !verifyReviewed.value || busy() || uncertain()">Verify selected-site boundary</button>
        <h3>Activate the reviewed workspace</h3>
        <label>Approved client template<select [formControl]="activationTemplate"><option value="">Select an approved version</option>
          @for(t of w.workspace.templates; track t.id) { @if(t.purpose === 'CLIENT_WORKSPACE' && t.approvedAt) { <option [value]="t.id">Version {{ t.version }} · {{ t.manifestDigest.slice(0,12) }}</option> } }
        </select></label>
        <label><input type="checkbox" [formControl]="activateReviewed" /> I reviewed the verified resource and selected approved client template for future workspaces.</label>
        <button matButton="filled" (click)="activate()" [disabled]="!canActivate() || busy() || uncertain()">Activate selected workspace</button>
      } @else { <p>No tenant setup draft exists. Complete initial administrator setup first.</p> }
      <h3>Versioned folder templates</h3><p>Local manifests are immutable after saving. Approval does not create folders or verify Microsoft access. Existing workspaces keep their pinned version. The latest 50 versions of each purpose are shown.</p>
      <form [formGroup]="templateForm" (ngSubmit)="saveTemplate()">
        <label>Template purpose<select formControlName="purpose"><option value="CLIENT_WORKSPACE">Client workspace</option><option value="ENGAGEMENT_WORKSPACE">Engagement workspace</option></select></label>
        <label>Folder manifest<textarea formControlName="manifestJson" rows="8" maxlength="20000"></textarea></label>
        <p>Allowed tokens: CLIENT_CODE, CLIENT_NAME, ENGAGEMENT_CODE, SERVICE and PERIOD. Only nodes, key, name and children are accepted.</p>
        <button matButton type="button" (click)="loadLayout(false)" [disabled]="busy() || uncertain()">Load default layout</button>
        <button matButton type="button" (click)="loadLayout(true)" [disabled]="busy() || uncertain()">Load STE layout</button>
        <label><input type="checkbox" formControlName="reviewed" /> I reviewed this local manifest and its current version.</label>
        <button matButton type="button" (click)="saveTemplateDraft()" [disabled]="!templateScope() || busy() || uncertain()">Save template tab draft</button>
        <button matButton type="button" (click)="recoverTemplateDraft()" [disabled]="!templateScope() || busy() || uncertain()">Recover template tab draft</button>
        <button matButton="filled" [disabled]="templateForm.invalid || busy() || uncertain()">Save template version</button>
      </form>
      <div class="table-scroll"><table><caption>Immutable template versions</caption><thead><tr><th>Purpose</th><th>Version</th><th>Digest</th><th>Approval</th><th>Review</th></tr></thead><tbody>
        @for(t of w.workspace.templates; track t.id) { <tr><td>{{ t.purpose }}</td><td>{{ t.version }}</td><td><code>{{ t.manifestDigest.slice(0,12) }}</code></td><td>{{ t.approvedAt ?? 'Unapproved' }}</td>
          <td><button matButton (click)="reviewTemplate(t.id)" [disabled]="busy() || uncertain()">Review version {{ t.version }} {{ t.purpose }}</button></td></tr> }
      </tbody></table></div>
      @if(reviewedTemplate(); as t) { <section class="panel" aria-label="Template approval review"><h4>Review immutable template</h4>
        <p>{{ t.purpose }} · version {{ t.version }} · {{ t.manifestDigest }}</p><pre>{{ t.manifestJson }}</pre>
        @if(!t.approvedAt) { <label><input type="checkbox" [formControl]="approveReviewed" /> I reviewed this exact immutable manifest and digest.</label>
          <button matButton="filled" (click)="approveTemplate()" [disabled]="!approveReviewed.value || busy() || uncertain()">Approve reviewed template</button> }
        @else { <p>Approved {{ t.approvedAt }}</p> }
      </section> }
    }
    @if(uncertain()) { <p role="status">The outcome is unconfirmed. Refresh persisted state and review it before submitting another action.</p> }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </section>
`, styles: `form { display:grid; gap: .8rem; margin-block:1rem; } label { display:block; } label > input:not([type=checkbox]), select, textarea { display:block; width:100%; box-sizing:border-box; } pre { white-space:pre-wrap; overflow-wrap:anywhere; }` })
export class SharePointAdministration {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly fb = inject(FormBuilder).nonNullable;
  readonly changed = output<void>();
  readonly ws = this.api.resource(() => '/api/ui/administration/microsoft365/resources', selectedResources, 'Current firm-wide Administrator access is required.');
  readonly resourceForm = this.fb.group({ siteUrl: ['', [Validators.required, Validators.maxLength(2000)]], siteId: ['', Validators.required],
    driveId: ['', Validators.required], rootFolderId: ['', Validators.required], accessProfile: ['APP_MEDIATED'], reviewed: [false, Validators.requiredTrue] });
  readonly templateForm = this.fb.group({ purpose: ['CLIENT_WORKSPACE'], manifestJson: ['', [Validators.required, Validators.maxLength(20000)]], reviewed: [false, Validators.requiredTrue] });
  readonly verifyReviewed = this.fb.control(false); readonly activateReviewed = this.fb.control(false); readonly activationTemplate = this.fb.control('');
  readonly approveReviewed = this.fb.control(false); readonly templateId = signal('');
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  readonly editableResource = editableResource; readonly currentVerification = currentVerification;
  private readonly drafts = inject(TabDrafts); private readonly dialog = inject(MatDialog);
  readonly resourceScope = signal<DraftScope | null>(null); readonly templateScope = signal<DraftScope | null>(null);
  private resourceBaseline = JSON.stringify(this.resourceValues()); private templateBaseline = JSON.stringify(this.templateValues());
  private refreshing = false;
  constructor() {
    effect(() => {
      const w = this.ws.data();
      this.resourceForm.reset(); this.templateForm.reset({ purpose: 'CLIENT_WORKSPACE', manifestJson: w?.workspace.clientDefaultManifest ?? '', reviewed: false });
      this.verifyReviewed.reset(); this.activateReviewed.reset(); this.approveReviewed.reset(); this.activationTemplate.reset(); this.templateId.set('');
      this.resourceScope.set(null); this.templateScope.set(null);
      if(w?.workspace.draft) { const d = w.workspace.draft; this.resourceForm.patchValue({ siteUrl: d.siteUrl ?? '', siteId: d.siteId ?? '', driveId: d.driveId ?? '', rootFolderId: d.rootFolderId ?? '', accessProfile: d.accessProfile });
        if(!editableResource(d.state)) this.resourceForm.disable(); else this.resourceForm.enable(); }
      this.resourceForm.controls.reviewed.setValue(false, {emitEvent:false});
      this.templateForm.controls.reviewed.setValue(false, {emitEvent:false});
      this.resourceBaseline = JSON.stringify(this.resourceValues()); this.templateBaseline = JSON.stringify(this.templateValues());
      void this.refreshDraftScopes(w);
      if(w && this.refreshing) { this.uncertain.set(false); this.refreshing = false; this.message.set('Persisted state refreshed. Review it before submitting another action.'); }
    });
    const subscriptions = [ ...(['siteUrl','siteId','driveId','rootFolderId','accessProfile'] as const).map(key =>
      this.resourceForm.controls[key].valueChanges.subscribe(() => this.resourceForm.controls.reviewed.setValue(false, { emitEvent: false }))),
      this.templateForm.controls.purpose.valueChanges.subscribe(() => this.templateForm.controls.reviewed.setValue(false)),
      this.templateForm.controls.manifestJson.valueChanges.subscribe(() => this.templateForm.controls.reviewed.setValue(false)),
      this.activationTemplate.valueChanges.subscribe(() => this.activateReviewed.setValue(false))];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach(s => s.unsubscribe()));
    effect(() => { this.session.invalidation(); this.message.set(''); this.failed.set(false); this.uncertain.set(false); });
  }
  private resourceValues() { const v=this.resourceForm.getRawValue(); return {siteUrl:v.siteUrl,siteId:v.siteId,driveId:v.driveId,rootFolderId:v.rootFolderId,accessProfile:v.accessProfile}; }
  private templateValues() { const v=this.templateForm.getRawValue(); return {purpose:v.purpose,manifestJson:v.manifestJson}; }
  private async refreshDraftScopes(w: ReturnType<typeof selectedResources> | null) {
    if(!w) return;
    const d=w.workspace.draft, owner=d?.id??this.session.current()?.firmId;
    if(!owner) return;
    const resources=d?JSON.stringify([d.id,d.revision,d.state,d.siteUrl,d.siteId,d.driveId,d.rootFolderId,d.accessProfile]):null;
    const templates=JSON.stringify([d?.id??null,d?.revision??null,d?.state??null,w.workspace.templates.map(t=>[t.id,t.purpose,t.version,t.manifestDigest]),
      w.workspace.clientDefaultManifest,w.workspace.engagementDefaultManifest,w.workspace.clientSteManifest,w.workspace.engagementSteManifest]);
    const [resourceHash,templateHash]=await Promise.all([resources?this.digest(resources):Promise.resolve(null),this.digest(templates)]);
    if(this.ws.data()!==w || this.session.current()===null) return;
    this.resourceScope.set(resourceHash?{entity:`sharepoint-resource:${owner}`,baseRevision:resourceHash}:null);
    this.templateScope.set({entity:`sharepoint-template:${owner}`,baseRevision:templateHash});
  }
  private async digest(value:string):Promise<string> { const bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value)); return Array.from(new Uint8Array(bytes),b=>b.toString(16).padStart(2,'0')).join(''); }
  private resourceDraft(raw:unknown):{siteUrl:string;siteId:string;driveId:string;rootFolderId:string;accessProfile:'APP_MEDIATED'|'DIRECT_STAFF_COLLABORATION'}|null {
    if(!raw||typeof raw!=='object'||Array.isArray(raw)) return null; const x=raw as Record<string,unknown>;
    if(Object.keys(x).sort().join(',')!=='accessProfile,driveId,rootFolderId,siteId,siteUrl' ||
      !['siteUrl','siteId','driveId','rootFolderId'].every(k=>typeof x[k]==='string'&&(x[k] as string).length<=2000) ||
      (x['accessProfile']!=='APP_MEDIATED'&&x['accessProfile']!=='DIRECT_STAFF_COLLABORATION')) return null;
    return x as {siteUrl:string;siteId:string;driveId:string;rootFolderId:string;accessProfile:'APP_MEDIATED'|'DIRECT_STAFF_COLLABORATION'};
  }
  private templateDraft(raw:unknown):{purpose:'CLIENT_WORKSPACE'|'ENGAGEMENT_WORKSPACE';manifestJson:string}|null {
    if(!raw||typeof raw!=='object'||Array.isArray(raw)) return null; const x=raw as Record<string,unknown>;
    if(Object.keys(x).sort().join(',')!=='manifestJson,purpose'||typeof x['manifestJson']!=='string'||x['manifestJson'].length>20000||
      (x['purpose']!=='CLIENT_WORKSPACE'&&x['purpose']!=='ENGAGEMENT_WORKSPACE')) return null;
    return x as {purpose:'CLIENT_WORKSPACE'|'ENGAGEMENT_WORKSPACE';manifestJson:string};
  }
  private isResourceDirty(){return JSON.stringify(this.resourceValues())!==this.resourceBaseline;}
  private isTemplateDirty(){return JSON.stringify(this.templateValues())!==this.templateBaseline;}
  saveResourceDraft():boolean { const scope=this.resourceScope(); if(!scope||this.busy()||this.uncertain()||!editableResource(this.ws.data()?.workspace.draft?.state??'')) return false;
    const ok=this.drafts.save(scope,this.resourceValues(),v=>this.resourceDraft(v)); this.failed.set(!ok); this.message.set(ok?'Unsubmitted SharePoint resource fields saved in this tab. Review assent is not retained.':'Tab draft could not be saved.'); return ok; }
  recoverResourceDraft():void { const scope=this.resourceScope(); if(!scope||this.busy()||this.uncertain()||!editableResource(this.ws.data()?.workspace.draft?.state??'')) return;
    const r=this.drafts.read(scope,v=>this.resourceDraft(v)); if(r.state==='ready'&&!r.draft.submissionPending){this.resourceForm.patchValue({...r.draft.value,reviewed:false});this.message.set('SharePoint resource fields recovered. Review them and provide fresh confirmation.');}
    else this.message.set('No compatible SharePoint resource tab draft is available.'); }
  saveTemplateDraft():boolean { const scope=this.templateScope(); if(!scope||this.busy()||this.uncertain()) return false;
    const ok=this.drafts.save(scope,this.templateValues(),v=>this.templateDraft(v)); this.failed.set(!ok); this.message.set(ok?'Unsubmitted folder-template fields saved in this tab. Review assent is not retained.':'Tab draft could not be saved.'); return ok; }
  recoverTemplateDraft():void { const scope=this.templateScope(); if(!scope||this.busy()||this.uncertain()) return;
    const r=this.drafts.read(scope,v=>this.templateDraft(v)); if(r.state==='ready'&&!r.draft.submissionPending){this.templateForm.patchValue({...r.draft.value,reviewed:false});this.message.set('Folder-template fields recovered. Review the exact manifest before saving.');}
    else this.message.set('No compatible folder-template tab draft is available.'); }
  async confirmNavigation(ignore:'resource'|'template'|null=null):Promise<boolean> {
    if(this.busy()) return false; const resourceDirty=ignore!=='resource'&&this.isResourceDirty(), templateDirty=ignore!=='template'&&this.isTemplateDirty();
    if(!resourceDirty&&!templateDirty) return true;
    const choice=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());
    if(choice==='save') { let saved=true; if(resourceDirty) saved=this.saveResourceDraft()&&saved; if(templateDirty) saved=this.saveTemplateDraft()&&saved; return saved; }
    if(choice==='discard') { const entities=[resourceDirty?this.resourceScope()?.entity:null,templateDirty?this.templateScope()?.entity:null].filter((x):x is string=>!!x);
      return entities.every(e=>this.drafts.clear(e)); }
    return false;
  }
  async refresh(): Promise<void> { if(this.busy() || !(await this.confirmNavigation())) return; this.refreshing = true; this.ws.reload(); }
  reviewedTemplate() { return this.ws.data()?.workspace.templates.find(t => t.id === this.templateId()) ?? null; }
  reviewTemplate(id: string): void { if(this.busy() || this.uncertain()) return; this.templateId.set(id); this.approveReviewed.reset(); }
  loadLayout(ste: boolean): void {
    const w = this.ws.data()?.workspace; if(!w || this.busy() || this.uncertain()) return;
    this.templateForm.controls.manifestJson.setValue(this.templateForm.controls.purpose.value === 'CLIENT_WORKSPACE' ?
      ste ? w.clientSteManifest : w.clientDefaultManifest : ste ? w.engagementSteManifest : w.engagementDefaultManifest);
  }
  canVerify(): boolean { const w = this.ws.data(), d = w?.workspace.draft; return !!w?.probeConfigured && !!d && editableResource(d.state) &&
    d.consentState === 'VERIFIED' && !!d.siteId && !!d.driveId && !!d.rootFolderId && !!d.siteUrl; }
  canActivate(): boolean { const w = this.ws.data()?.workspace, d = w?.draft, v = w?.verification; return !!d && d.connectionState === 'VERIFIED' &&
    d.consentState === 'VERIFIED' && !!v && currentVerification(v.state,v.observedAt) && !!this.activationTemplate.value && this.activateReviewed.value; }
  private async run(path: string, body: unknown, success: string): Promise<void> {
    if(this.busy() || this.uncertain()) return; const epoch = this.session.invalidation(); this.busy.set(true);
    try { const result = await this.api.command('/api/ui/administration/microsoft365/' + path, body); if(epoch !== this.session.invalidation()) return;
      this.failed.set(!result.ok); this.message.set(result.ok ? success : result.message);
      if(result.ok) { this.ws.reload(); this.changed.emit(); } else if(result.unknown) this.uncertain.set(true);
    } finally { this.busy.set(false); }
  }
  async saveResource(): Promise<void> { const d = this.ws.data()?.workspace.draft; if(!d || this.resourceForm.invalid || !editableResource(d.state)) return;
    if(!(await this.confirmNavigation('resource'))) return;
    await this.run('resources/save', { ...this.resourceForm.getRawValue(), draftId: d.id, expectedRevision: d.revision }, 'Draft saved. Its selected-site verification is invalidated until the exact boundary is checked again.'); }
  async saveTemplate(): Promise<void> { const w = this.ws.data(); if(!w || this.templateForm.invalid) return;
    if(!(await this.confirmNavigation('template'))) return;
    const expectedVersion = w.workspace.templates.find(t => t.purpose === this.templateForm.controls.purpose.value)?.version ?? '0';
    await this.run('templates/save', { ...this.templateForm.getRawValue(), expectedVersion }, 'Immutable local template version saved. Approval and SharePoint provisioning remain separate.'); }
  async approveTemplate(): Promise<void> { const t = this.reviewedTemplate(); if(!t || t.approvedAt || !this.approveReviewed.value) return;
    if(!(await this.confirmNavigation())) return;
    await this.run('templates/approve', { templateId: t.id, expectedDigest: t.manifestDigest, reviewed: true }, 'Exact local template approved. Microsoft access is still independently verified.'); }
  async verify(): Promise<void> { const d = this.ws.data()?.workspace.draft; if(!d || !this.canVerify() || !this.verifyReviewed.value) return;
    if(!(await this.confirmNavigation())) return;
    await this.run('resources/verify', { draftId:d.id, expectedRevision:d.revision, reviewed:true }, 'Exact resource and unrelated-site denial evidence recorded.'); }
  async activate(): Promise<void> { const d = this.ws.data()?.workspace.draft; if(!d || !this.canActivate()) return;
    if(!(await this.confirmNavigation())) return;
    await this.run('resources/activate', { draftId:d.id, expectedRevision:d.revision, templateId:this.activationTemplate.value, reviewed:true }, 'Selected workspace activated for future clients. Mail, records and production acceptance remain separate.'); }
}
